using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Divoom.HardwareMonitor;

public sealed record Metric(double? Value, string Unit, string Source, DateTimeOffset SampledAt, string? Error = null)
{
    public bool IsAvailable(DateTimeOffset now, int maxAgeSeconds = 0) => Value is { } v && double.IsFinite(v)
        && now >= SampledAt - TimeSpan.FromSeconds(2)
        && now - SampledAt <= TimeSpan.FromSeconds(maxAgeSeconds > 0 ? maxAgeSeconds : Source.StartsWith("smartctl", StringComparison.OrdinalIgnoreCase) ? 65 : 10);
    public string Display(DateTimeOffset now, int decimals = 1) => IsAvailable(now)
        ? Value!.Value.ToString("F" + decimals, CultureInfo.InvariantCulture) + Unit : "--";
    public static Metric Missing(string unit, string source, string error) => new(null, unit, source, DateTimeOffset.UtcNow, error);
    public static Metric Create(double value, string unit, string source) => double.IsFinite(value)
        ? new(value, unit, source, DateTimeOffset.UtcNow) : Missing(unit, source, "数据无效");
    public static Metric Percent(double value, string source) => Create(Math.Clamp(value, 0, 100), "%", source);
}

public sealed record GpuReading(string Id, string Name, string MatchKey, bool IsDiscrete, Metric Load, Metric Temperature, string PhysicalLocation = "");
public sealed record VolumeReading(string Name, long TotalBytes, long FreeBytes, int? DiskIndex)
{
    public double UsedPercent => TotalBytes > 0 ? (TotalBytes - FreeBytes) * 100.0 / TotalBytes : 0;
}
public sealed record DiskReading(string Id, int Index, string Name, Metric Activity, Metric Temperature);
public sealed record HardwareSnapshot(DateTimeOffset SampledAt, string CpuName, Metric CpuLoad, Metric CpuTemperature,
    Metric MemoryLoad, long TotalMemoryBytes, long AvailableMemoryBytes, List<GpuReading> Gpus,
    List<DiskReading> Disks, List<VolumeReading> Volumes, List<string> Diagnostics);

// Public extension contract: a licensed SDK adapter can implement this in a provider assembly.
// Adapters execute only in the isolated sensor worker, never on the UI thread.
public interface IHardwareProvider : IDisposable
{
    string Name { get; }
    SensorFrame Read();
}
public sealed record SensorReading(string Kind, string Id, string Name, string MatchKey, Metric? Load, Metric? Temperature, Metric? Hotspot = null);
public sealed record SensorFrame(DateTimeOffset SampledAt, List<SensorReading> Readings, List<string> Diagnostics);

public enum SmartctlMode { Automatic, Disabled, Custom }

public sealed class AppSettings
{
    public string Language { get; set; } = "auto";
    public string DeviceIp { get; set; } = "";
    public int DeviceId { get; set; }
    public int DeviceHardware { get; set; }
    public bool TimeGate { get; set; }
    public int LcdIndex { get; set; }
    public int LcdIndependence { get; set; }
    public string GpuId { get; set; } = "";
    public string DiskId { get; set; } = "";
    public SmartctlMode SmartctlMode { get; set; } = SmartctlMode.Automatic;
    public string SmartctlPath { get; set; } = "";
    public bool EnableExternalProviders { get; set; }
    public static string DataDirectory => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Divoom", "HardwareMonitor")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Divoom", "HardwareMonitor");
    public static JsonSerializerOptions JsonOptions { get; } = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static AppSettings Load()
    {
        try { return Parse(File.ReadAllText(Path.Combine(DataDirectory, "settings.json"))); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public static AppSettings Parse(string json)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty("DeviceHardware", out _) && settings.TimeGate) settings.DeviceHardware = 400;
        if (document.RootElement.ValueKind == JsonValueKind.Object && !document.RootElement.TryGetProperty("SmartctlMode", out _)
            && document.RootElement.TryGetProperty("EnableSmartctl", out var legacy) && legacy.ValueKind == JsonValueKind.True
            && !string.IsNullOrWhiteSpace(settings.SmartctlPath)) settings.SmartctlMode = SmartctlMode.Custom;
        return settings;
    }
    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        string path = Path.Combine(DataDirectory, "settings.json");
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temp, path, true);
    }
}

public static partial class HardwareIdentity
{
    [GeneratedRegex(@"VEN_([0-9A-F]{4}).*?DEV_([0-9A-F]{4}).*?SUBSYS_([0-9A-F]{8})", RegexOptions.IgnoreCase)]
    private static partial Regex PciRegex();
    public static string PciKey(string? deviceId)
    {
        var match = PciRegex().Match(deviceId ?? "");
        return match.Success ? $"{match.Groups[1].Value}:{match.Groups[2].Value}:{match.Groups[3].Value}".ToUpperInvariant() : "";
    }
    public static SensorReading? MatchGpu(GpuReading gpu, IEnumerable<GpuReading> all, IEnumerable<SensorReading> sensors)
    {
        // PCI signatures alone cannot disambiguate two identical boards. Leave temperatures unknown.
        if (gpu.MatchKey.Length == 0 || all.Count(x => x.MatchKey == gpu.MatchKey) != 1) return null;
        var matches = sensors.Where(x => x.Kind == "gpu" && x.MatchKey == gpu.MatchKey).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }
    public static List<GpuReading> PhysicalGpus(IEnumerable<GpuReading> adapters, IReadOnlyDictionary<string, double> loads) => adapters
        .GroupBy(x => x.PhysicalLocation.Length > 0 ? x.MatchKey + "@" + x.PhysicalLocation : x.Id)
        .Select(group => group.OrderByDescending(x => loads.ContainsKey(x.Id.Split('/')[^1])).First()).ToList();
}

public static class AppLog
{
    private static readonly object Gate = new();
    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                string path = Path.Combine(AppSettings.DataDirectory, "monitor.log");
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".previous", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
