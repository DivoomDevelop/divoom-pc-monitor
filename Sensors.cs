using System.Diagnostics;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Principal;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace Divoom.HardwareMonitor;

public sealed class LibreProvider : IHardwareProvider
{
    private readonly Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsStorageEnabled = true };
    private readonly bool _pawnInstalled;
    public string Name => "LibreHardwareMonitor 0.9.6";
    public LibreProvider()
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        _pawnInstalled = key?.GetValue("DisplayVersion") is string;
        _computer.Open();
    }
    public SensorFrame Read()
    {
        var readings = new List<SensorReading>();
        var notes = new List<string>();
        if (!_pawnInstalled) notes.Add("PawnIO 未安装：CPU 等低层温度可能不可用；可在设置中打开官方安装页面。");
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) notes.Add("当前为普通权限；部分传感器可能需要管理员身份。");
        foreach (var hardware in _computer.Hardware)
        {
            try
            {
                Update(hardware);
                var sensors = Flatten(hardware).ToArray();
                var temperatures = sensors.Where(x => x.SensorType == SensorType.Temperature && x.Value is > 0 and < 150).ToArray();
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    var selected = ChooseCpuTemperature(temperatures.Select(x => (x.Name, (double)x.Value!.Value)));
                    Metric? temp = selected is { } value ? Metric.Create(value.Value, "℃", Name + " · " + value.Name) : null;
                    readings.Add(new("cpu", hardware.Identifier.ToString(), hardware.Name, "", null, temp));
                }
                else if (hardware.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                {
                    var core = temperatures.FirstOrDefault(x => x.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase));
                    var hotspot = temperatures.FirstOrDefault(x => x.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase));
                    var load = sensors.FirstOrDefault(x => x.SensorType == SensorType.Load && x.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) && x.Value is not null);
                    string? device = hardware.GetType().GetProperty("DeviceId", BindingFlags.Instance | BindingFlags.Public)?.GetValue(hardware)?.ToString();
                    readings.Add(new("gpu", hardware.Identifier.ToString(), hardware.Name, HardwareIdentity.PciKey(device),
                        load?.Value is { } lv ? Metric.Percent(lv, Name + " · GPU Core") : null,
                        core?.Value is { } tv ? Metric.Create(tv, "℃", Name + " · GPU Core") : null,
                        hotspot?.Value is { } hv ? Metric.Create(hv, "℃", Name + " · Hot Spot") : null));
                }
                else if (hardware.HardwareType == HardwareType.Storage)
                {
                    string last = hardware.Identifier.ToString().Split('/')[^1];
                    if (int.TryParse(last, out int index))
                    {
                        var temperature = temperatures.FirstOrDefault(x => x.Name.Equals("Temperature", StringComparison.OrdinalIgnoreCase)) ?? temperatures.FirstOrDefault(x => !x.Name.Contains("2"));
                        readings.Add(new("disk", "disk/" + index, hardware.Name, "", null,
                            temperature?.Value is { } t ? Metric.Create(t, "℃", Name + " · " + temperature.Name) : null));
                    }
                }
            }
            catch (Exception e) { notes.Add(hardware.Name + ": " + e.Message); }
        }
        return new(DateTimeOffset.UtcNow, readings, notes);
    }
    public static (string Name, double Value)? ChooseCpuTemperature(IEnumerable<(string Name, double Value)> temperatures)
    {
        var values = temperatures.Where(x => double.IsFinite(x.Value) && x.Value is > 0 and < 150).ToList();
        foreach (string preferred in new[] { "CPU Package", "Core (Tdie)", "Core (Tctl/Tdie)", "Core (Tctl)" })
        {
            var found = values.Where(x => x.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count > 0) return (preferred, found.Max(x => x.Value));
        }
        var cores = values.Where(x => x.Name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase)).ToList();
        return cores.Count > 0 ? ("最高核心温度", cores.Max(x => x.Value)) : null;
    }
    private static void Update(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware) Update(sub);
    }
    private static IEnumerable<ISensor> Flatten(IHardware hardware) => hardware.Sensors.Concat(hardware.SubHardware.SelectMany(Flatten));
    public void Dispose() => _computer.Close();
}

public sealed class AdlxProvider : IHardwareProvider
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)] private struct AdlxMetric
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string Pnp;
        public double Usage, Temperature, Hotspot;
        public uint Valid;
    }
    [DllImport("Divoom.AdlxBridge.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int AdlxOpen();
    [DllImport("Divoom.AdlxBridge.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int AdlxRead([Out] AdlxMetric[] metrics, int capacity);
    [DllImport("Divoom.AdlxBridge.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void AdlxClose();
    private readonly int _status;
    public string Name => "AMD ADLX";
    public AdlxProvider() => _status = AdlxOpen();
    public SensorFrame Read()
    {
        if (_status != 0) return new(DateTimeOffset.UtcNow, [], [$"ADLX 未启用（状态 {_status}）：无兼容 AMD 驱动或设备。"]);
        var buffer = new AdlxMetric[32];
        int count = AdlxRead(buffer, buffer.Length);
        if (count < 0) return new(DateTimeOffset.UtcNow, [], ["ADLX 采样失败: " + count]);
        return new(DateTimeOffset.UtcNow, buffer.Take(Math.Min(count, buffer.Length)).Select((x, i) => new SensorReading("gpu", "adlx/" + i, x.Name,
            HardwareIdentity.PciKey(x.Pnp), (x.Valid & 1) != 0 ? Metric.Percent(x.Usage, Name) : null,
            (x.Valid & 2) != 0 && x.Temperature is > 0 and < 150 ? Metric.Create(x.Temperature, "℃", Name + " · Core") : null,
            (x.Valid & 4) != 0 ? Metric.Create(x.Hotspot, "℃", Name + " · Hotspot") : null)).ToList(), []);
    }
    public void Dispose() { if (_status == 0) AdlxClose(); }
}

public static class SmartctlParser
{
    public static Metric Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("smartctl", out var ctl) && ctl.TryGetProperty("exit_status", out var status) && (status.GetInt32() & 7) != 0)
            return Metric.Missing("℃", "smartctl", "无法打开磁盘、参数错误或 SMART 命令失败");
        double value;
        if (root.TryGetProperty("temperature", out var temperature) && temperature.TryGetProperty("current", out var current) && current.TryGetDouble(out value))
            return value is > 0 and < 150 ? Metric.Create(value, "℃", "smartctl · SMART") : Metric.Missing("℃", "smartctl", "温度数值无效");
        return Metric.Missing("℃", "smartctl", "磁盘未提供温度");
    }
}

public sealed class SmartctlProvider : IHardwareProvider
{
    private readonly string _path;
    private readonly Func<List<(int Index, string Name)>> _enumerateDisks;
    private SensorFrame _cached = new(DateTimeOffset.MinValue, [], []);
    public string Name => "smartctl";
    public SmartctlProvider(string path, Func<List<(int Index, string Name)>>? enumerateDisks = null)
    {
        _path = Path.GetFullPath(path);
        _enumerateDisks = enumerateDisks ?? EnumerateDisks;
    }
    private static List<(int Index, string Name)> EnumerateDisks()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Index, Model FROM Win32_DiskDrive");
        using var disks = searcher.Get();
        return disks.Cast<ManagementBaseObject>().Select(x => (Convert.ToInt32(x["Index"]), x["Model"]?.ToString() ?? "Disk")).ToList();
    }
    public SensorFrame Read() => ReadMissing([]);
    public SensorFrame ReadMissing(IReadOnlyCollection<SensorReading> primary)
    {
        if (DateTimeOffset.UtcNow - _cached.SampledAt < TimeSpan.FromSeconds(30)) return _cached;
        var readings = new List<SensorReading>();
        var notes = new List<string>();
        if (!File.Exists(_path)) return _cached = new(DateTimeOffset.UtcNow, [], ["smartctl 路径不存在: " + _path]);
        using var totalTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        foreach (var disk in _enumerateDisks())
        {
            int index = disk.Index;
            if (primary.Any(x => x.Kind == "disk" && x.Id == "disk/" + index && x.Temperature?.IsAvailable(DateTimeOffset.UtcNow) == true)) continue;
            using var process = new Process { StartInfo = new(_path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (var arg in new[] { "-a", "-j", "/dev/pd" + index }) process.StartInfo.ArgumentList.Add(arg);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(totalTimeout.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                process.Start();
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
                var json = stdout.GetAwaiter().GetResult();
                _ = stderr.GetAwaiter().GetResult();
                var metric = SmartctlParser.Parse(json);
                readings.Add(new("disk", "disk/" + index, disk.Name, "", null, metric));
                if (metric.Error is not null) notes.Add($"smartctl 磁盘 {index}: {metric.Error}");
            }
            catch (Exception e)
            {
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                notes.Add($"smartctl 磁盘 {index}: {e.Message}");
            }
            if (totalTimeout.IsCancellationRequested) break;
        }
        return _cached = new(DateTimeOffset.UtcNow, readings, notes);
    }
    public void Dispose() { }
}

public static class SensorWorker
{
    public static int Run(int parentPid)
    {
        using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var providers = new List<IHardwareProvider>();
        var initializationNotes = new List<string>();
        // Emit a heartbeat before native library initialization; the parent has a bounded watchdog.
        writer.WriteLine(JsonSerializer.Serialize(new SensorFrame(DateTimeOffset.UtcNow, [], ["正在初始化温度采集器…"])));
        bool Add(Func<IHardwareProvider> create)
        {
            try { providers.Add(create()); return true; } catch (Exception e) { initializationNotes.Add("采集器初始化失败: " + e.Message); return false; }
        }
        Add(() => new LibreProvider());
        Add(() => new AdlxProvider());
        var settings = AppSettings.Load();
        if (settings.EnableExternalProviders)
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "providers");
            if (Directory.Exists(directory))
                foreach (string file in Directory.GetFiles(directory, "*.Provider.dll"))
                {
                    try
                    {
                        var assembly = new ProviderLoadContext(file).LoadFromAssemblyPath(Path.GetFullPath(file));
                        foreach (var type in assembly.GetTypes().Where(x => !x.IsAbstract && typeof(IHardwareProvider).IsAssignableFrom(x)))
                            Add(() => (IHardwareProvider)Activator.CreateInstance(type)!);
                    }
                    catch (Exception e) { initializationNotes.Add(Path.GetFileName(file) + ": " + e.Message); }
                }
        }
        if (settings.SmartctlMode != SmartctlMode.Disabled)
        {
            if (Add(() => new SmartctlProvider(SmartctlRuntime.Resolve(settings))))
                initializationNotes.Add(settings.SmartctlMode == SmartctlMode.Automatic ? "磁盘温度自动补充：内置 smartctl 7.5，仅查询缺失温度的磁盘。" : "磁盘温度自动补充：自定义 smartctl，仅查询缺失温度的磁盘。");
        }
        try
        {
            while (true)
            {
                if (parentPid > 0)
                {
                    try { using var parent = Process.GetProcessById(parentPid); if (parent.HasExited) break; }
                    catch (ArgumentException) { break; }
                }
                var readings = new List<SensorReading>();
                var notes = new List<string>(initializationNotes);
                foreach (var provider in providers)
                {
                    try
                    {
                        var frame = provider is SmartctlProvider smart ? smart.ReadMissing(readings) : provider.Read();
                        notes.AddRange(frame.Diagnostics);
                        SensorMerge.Add(readings, frame.Readings, DateTimeOffset.UtcNow);
                    }
                    catch (Exception e) { notes.Add(provider.Name + ": " + e.Message); }
                }
                writer.WriteLine(JsonSerializer.Serialize(new SensorFrame(DateTimeOffset.UtcNow, readings, notes.Distinct().ToList())));
                Thread.Sleep(2000);
            }
        }
        catch (IOException) { /* Parent exited and closed the pipe. */ }
        finally { foreach (var provider in providers) { try { provider.Dispose(); } catch { } } }
        return 0;
    }
    private sealed class ProviderLoadContext(string file) : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver = new(Path.GetFullPath(file));
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name == typeof(IHardwareProvider).Assembly.GetName().Name) return typeof(IHardwareProvider).Assembly;
            var path = _resolver.ResolveAssemblyToPath(name);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
        protected override IntPtr LoadUnmanagedDll(string name)
        {
            string? path = _resolver.ResolveUnmanagedDllToPath(name);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
