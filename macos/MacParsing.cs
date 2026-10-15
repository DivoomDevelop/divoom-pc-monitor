using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Divoom.HardwareMonitor.Mac;

public static class MacParsing
{
    public static object? Plist(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
        var root = XDocument.Load(reader).Root ?? throw new FormatException("空 plist");
        return Decode(root.Name.LocalName == "plist" ? root.Elements().First() : root);
    }
    private static object? Decode(XElement node)
    {
        switch (node.Name.LocalName)
        {
            case "dict":
                var result = new Dictionary<string, object?>();
                var children = node.Elements().ToArray();
                for (int i = 0; i + 1 < children.Length; i += 2)
                    if (children[i].Name.LocalName == "key") result[children[i].Value] = Decode(children[i + 1]);
                return result;
            case "array": return node.Elements().Select(Decode).ToList();
            case "integer": return long.TryParse(node.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? n : null;
            case "real": return double.TryParse(node.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null;
            case "true": return true;
            case "false": return false;
            case "data": return Convert.FromBase64String(node.Value);
            default: return node.Value;
        }
    }
    public static IEnumerable<Dictionary<string, object?>> Dictionaries(object? value)
    {
        if (value is Dictionary<string, object?> dict)
        {
            yield return dict;
            foreach (var child in dict.Values.SelectMany(Dictionaries)) yield return child;
        }
        else if (value is List<object?> list)
            foreach (var child in list.SelectMany(Dictionaries)) yield return child;
    }
    public static string Text(Dictionary<string, object?> value, string key, string fallback = "") => value.GetValueOrDefault(key) switch
    {
        byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0'),
        null => fallback,
        var item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? fallback
    };
    public static double? Number(Dictionary<string, object?> value, string key) => value.GetValueOrDefault(key) switch { long n => n, double d when double.IsFinite(d) => d, _ => null };
    public static Metric Temperature(double? value, string source, string error) => value is > 0 and < 150 ? Metric.Create(value.Value, "℃", source) : Metric.Missing("℃", source, error);
    public static double? CpuPercent(uint[] previous, uint[] current)
    {
        if (previous.Length != 4 || current.Length != 4) return null;
        // Unsigned subtraction handles Darwin's 32-bit tick counter wraparound.
        ulong[] delta = current.Select((n, i) => (ulong)unchecked(n - previous[i])).ToArray();
        ulong total = delta.Aggregate(0UL, (a, b) => a + b);
        return total == 0 ? null : (total - delta[2]) * 100.0 / total;
    }
    public static long VmMemoryUsed(long total, uint[] data, uint pageSize) => data.Length >= 36
        ? MemoryUsed(total, data[35], data[3], data[32], data[22], pageSize) : throw new ArgumentException("vm_statistics64 太短");
    public static long MemoryUsed(long total, uint internalPages, uint wiredPages, uint compressedPages, uint purgeablePages, uint pageSize)
    {
        // App memory (anonymous minus purgeable) + wired + physical compressor pages.
        long pages = Math.Max(0L, (long)internalPages - purgeablePages) + wiredPages + compressedPages;
        return Math.Clamp(pages * pageSize, 0, total);
    }
    public static Metric SmartTemperature(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("smartctl", out var ctl) && ctl.TryGetProperty("exit_status", out var exit) && (exit.GetInt32() & 7) != 0)
            return Metric.Missing("℃", "smartctl", "磁盘访问、参数或 SMART 命令失败");
        if (root.TryGetProperty("temperature", out var temp) && temp.TryGetProperty("current", out var current) && current.TryGetDouble(out double value))
            return Temperature(value, "smartctl · SMART", "磁盘温度无效");
        return Metric.Missing("℃", "smartctl", "磁盘未提供温度");
    }
    public static List<GpuReading> Gpus(string plist)
    {
        var result = new List<GpuReading>();
        foreach (var item in Dictionaries(Plist(plist)))
        {
            if (item.GetValueOrDefault("PerformanceStatistics") is not Dictionary<string, object?> stats) continue;
            string kind = Text(item, "IOClass"), entry = Text(item, "IORegistryEntryID");
            if (entry.Length == 0) entry = Text(item, "IORegistryEntryName") + "|" + kind + "|" + Text(item, "IOPCIMatch");
            string id = "gpu/" + entry;
            string name = Text(item, "model", Text(stats, "model", Text(item, "IORegistryEntryName", kind)));
            double? load = Number(stats, "Device Utilization %") ?? Number(stats, "GPU Activity(%)");
            result.Add(new(id, name, id, kind.Contains("AMD", StringComparison.OrdinalIgnoreCase) || kind.Contains("NV", StringComparison.OrdinalIgnoreCase),
                load is >= 0 and <= 100 ? Metric.Percent(load.Value, "IOKit PerformanceStatistics") : Metric.Missing("%", "IOKit", "驱动未开放 GPU 使用率"),
                Temperature(Number(stats, "Temperature(C)"), "IOKit PerformanceStatistics", "驱动未开放 GPU 温度")));
        }
        // Never merge or send two adapters with ambiguous identity.
        return result.GroupBy(g => g.Id).Where(g => g.Count() == 1).Select(g => g.Single()).ToList();
    }
}
