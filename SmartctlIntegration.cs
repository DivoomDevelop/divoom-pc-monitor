namespace Divoom.HardwareMonitor;

public static class SmartctlRuntime
{
    public static string Resolve(AppSettings settings, string? baseDirectory = null)
    {
        string path = settings.SmartctlMode switch
        {
            SmartctlMode.Automatic => Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "third_party", "smartmontools", "smartctl.exe"),
            SmartctlMode.Custom when !string.IsNullOrWhiteSpace(settings.SmartctlPath) => Path.GetFullPath(settings.SmartctlPath.Trim()),
            SmartctlMode.Disabled => throw new InvalidOperationException("smartctl 已禁用。"),
            _ => throw new InvalidOperationException("smartctl 模式或自定义路径无效。")
        };
        if (!File.Exists(path)) throw new FileNotFoundException("smartctl 不存在，请使用完整发布包，或在采集设置中指定路径。", path);
        return path;
    }
}

public static class SensorMerge
{
    public static void Add(List<SensorReading> readings, IEnumerable<SensorReading> extra, DateTimeOffset now)
    {
        Metric? Prefer(Metric? first, Metric? fallback) => first?.IsAvailable(now) == true ? first : fallback?.IsAvailable(now) == true ? fallback : first ?? fallback;
        foreach (var reading in extra)
        {
            int existing = readings.FindIndex(x => x.Kind == reading.Kind && (x.Kind == "gpu" ? x.MatchKey.Length > 0 && x.MatchKey == reading.MatchKey : x.Id == reading.Id));
            if (existing < 0) readings.Add(reading);
            else
            {
                var old = readings[existing];
                readings[existing] = old with { Load = Prefer(old.Load, reading.Load), Temperature = Prefer(old.Temperature, reading.Temperature), Hotspot = Prefer(old.Hotspot, reading.Hotspot) };
            }
        }
    }
}
