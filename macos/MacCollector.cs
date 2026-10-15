using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Divoom.HardwareMonitor.Mac;

internal sealed class MacCollector : IDisposable
{
    private readonly MacNative _native = new();
    private readonly SmcReader _smc = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private List<DiskReading> _disks = [];
    private readonly List<string> _diskNotes = [];
    private DateTimeOffset _diskAt;
    private readonly Dictionary<string, (double Time, DateTimeOffset At)> _ioPrevious = new();

    public async Task<HardwareSnapshot> ReadAsync(CancellationToken token)
    {
        var notes = new List<string> { _smc.Status, "CPU 温度键: " + string.Join(",", _smc.CpuKeys), "GPU 温度键: " + string.Join(",", _smc.GpuKeys) };
        Metric cpu = _native.CpuLoad(), cpuTemp = _smc.CpuTemperature();
        var memory = _native.Memory();
        var gpuTask = Command("/usr/sbin/ioreg", ["-a", "-r", "-c", "IOAccelerator"], token);
        var diskTask = Command("/usr/sbin/ioreg", ["-a", "-r", "-c", "IOBlockStorageDriver"], token);
        // Both commands drain stdout/stderr concurrently and have bounded lifetimes.
        var gpus = new List<GpuReading>();
        try { gpus = MacParsing.Gpus(await gpuTask); }
        catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested) { notes.Add("GPU 采集失败: " + e.Message); }
        if (gpus.Count == 1 && !gpus[0].Temperature.IsAvailable(DateTimeOffset.UtcNow))
        {
            // SMC does not expose a PCI identity. Only associate it with a single GPU.
            Metric temperature = _smc.GpuTemperature();
            if (temperature.IsAvailable(DateTimeOffset.UtcNow)) gpus[0] = gpus[0] with { Temperature = temperature };
        }
        if (gpus.Count == 0) notes.Add("IOKit 未枚举到可唯一标识的显卡，GPU 指标不可用");
        if (DateTimeOffset.UtcNow - _diskAt >= TimeSpan.FromSeconds(30)) await RefreshDisks(token);
        notes.AddRange(_diskNotes);
        var disks = _disks.Select(d => d with { Activity = Metric.Missing("%", "IOKit", "驱动未开放磁盘服务时间计数器") }).ToList();
        try
        {
            var tree = MacParsing.Plist(await diskTask);
            foreach (var entry in MacParsing.Dictionaries(tree))
            {
                if (entry.GetValueOrDefault("Statistics") is not Dictionary<string, object?> stats) continue;
                double? read = MacParsing.Number(stats, "Total Time (Read)"), write = MacParsing.Number(stats, "Total Time (Write)");
                var media = MacParsing.Dictionaries(entry).Where(d => d.GetValueOrDefault("Whole") is true).Select(d => MacParsing.Text(d, "BSD Name")).Distinct().ToArray();
                if (read is null || write is null || media.Length != 1) continue;
                string id = media[0]; int index = disks.FindIndex(d => d.Id == id);
                if (index < 0) continue;
                double total = read.Value + write.Value; var now = DateTimeOffset.UtcNow;
                if (_ioPrevious.TryGetValue(id, out var last) && total >= last.Time && now > last.At)
                    disks[index] = disks[index] with { Activity = Metric.Percent((total - last.Time) / (now - last.At).TotalNanoseconds * 100, "IOKit · 读写服务时间占比") };
                _ioPrevious[id] = (total, now);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested) { notes.Add("磁盘活动采集失败: " + e.Message); }
        var volumes = new List<VolumeReading>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.Name == "/" || d.Name.StartsWith("/Volumes/", StringComparison.Ordinal)))
        {
            try { if (drive.IsReady) volumes.Add(new(drive.Name, drive.TotalSize, drive.AvailableFreeSpace, null)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { notes.Add(drive.Name + " 容量读取失败: " + e.Message); }
        }
        return new(DateTimeOffset.UtcNow, _native.CpuName, cpu, cpuTemp, memory.Load, _native.TotalMemory, memory.Available,
            gpus, disks, volumes, notes.Distinct().ToList());
    }
    private async Task RefreshDisks(CancellationToken token)
    {
        _diskAt = DateTimeOffset.UtcNow; _diskNotes.Clear();
        var next = new List<DiskReading>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            var list = MacParsing.Plist(await Command("/usr/sbin/diskutil", ["list", "-plist", "physical"], budget.Token));
            var ids = MacParsing.Dictionaries(list).Select(d => MacParsing.Text(d, "DeviceIdentifier")).Where(id => Regex.IsMatch(id, @"^disk\d+$")).Distinct().ToArray();
            string? smart = FindSmartctl(_settings);
            if (_settings.SmartctlMode != SmartctlMode.Disabled && smart is null) _diskNotes.Add("未找到 macOS smartctl；磁盘温度不可用。可安装 smartmontools，或在采集设置指定路径。");
            foreach (string id in ids)
            {
                int index = int.Parse(id[4..], CultureInfo.InvariantCulture);
                string name = id;
                try
                {
                    var info = MacParsing.Plist(await Command("/usr/sbin/diskutil", ["info", "-plist", id], budget.Token)) as Dictionary<string, object?> ?? [];
                    name = MacParsing.Text(info, "MediaName", id);
                }
                catch (Exception e) when (!token.IsCancellationRequested) { _diskNotes.Add(id + " 型号读取失败: " + e.Message); }
                Metric temp = Metric.Missing("℃", "smartctl", smart is null ? "未启用或未找到 smartctl" : "等待采样");
                if (smart is not null)
                {
                    try { temp = MacParsing.SmartTemperature(await Command(smart, ["-a", "-j", "/dev/" + id], budget.Token, allowNonzero: true)); }
                    catch (Exception e) when (!token.IsCancellationRequested) { temp = Metric.Missing("℃", "smartctl", e is OperationCanceledException ? "SMART 采集超时" : e.Message); }
                }
                next.Add(new(id, index, name, Metric.Missing("%", "IOKit", "等待采样"), temp));
                if (budget.IsCancellationRequested) break;
            }
            _disks = next;
        }
        catch (Exception e) when (!token.IsCancellationRequested) { _diskNotes.Add("磁盘枚举失败: " + (e is OperationCanceledException ? "超时" : e.Message)); }
    }
    public static string? FindSmartctl(AppSettings settings)
    {
        if (settings.SmartctlMode == SmartctlMode.Disabled) return null;
        if (settings.SmartctlMode == SmartctlMode.Custom) return File.Exists(settings.SmartctlPath) ? settings.SmartctlPath : null;
        return new[] { Path.Combine(AppContext.BaseDirectory, "tools", "smartctl"), "/opt/homebrew/sbin/smartctl", "/opt/homebrew/bin/smartctl", "/usr/local/sbin/smartctl", "/usr/local/bin/smartctl", "/usr/sbin/smartctl" }.FirstOrDefault(File.Exists);
    }
    private static async Task<string> Command(string executable, string[] args, CancellationToken token, bool allowNonzero = false)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var process = new Process { StartInfo = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["LC_ALL"] = "C";
        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token); var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            string result = await stdout, error = await stderr;
            if (!allowNonzero && process.ExitCode != 0) throw new IOException(Path.GetFileName(executable) + $" 返回 {process.ExitCode}: " + error[..Math.Min(error.Length, 1000)]);
            if (result.Length > 8_000_000) throw new IOException("采集输出超过大小限制");
            return result;
        }
        finally { try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } } catch (InvalidOperationException) { } }
    }
    public void Dispose() { _smc.Dispose(); _native.Dispose(); }
}
