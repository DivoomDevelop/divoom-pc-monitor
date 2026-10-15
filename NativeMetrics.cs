using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace Divoom.HardwareMonitor;

public sealed class NativeMetrics : IDisposable
{
    private readonly PdhQuery _pdh = new();
    private readonly IntPtr _cpuCounter, _gpuCounter, _diskCounter;
    private ulong _lastIdle, _lastKernel, _lastUser;
    private bool _hasCpuBaseline;
    private string _cpuName = "CPU";
    private List<GpuReading> _gpus = [];
    private List<DiskReading> _disks = [];
    private List<VolumeReading> _volumes = [];
    private DateTimeOffset _inventoryAt;
    private readonly List<string> _inventoryNotes = [];

    public NativeMetrics()
    {
        _cpuCounter = _pdh.Add(@"\Processor Information(_Total)\% Processor Time");
        _gpuCounter = _pdh.Add(@"\GPU Engine(*)\Utilization Percentage");
        _diskCounter = _pdh.Add(@"\PhysicalDisk(*)\% Idle Time");
        _pdh.Collect();
        Native.GetSystemTimes(out _lastIdle, out _lastKernel, out _lastUser);
        RefreshInventory();
    }

    private void RefreshInventory()
    {
        _inventoryNotes.Clear();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            using var values = searcher.Get();
            _cpuName = string.Join(" / ", values.Cast<ManagementBaseObject>().Select(x => x["Name"]?.ToString()?.Trim()).Where(x => !string.IsNullOrEmpty(x)));
        }
        catch (Exception e) { _inventoryNotes.Add("CPU 型号读取失败: " + e.Message); }
        try { _gpus = DxgiInventory.Read(); }
        catch (Exception e) { _inventoryNotes.Add("DXGI 显卡枚举失败: " + e.Message); }
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Index, Model FROM Win32_DiskDrive");
            using var values = searcher.Get();
            _disks = values.Cast<ManagementBaseObject>().Select(x => new DiskReading("disk/" + x["Index"], Convert.ToInt32(x["Index"]),
                x["Model"]?.ToString()?.Trim() ?? "Disk", Metric.Missing("%", "Windows PDH", "等待采样"), Metric.Missing("℃", "传感器", "等待采样"))).ToList();
        }
        catch (Exception e) { _inventoryNotes.Add("磁盘枚举失败: " + e.Message); }
        _volumes = [];
        foreach (var drive in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed))
        {
            try
            {
                if (drive.IsReady) _volumes.Add(new(drive.Name, drive.TotalSize, drive.TotalFreeSpace, Native.VolumeDisk(drive.Name)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _inventoryNotes.Add(drive.Name + " 容量不可用"); }
        }
        _inventoryAt = DateTimeOffset.UtcNow;
    }

    public HardwareSnapshot Read(SensorFrame? sensors, string workerStatus)
    {
        if (DateTimeOffset.UtcNow - _inventoryAt > TimeSpan.FromSeconds(30)) RefreshInventory();
        _pdh.Collect();
        var now = DateTimeOffset.UtcNow;
        var notes = new List<string>(_inventoryNotes);
        if (!string.IsNullOrEmpty(workerStatus)) notes.Add(workerStatus);
        if (sensors is not null) notes.AddRange(sensors.Diagnostics);
        var fallbackCpu = CpuFallback();
        Metric cpu = _pdh.Scalar(_cpuCounter) is { } cpuValue ? Metric.Percent(cpuValue, "Windows PDH · Processor Time") : fallbackCpu;
        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        bool memoryOk = Native.GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0;
        var memLoad = memoryOk ? Metric.Percent((memory.TotalPhysical - memory.AvailablePhysical) * 100.0 / memory.TotalPhysical, "GlobalMemoryStatusEx") : Metric.Missing("%", "GlobalMemoryStatusEx", "物理内存调用失败");
        var gpuLoads = AggregateGpuEngines(_pdh.Array(_gpuCounter));
        var diskLoads = _pdh.Array(_diskCounter);
        var sensorReadings = sensors?.Readings ?? [];
        var physicalGpus = HardwareIdentity.PhysicalGpus(_gpus, gpuLoads);
        if (physicalGpus.Count < _gpus.Count) notes.Add("已合并同一 PCI 地址的显卡镜像适配器。");
        var gpus = physicalGpus.Select(gpu =>
        {
            var sensor = HardwareIdentity.MatchGpu(gpu, physicalGpus, sensorReadings);
            var load = gpuLoads.TryGetValue(gpu.Id.Split('/')[^1], out var value)
                ? Metric.Percent(value, "Windows GPU Engine · 最忙引擎")
                : sensor?.Load ?? Metric.Missing("%", "Windows GPU Engine", "驱动不支持或计数器不可用");
            var temp = sensor?.Temperature ?? Metric.Missing("℃", "传感器", "未取得可匹配的核心温度");
            return gpu with { Load = load, Temperature = temp };
        }).ToList();
        var disks = _disks.Select(disk =>
        {
            var activity = diskLoads.FirstOrDefault(x => x.Key.Split(' ')[0] == disk.Index.ToString());
            var sensor = sensorReadings.FirstOrDefault(x => x.Kind == "disk" && x.Id == disk.Id);
            return disk with { Activity = activity.Key is not null ? Metric.Percent(100 - activity.Value, "Windows PDH · 100 − Idle Time") : Metric.Missing("%", "Windows PDH", "磁盘活动计数器不可用"),
                Temperature = sensor?.Temperature ?? Metric.Missing("℃", "传感器", "未取得磁盘温度") };
        }).ToList();
        var cpuSensor = sensorReadings.FirstOrDefault(x => x.Kind == "cpu");
        if (cpu.Error is not null) notes.Add("CPU: " + cpu.Error);
        return new(now, _cpuName, cpu, cpuSensor?.Temperature ?? Metric.Missing("℃", "传感器", "需要可访问的温度传感器"), memLoad,
            memoryOk ? (long)memory.TotalPhysical : 0, memoryOk ? (long)memory.AvailablePhysical : 0, gpus, disks, _volumes.ToList(), notes.Distinct().ToList());
    }

    public static Dictionary<string, double> AggregateGpuEngines(IEnumerable<KeyValuePair<string, double>> samples)
    {
        var engines = new Dictionary<(string Luid, string Engine), double>();
        foreach (var sample in samples)
        {
            var match = Regex.Match(sample.Key, @"(luid_0x[0-9a-f]+_0x[0-9a-f]+)_(phys_\d+_eng_\d+)", RegexOptions.IgnoreCase);
            if (!match.Success || !double.IsFinite(sample.Value) || sample.Value < 0) continue;
            var key = (match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value.ToLowerInvariant());
            engines[key] = engines.GetValueOrDefault(key) + sample.Value;
        }
        return engines.GroupBy(x => x.Key.Luid).ToDictionary(x => x.Key, x => Math.Clamp(x.Max(y => y.Value), 0, 100));
    }

    private Metric CpuFallback()
    {
        if (Environment.ProcessorCount > 64) return Metric.Missing("%", "Windows", "PDH 不可用；超过 64 线程时不使用单处理器组回退");
        if (!Native.GetSystemTimes(out var idle, out var kernel, out var user)) return Metric.Missing("%", "GetSystemTimes", "调用失败");
        ulong total = (kernel - _lastKernel) + (user - _lastUser), idleDelta = idle - _lastIdle;
        bool valid = _hasCpuBaseline && total > 0 && idleDelta <= total;
        _lastIdle = idle; _lastKernel = kernel; _lastUser = user; _hasCpuBaseline = true;
        return valid ? Metric.Percent((total - idleDelta) * 100.0 / total, "GetSystemTimes") : Metric.Missing("%", "GetSystemTimes", "等待下一次差分采样");
    }
    public void Dispose() => _pdh.Dispose();
}

internal sealed class PdhQuery : IDisposable
{
    private IntPtr _query;
    public PdhQuery() => PdhOpenQuery(null, 0, out _query);
    public IntPtr Add(string path) => _query != IntPtr.Zero && PdhAddEnglishCounter(_query, path, 0, out var counter) == 0 ? counter : IntPtr.Zero;
    public void Collect() { if (_query != IntPtr.Zero) PdhCollectQueryData(_query); }
    public double? Scalar(IntPtr counter) => counter != IntPtr.Zero && PdhGetFormattedCounterValue(counter, 0x200, out _, out var value) == 0 && value.Status <= 1 && double.IsFinite(value.Value) ? value.Value : null;
    public Dictionary<string, double> Array(IntPtr counter)
    {
        var result = new Dictionary<string, double>();
        if (counter == IntPtr.Zero) return result;
        uint bytes = 0, count = 0;
        uint status = PdhGetFormattedCounterArray(counter, 0x200, ref bytes, ref count, IntPtr.Zero);
        if (status != 0x800007D2 || bytes == 0 || bytes > 16_000_000) return result;
        var buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArray(counter, 0x200, ref bytes, ref count, buffer) != 0) return result;
            int size = Marshal.SizeOf<CounterItem>();
            for (int i = 0; i < count && (i + 1L) * size <= bytes; i++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(IntPtr.Add(buffer, i * size));
                if (item.Value.Status <= 1 && double.IsFinite(item.Value.Value) && Marshal.PtrToStringUni(item.Name) is { } name) result[name] = item.Value.Value;
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }
    public void Dispose() { if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; } }
    [StructLayout(LayoutKind.Sequential)] private struct CounterValue { public uint Status; public double Value; }
    [StructLayout(LayoutKind.Sequential)] private struct CounterItem { public IntPtr Name; public CounterValue Value; }
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhOpenQueryW")] private static extern uint PdhOpenQuery(string? source, nuint data, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, nuint data, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhGetFormattedCounterArrayW")] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bytes, ref uint count, IntPtr buffer);
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputBytes, byte[] output, uint outputBytes, out uint returned, IntPtr overlapped);
    internal static int? VolumeDisk(string volume)
    {
        using var handle = CreateFile(@"\\.\" + volume.TrimEnd('\\'), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        byte[] data = new byte[4096];
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x00560000, IntPtr.Zero, 0, data, (uint)data.Length, out var returned, IntPtr.Zero) || returned < 32) return null;
        // Multi-extent/spanned volumes have no single physical-disk temperature.
        return BitConverter.ToUInt32(data, 0) == 1 ? BitConverter.ToInt32(data, 8) : null;
    }
}

internal static class DxgiInventory
{
    [StructLayout(LayoutKind.Sequential)] private struct OpenAdapter { public uint LuidLow; public int LuidHigh; public uint Handle; }
    [StructLayout(LayoutKind.Sequential)] private struct AdapterQuery { public uint Handle, Type; public IntPtr Data; public uint Size; }
    [DllImport("gdi32.dll")] private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapter adapter);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref AdapterQuery query);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref uint handle);
    private static string Location(uint low, int high)
    {
        var adapter = new OpenAdapter { LuidLow = low, LuidHigh = high };
        if (D3DKMTOpenAdapterFromLuid(ref adapter) != 0) return "";
        var buffer = Marshal.AllocHGlobal(12);
        try
        {
            var typeQuery = new AdapterQuery { Handle = adapter.Handle, Type = 15, Data = buffer, Size = 4 };
            if (D3DKMTQueryAdapterInfo(ref typeQuery) == 0 && (Marshal.ReadInt32(buffer) & 0x44) != 0) return "virtual";
            var query = new AdapterQuery { Handle = adapter.Handle, Type = 6, Data = buffer, Size = 12 };
            if (D3DKMTQueryAdapterInfo(ref query) != 0) return "";
            uint bus = unchecked((uint)Marshal.ReadInt32(buffer)), device = unchecked((uint)Marshal.ReadInt32(buffer, 4)), function = unchecked((uint)Marshal.ReadInt32(buffer, 8));
            return bus <= 255 && device <= 31 && function <= 7 ? $"{bus:X2}:{device:X2}.{function}" : "";
        }
        finally { Marshal.FreeHGlobal(buffer); D3DKMTCloseAdapter(ref adapter.Handle); }
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapter(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr self, out AdapterDescription desc);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Vendor, Device, Subsystem, Revision;
        public nuint DedicatedVideo, DedicatedSystem, SharedSystem;
        public uint LuidLow; public int LuidHigh; public uint Flags;
    }
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(in Guid iid, out IntPtr factory);
    public static List<GpuReading> Read()
    {
        var result = new List<GpuReading>();
        Guid iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(in iid, out var factory));
        try
        {
            var enumerate = Marshal.GetDelegateForFunctionPointer<EnumAdapter>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 12 * IntPtr.Size));
            for (uint i = 0; i < 32; i++)
            {
                if (enumerate(factory, i, out var adapter) != 0) break;
                try
                {
                    var getDesc = Marshal.GetDelegateForFunctionPointer<GetDescription>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(adapter), 10 * IntPtr.Size));
                    if (getDesc(adapter, out var desc) != 0 || (desc.Flags & 2) != 0 || desc.Vendor is not (0x8086 or 0x10DE or 0x1002)) continue;
                    string location = Location(desc.LuidLow, desc.LuidHigh);
                    if (location == "virtual") continue;
                    string luid = $"luid_0x{unchecked((uint)desc.LuidHigh):x8}_0x{desc.LuidLow:x8}";
                    result.Add(new($"gpu/{desc.Vendor:x4}-{desc.Device:x4}-{desc.Subsystem:x8}/{luid}", desc.Description,
                        $"{desc.Vendor:X4}:{desc.Device:X4}:{desc.Subsystem:X8}", desc.DedicatedVideo > 512 * 1024 * 1024,
                        Metric.Missing("%", "Windows GPU Engine", "等待采样"), Metric.Missing("℃", "传感器", "等待采样"), location));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }
}
