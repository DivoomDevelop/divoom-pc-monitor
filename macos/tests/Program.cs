using System.Net;
using System.Text;
using System.Text.Json;
using Divoom.HardwareMonitor;
using Divoom.HardwareMonitor.Mac;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Themes.Fluent;

int passed = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); passed++; Console.WriteLine("PASS: " + label); }
foreach (var language in Localizer.Languages)
{
    Localizer.Configure(language.Code);
    Check(Localizer.Resources[language.Code].Length == Localizer.Resources["keys"].Length && Localizer.Format("发送失败，{0} 秒后重试: {1}", 2, "HTTP 500").Contains("HTTP 500"), "Mac language resource and dynamic status " + language.Code);
}
Localizer.Configure("zh-Hans");
Check(MacParsing.CpuPercent([100, 40, 200, 0], [120, 50, 270, 0]) == 30, "CPU tick differences exclude idle");
Check(MacParsing.CpuPercent([uint.MaxValue - 4, 0, 0, 0], [5, 0, 10, 0]) == 50, "CPU ticks survive 32-bit wraparound");
Check(MacParsing.CpuPercent([1, 2, 3, 4], [1, 2, 3, 4]) is null, "unchanged counters remain unknown");
Check(MacParsing.MemoryUsed(10000000, 100, 20, 10, 5, 16384) == 125 * 16384, "Apple Silicon 16KB pages include physical compressor pages once");
Check(MacParsing.MemoryUsed(10000000, 2, 20, 10, 5, 4096) == 30 * 4096, "purgeable subtraction does not produce negative app memory");
var vm = new uint[40]; vm[3] = 20; vm[22] = 5; vm[32] = 10; vm[35] = 100; vm[34] = 10000; vm[37] = 10000;
Check(MacParsing.VmMemoryUsed(100000000, vm, 4096) == 125 * 4096, "Darwin VM layout excludes file-backed and uncompressed virtual counts");
Check(SmcReader.Decode(SmcReader.Key("sp78"), [0x30, 0x80]) == 48.5, "Intel fixed point temperature decodes correctly");
Check(SmcReader.Decode(SmcReader.Key("sp78"), [0xff, 0x80]) == -0.5, "signed sensor values are not mistaken for high temperature");
Check(SmcReader.Decode(SmcReader.Key("flt "), BitConverter.GetBytes(56.25f)) == 56.25, "Apple Silicon float temperature decodes correctly");
Check(SmcReader.Decode(SmcReader.Key("flt "), BitConverter.GetBytes(float.NaN)) is null, "invalid sensor float is rejected");
Check(!SmcReader.IsCpuKey("TC0P", false) && !SmcReader.IsCpuKey("Tp0P", false) && !SmcReader.IsGpuKey("TG0P", false), "proximity and powerboard sensors never become core temperature");
Check(SmcReader.IsCpuKey("Tp0P", true) && SmcReader.IsCpuKey("Te05", true) && SmcReader.IsCpuKey("Tf04", true) && SmcReader.IsGpuKey("Tg0D", true), "Apple Silicon core key families are separate from Intel");
Check(MacParsing.SmartTemperature("{\"smartctl\":{\"exit_status\":8},\"temperature\":{\"current\":42}}").Value == 42, "SMART health warnings preserve temperature");
Check(MacParsing.SmartTemperature("{\"smartctl\":{\"exit_status\":2},\"temperature\":{\"current\":42}}").Value is null, "SMART access failures never send a temperature");
const string gpuXml = """
<?xml version="1.0"?><!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><array><dict><key>IOClass</key><string>AGXAcceleratorG13G</string><key>IORegistryEntryID</key><integer>4321</integer><key>PerformanceStatistics</key><dict><key>model</key><string>Apple M1</string><key>Device Utilization %</key><integer>35</integer><key>Temperature(C)</key><integer>47</integer></dict></dict></array></plist>
""";
var gpu = MacParsing.Gpus(gpuXml).Single();
Check(gpu.Id == "gpu/4321" && gpu.Name == "Apple M1" && gpu.Load.Value == 35 && gpu.Temperature.Value == 47, "IORegistry Apple GPU maps identity and metrics without network DTD loading");
var noTemp = MacParsing.Gpus(gpuXml.Replace("<key>Temperature(C)</key><integer>47</integer>", "")).Single();
Check(noTemp.Temperature.Value is null, "GPU without a temperature remains missing");
var duplicate = gpuXml.Replace("</array>", gpuXml[(gpuXml.IndexOf("<dict>"))..gpuXml.IndexOf("</array>")] + "</array>");
Check(MacParsing.Gpus(duplicate).Count == 0, "ambiguous GPU identities cannot send another adapter's values");
var now = DateTimeOffset.UtcNow;
Metric P(double n) => new(n, "%", "test", now);
var snapshot = new HardwareSnapshot(now, "Apple M1", P(20), P(55), P(60), 16000, 6400, [gpu], [new("disk0", 0, "APPLE SSD", P(5), P(41))], [], []);
var packet = DeviceProtocol.Create(snapshot, gpu.Id, "disk0", 4, now);
foreach (var (hardware, clockId) in new[] { (90, 625), (92, 625), (96, 625), (400, 625), (402, 625), (511, 0), (530, 3191), (999, 0) })
{
    var handler = new Recorder(); using var transport = new DivoomTransport(handler); var target = new DeviceTarget("127.0.0.1", 42, 4, 1, hardware);
    await transport.SelectClockAsync(target, CancellationToken.None);
    Check(clockId == 0 ? handler.Bodies.Count == 0 : JsonDocument.Parse(handler.Bodies.Single()).RootElement.GetProperty("ClockId").GetInt32() == clockId, "shared clock whitelist " + hardware);
    await transport.SendAsync(target, packet, CancellationToken.None);
    Check(handler.Uri == target.PostUri && handler.ContentLength == Encoding.UTF8.GetByteCount(handler.Bodies[^1]), "shared endpoint and non-chunked JSON " + hardware);
    using var json = JsonDocument.Parse(handler.Bodies[^1]);
    Check(target.UsesScreenList ? json.RootElement.GetProperty("ScreenList")[0].GetProperty("LcdId").GetInt32() == 4 : json.RootElement.GetProperty("DeviceId").GetInt32() == 42, "shared PC format " + hardware);
}
Check(DeviceProtocol.Format(snapshot.CpuLoad with { SampledAt = now.AddMinutes(-1) }, "%", now) == "--", "worker failure cannot transmit stale metrics");
AppBuilder.Configure<PreviewApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
foreach (string language in new[] { "en", "ru", "th" })
{
    Localizer.Configure(language);
    var window = new MainWindow(new ClassicDesktopStyleApplicationLifetime(), preview: true) { Width = 1120, Height = 900 };
    window.Show(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    var tabs = window.GetLogicalDescendants().OfType<TabControl>().Single();
    foreach (int tab in new[] { 0, 1, 2 })
    {
        tabs.SelectedIndex = tab; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var labels = window.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text ?? "").ToArray();
        var untranslated = labels.Where(t => System.Text.RegularExpressions.Regex.IsMatch(t, @"[\p{IsCJKUnifiedIdeographs}]")).ToArray();
        Check(untranslated.Length == 0, language + " visible product labels contain no untranslated Chinese on tab " + tab + ": " + string.Join(" / ", untranslated));
        if (args.Contains("--render"))
        {
            string directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/mac-ui")); Directory.CreateDirectory(directory);
            using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(directory, language + "-" + tab + ".png"));
        }
    }
    window.Hide(); window.Content = null;
}
Console.WriteLine($"All {passed} Mac and shared protocol checks passed.");

sealed class PreviewApp : Application { public override void Initialize() => Styles.Add(new FluentTheme()); }

sealed class Recorder : HttpMessageHandler
{
    public readonly List<string> Bodies = [];
    public Uri? Uri; public long? ContentLength;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Uri = request.RequestUri; ContentLength = request.Content!.Headers.ContentLength; Bodies.Add(await request.Content.ReadAsStringAsync(token));
        return new(HttpStatusCode.OK) { Content = new StringContent("{\"ReturnCode\":0}") };
    }
}
