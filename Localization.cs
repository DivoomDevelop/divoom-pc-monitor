using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Divoom.HardwareMonitor;

// Numeric order follows DIVOOM_LVGL_MENU_LANG_* supplied by the device team.
public enum MenuLanguage { English, SimplifiedChinese, TraditionalChinese, Japanese, German, Thai, Russian, Korean, Vietnamese, French, Turkish, Spanish, Portuguese, Indonesian, Italian, Ukrainian, Swedish, Danish, Polish }
public sealed record LanguageChoice(string Code, string Name) { public override string ToString() => Name; }

public static class Localizer
{
    public static readonly LanguageChoice[] Languages = [new("en", "English"), new("zh-Hans", "简体中文"), new("zh-Hant", "繁體中文"), new("ja", "日本語"), new("de", "Deutsch"), new("th", "ไทย"), new("ru", "Русский"), new("ko", "한국어"), new("vi", "Tiếng Việt"), new("fr", "Français"), new("tr", "Türkçe"), new("es", "Español"), new("pt", "Português"), new("id", "Bahasa Indonesia"), new("it", "Italiano"), new("uk", "Українська"), new("sv", "Svenska"), new("da", "Dansk"), new("pl", "Polski")];
    private static readonly Dictionary<string, string[]> Catalog = Load();
    private static readonly Dictionary<string, int> Index = Catalog["keys"].Select((s, i) => (s, i)).ToDictionary(x => x.s, x => x.i);
    private static readonly string[] OrderedKeys = Catalog["keys"].Where(s => !s.Contains('{')).OrderByDescending(s => s.Length).ToArray();
    private static readonly Regex Labels = new(string.Join('|', OrderedKeys.Select(Regex.Escape)), RegexOptions.Compiled);
    public static string Code { get; private set; } = "zh-Hans";
    public static string Detect(string culture)
    {
        culture = culture.Replace('_', '-');
        if (culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return Regex.IsMatch(culture, @"(?:^|-)(?:Hant|TW|HK|MO)(?:-|$)", RegexOptions.IgnoreCase) ? "zh-Hant" : "zh-Hans";
        string prefix = culture.Split('-')[0].ToLowerInvariant();
        return Languages.Any(l => l.Code == prefix) ? prefix : "en";
    }
    public static void Configure(string? preferred = null, string? systemCulture = null) => Code = Languages.Any(l => l.Code == preferred) ? preferred! : Detect(systemCulture ?? CultureInfo.CurrentUICulture.Name);
    public static string Get(string key) => Index.TryGetValue(key, out int index) ? Catalog[Code][index] : key;
    public static string Format(string key, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Get(key), values);
    public static string Ui(string text)
    {
        if (text.Length == 0 || Code == "zh-Hans") return text;
        if (Index.ContainsKey(text)) return Get(text);
        // Only product strings are passed here. User-entered text and raw error payloads are excluded by the UI.
        var sent = Regex.Match(text, @"^已连接 (.+) · LCD (\d+) · 最近发送 (.+)$");
        if (sent.Success) return Format("已连接 {0} · LCD {1} · 最近发送 {2}", sent.Groups[1].Value, sent.Groups[2].Value, sent.Groups[3].Value);
        var retry = Regex.Match(text, @"^发送失败，(\d+) 秒后重试: (.*)$", RegexOptions.Singleline);
        if (retry.Success) return Format("发送失败，{0} 秒后重试: {1}", retry.Groups[1].Value, retry.Groups[2].Value);
        var clock = Regex.Match(text, @"^正在连接设备并选择表盘 (\d+)…$");
        if (clock.Success) return Format("正在连接设备并选择表盘 {0}…", clock.Groups[1].Value);
        var found = Regex.Match(text, @"^发现 (\d+) 台设备，(?:选择设备或手填 IP。|也可手动填写 IP。)$");
        if (found.Success) return Format("发现 {0} 台设备，选择设备或手填 IP。", found.Groups[1].Value);
        // Translate Chinese product labels within titles, values and status prefixes. Diagnostics retain original raw text.
        return Labels.Replace(text, m => Get(m.Value));
    }
    public static IReadOnlyDictionary<string, string[]> Resources => Catalog;
    private static Dictionary<string, string[]> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Divoom.Localization.catalog.json") ?? throw new IOException("Language resources missing");
        var rows = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new IOException("Language resources invalid");
        var result = rows.ToDictionary(row => row.Key, row => row.Value.Split('|'));
        int count = result["keys"].Length;
        foreach (var language in Languages)
            if (!result.TryGetValue(language.Code, out var values) || values.Length != count || values.Any(string.IsNullOrWhiteSpace)) throw new IOException("Incomplete language: " + language.Code);
        return result;
    }
}
