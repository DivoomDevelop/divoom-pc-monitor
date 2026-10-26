using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Divoom.HardwareMonitor;

// Act as a controllable smartctl process when launched by SmartctlProvider.
if (args.Contains("-a"))
{
    if (!args.Contains("-j") || !args[^1].StartsWith("/dev/pd")) return;
    int index = int.Parse(args[^1][7..]);
    if (Environment.GetEnvironmentVariable("DIVOOM_TEST_SMART_TIMEOUT") == "1" && index == 0) Thread.Sleep(15000);
    Console.WriteLine(JsonSerializer.Serialize(new { smartctl = new { exit_status = 0 }, temperature = new { current = 37 + index } }));
    return;
}
int passed = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label); passed++;
}
async Task Reject(Func<Task> operation, string label)
{
    bool rejected = false;
    try { await operation(); } catch (HttpRequestException) { rejected = true; }
    Check(rejected, label);
}
var now = DateTimeOffset.UtcNow;
Check(Localizer.Languages.Length == 19 && Enum.GetValues<MenuLanguage>().Length == 19, "all nineteen requested language IDs exist");
Check(Localizer.Detect("zh-TW") == "zh-Hant" && Localizer.Detect("zh-HK") == "zh-Hant" && Localizer.Detect("zh-Hans-SG") == "zh-Hans", "Chinese system locales distinguish scripts");
Check(Localizer.Detect("pt-BR") == "pt" && Localizer.Detect("uk-UA") == "uk" && Localizer.Detect("nb-NO") == "en", "language variants and unsupported systems fall back correctly");
foreach (var language in Localizer.Languages)
{
    Localizer.Configure(language.Code, "en-US");
    var resources = Localizer.Resources;
    Check(resources[language.Code].Length == resources["keys"].Length && resources[language.Code].All(s => s.Length > 0), language.Code + " has every product string");
    bool placeholders = resources["keys"].Select((key, i) => (key, i)).All(pair =>
        System.Text.RegularExpressions.Regex.Matches(pair.key, @"\{\d+\}").Select(m => m.Value).Order()
        .SequenceEqual(System.Text.RegularExpressions.Regex.Matches(resources[language.Code][pair.i], @"\{\d+\}").Select(m => m.Value).Order()));
    Check(placeholders, language.Code + " preserves all dynamic placeholders");
    Check(Localizer.Ui("正在连接设备并选择表盘 3191…").Contains("3191"), language.Code + " localizes dynamic clock status without changing clock ID");
}
Localizer.Configure("auto", "ja-JP"); Check(Localizer.Code == "ja", "first launch follows system language");
Localizer.Configure("de", "ja-JP"); Check(Localizer.Code == "de", "explicit language overrides system language");
Localizer.Configure("zh-Hans");
Check(AppSettings.Parse("{}").Language == "auto" && AppSettings.Parse("{\"Language\":\"pl\"}").Language == "pl", "language preference persists and old settings follow system");
Metric M(double value, string unit = "%") => new(value, unit, "test", now);
Check(DeviceProtocol.Format(M(100), "%", now) == "100%", "100 percent is not truncated");
Check(DeviceProtocol.Format(M(0), "%", now) == "0%", "zero percent is valid");
Check(DeviceProtocol.Format(M(7), "%", now) == "7%", "single digit is valid");
Check(DeviceProtocol.Format(M(99.5), "%", now) == "100%", "rounding is numerical");
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
Check(M(7.5).Display(now) == "7.5%", "decimal culture is independent");
Check(DeviceProtocol.Format(null, "C", now) == "--", "missing temperature remains missing");
Check(DeviceProtocol.Format(M(double.NaN), "%", now) == "--", "NaN cannot be transmitted");
Check(DeviceProtocol.Format(M(55) with { SampledAt = now.AddSeconds(-20) }, "C", now) == "--", "stale sensor cannot be transmitted");
Check((M(45) with { Source = "smartctl · SMART", SampledAt = now.AddSeconds(-40) }).IsAvailable(now), "slow SMART samples remain valid between polls");
Check(!(M(45) with { Source = "smartctl · SMART", SampledAt = now.AddSeconds(-70) }).IsAvailable(now), "SMART samples eventually expire");
var gpu = new GpuReading("gpu/1", "GPU", "1002:1234:12345678", true, M(22), M(66, "℃"));
var disk = new DiskReading("disk/0", 0, "SSD", M(10), M(44, "℃"));
var snapshot = new HardwareSnapshot(now, "CPU", M(11), M(55, "℃"), M(33), 1000, 670, [gpu], [disk], [], []);
var packet = DeviceProtocol.Create(snapshot, gpu.Id, disk.Id, 4, now);
Check(packet.Command == "Device/UpdatePCParaInfo" && packet.ScreenList[0].LcdId == 4, "existing command and zero-based LCD mapping");
Check(packet.ScreenList[0].DispData.SequenceEqual(new[] { "11%", "22%", "55C", "66C", "33%", "44C" }), "six legacy fields preserve their order");
var missing = DeviceProtocol.Create(snapshot, "removed-gpu", "removed-disk", 0, now).ScreenList[0].DispData;
Check(missing[1] == "--" && missing[3] == "--" && missing[5] == "--", "removed hardware never sends another device's data");
var values = new Dictionary<string, double>
{
    ["pid_1_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D"] = 40,
    ["pid_2_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D"] = 30,
    ["pid_2_luid_0x00000000_0x00000001_phys_0_eng_1_engtype_Copy"] = 60,
    ["pid_1_luid_0x00000000_0x00000002_phys_0_eng_0_engtype_3D"] = 5,
    ["invalid"] = 99
};
var usage = NativeMetrics.AggregateGpuEngines(values);
Check(usage["luid_0x00000000_0x00000001"] == 70, "GPU sums processes per engine then takes busiest engine");
Check(usage["luid_0x00000000_0x00000002"] == 5, "multiple GPU utilization remains separate");
Check(HardwareIdentity.PciKey(@"PCI\VEN_1002&DEV_1234&SUBSYS_12345678&REV_01") == gpu.MatchKey, "PCI identity joins providers");
var reading = new SensorReading("gpu", "sensor/1", "GPU", gpu.MatchKey, null, M(66, "℃"));
Check(HardwareIdentity.MatchGpu(gpu, [gpu], [reading]) == reading, "unique GPU identity joins temperature");
Check(HardwareIdentity.MatchGpu(gpu, [gpu, gpu with { Id = "gpu/2" }], [reading]) is null, "identical GPUs are never incorrectly joined");
var physical = gpu with { PhysicalLocation = "01:00.0", Id = "gpu/luid_0x00000000_0x00000001" };
var mirror = physical with { Id = "gpu/mirror" };
Check(HardwareIdentity.PhysicalGpus([mirror, physical], usage).Single().Id == physical.Id, "same PCI location selects the active adapter");
Check(HardwareIdentity.PhysicalGpus([physical, physical with { Id = "gpu/second", PhysicalLocation = "02:00.0" }], usage).Count == 2, "identical boards in distinct PCI slots remain separate");
Check(LibreProvider.ChooseCpuTemperature([("CPU Core #1", 90), ("CPU Package", 70)])?.Value == 70, "CPU package wins over arbitrary final core");
Check(LibreProvider.ChooseCpuTemperature([("Core (Tctl)", 80), ("Core (Tdie)", 65)])?.Value == 65, "AMD actual die temperature is preferred");
Check(LibreProvider.ChooseCpuTemperature([("CPU Core #1", 60), ("CPU Core #2", 75)])?.Value == 75, "fallback chooses maximum core");
Check(SmartctlParser.Parse("{\"smartctl\":{\"exit_status\":8},\"temperature\":{\"current\":42}}").Value == 42, "SMART health warning does not discard a readable temperature");
Check(SmartctlParser.Parse("{\"smartctl\":{\"exit_status\":2},\"temperature\":{\"current\":42}}").Value is null, "SMART open failure is not a temperature");
Check(SmartctlParser.Parse("{}").Value is null, "missing SMART temperature stays missing");
foreach (int hardware in new[] { 90, 92, 96, 400, 402, 511, 530, 91, 401, 0 })
{
    var uri = new DeviceTarget("127.0.0.1", 0, 0, 0, hardware).PostUri;
    bool legacyEndpoint = hardware is 90 or 92 or 400;
    Check(uri.Port == (legacyEndpoint ? 80 : 9000) && uri.AbsolutePath == (legacyEndpoint ? "/post" : "/divoom_api"), $"Hardware {hardware} selects the correct port and path");
}
Check(AppSettings.Parse("{\"TimeGate\":true}").DeviceHardware == 400, "old TimeGate settings preserve Hardware 400 routing");
Check(AppSettings.Parse("{\"TimeGate\":true,\"DeviceHardware\":530}").DeviceHardware == 530, "explicit Hardware survives settings reload");
bool invalidIp = false;
try { _ = new DeviceTarget("192.168.1.1/other", 0, 0, 0).PostUri; } catch (ArgumentException) { invalidIp = true; }
Check(invalidIp, "manual input cannot inject a URL path");
Check(Enumerable.Range(0, 6).Select(SendingService.RetrySeconds).SequenceEqual(new[] { 2, 4, 8, 16, 30, 30 }), "retry backoff stays bounded");
using (var transport = new DivoomTransport(new RecordingHandler()))
{
    await transport.SelectClockAsync(new("127.0.0.1", 42, 4, 1, 90), CancellationToken.None);
    Check(RecordingHandler.LastContentLength == Encoding.UTF8.GetByteCount(RecordingHandler.LastBody!), "clock body has Content-Length before a handler reads it");
    using var clock = JsonDocument.Parse(RecordingHandler.LastBody!);
    Check(clock.RootElement.GetProperty("ClockId").GetInt32() == 625 && clock.RootElement.GetProperty("LcdIndex").GetInt32() == 4, "clock setup uses legacy clock and LCD");
    await transport.SendAsync(new("127.0.0.1", 42, 4, 1, 90), packet, CancellationToken.None);
    Check(RecordingHandler.LastContentLength == Encoding.UTF8.GetByteCount(RecordingHandler.LastBody!), "PC body has Content-Length before a handler reads it");
    Check(RecordingHandler.LastUri?.AbsolutePath == "/post", "transport uses local post endpoint");
    using var payload = JsonDocument.Parse(RecordingHandler.LastBody!);
    Check(payload.RootElement.GetProperty("ScreenList")[0].GetProperty("DispData").GetArrayLength() == 6, "HTTP JSON preserves firmware field casing");
}
foreach (var (hardware, clockId) in new[] { (90, (int?)625), (92, (int?)625), (96, (int?)625), (400, (int?)625), (402, (int?)625), (511, (int?)null), (530, (int?)3191), (0, (int?)null), (91, (int?)null), (999, (int?)null) })
{
    var handler = new RecordingHandler();
    using var transport = new DivoomTransport(handler);
    var target = new DeviceTarget("127.0.0.1", 42, 0, 0, hardware);
    await transport.SelectClockAsync(target, CancellationToken.None);
    if (clockId is { } expected)
    {
        using var selected = JsonDocument.Parse(RecordingHandler.LastBody!);
        Check(handler.Calls == 1 && RecordingHandler.LastUri == target.PostUri && selected.RootElement.GetProperty("ClockId").GetInt32() == expected, $"Hardware {hardware} selects only clock {expected} at the correct endpoint");
    }
    else Check(handler.Calls == 0, $"Hardware {hardware} sends no clock selection request");
    await transport.SendAsync(target, packet, CancellationToken.None);
    Check(handler.Calls == (clockId.HasValue ? 2 : 1) && RecordingHandler.LastUri == target.PostUri, $"PC data request routes Hardware {hardware} correctly");
    using var payload = JsonDocument.Parse(RecordingHandler.LastBody!);
    if (target.UsesScreenList)
        Check(payload.RootElement.GetProperty("ScreenList")[0].GetProperty("DispData")[0].GetString() == "11%", $"Hardware {hardware} preserves the legacy payload");
    else
    {
        Check(payload.RootElement.GetProperty("DeviceId").GetInt32() == 42 && !payload.RootElement.TryGetProperty("ClockId", out _), $"Hardware {hardware} API payload carries DeviceId and omits ClockId");
        Check(payload.RootElement.GetProperty("DispData").EnumerateArray().Select(x => x.GetProperty("AppData").GetString()).SequenceEqual(packet.ScreenList[0].DispData), $"Hardware {hardware} API payload preserves all six values");
    }
}
var compatibility = new ClockCompatibilityHandler();
using (var transport = new DivoomTransport(compatibility))
{
    await transport.SendAsync(new("127.0.0.1", 42, 0, 0, 530), packet, CancellationToken.None);
    await transport.SendAsync(new("127.0.0.1", 42, 0, 0, 530), packet, CancellationToken.None);
    Check(compatibility.ClockIds.SequenceEqual(new int?[] { null, 3191, 3191 }), "Hardware 530 compatibility retries use 3191 and never 625");
}
var unmapped = new ClockCompatibilityHandler();
foreach (int hardware in new[] { 400, 402 })
{
    using var transport = new DivoomTransport(new RecordingHandler());
    await transport.SendAsync(new("127.0.0.1", 0, 4, 1, hardware), packet, CancellationToken.None);
    using var payload = JsonDocument.Parse(RecordingHandler.LastBody!);
    Check(payload.RootElement.GetProperty("ScreenList")[0].GetProperty("LcdId").GetInt32() == 4
        && payload.RootElement.GetProperty("ScreenList")[0].GetProperty("DispData").EnumerateArray().Select(x => x.GetString()).SequenceEqual(packet.ScreenList[0].DispData)
        && RecordingHandler.LastUri!.Port == (hardware == 400 ? 80 : 9000), $"TimeGate {hardware} keeps ScreenList and selected LCD independently of HTTP port");
}
using (var transport = new DivoomTransport(new RecordingHandler("{\"error_code\":\"DeviceToken is err\"}")))
    await Reject(() => transport.SendAsync(new("127.0.0.1", 0, 0, 0, 400), packet, CancellationToken.None), "string device authentication errors cannot be reported as success");
Check(DeviceSecrets.ParseLocalToken(" 123456 ") == 123456 && DeviceSecrets.ParseLocalToken("") is null, "LocalToken input is optional and numeric");
bool invalidToken = false;
try { DeviceSecrets.ParseLocalToken("123x"); } catch (ArgumentException) { invalidToken = true; }
Check(invalidToken, "invalid LocalToken is rejected before sending");
using (var transport = new DivoomTransport(new RecordingHandler()))
{
    var target = new DeviceTarget("127.0.0.1", 42, 4, 1, 402, 123456);
    await transport.SelectClockAsync(target, CancellationToken.None);
    using var clock = JsonDocument.Parse(RecordingHandler.LastBody!);
    Check(clock.RootElement.GetProperty("LocalToken").GetUInt32() == 123456 && !clock.RootElement.TryGetProperty("DeviceToken", out _), "clock selection uses numeric LocalToken, never DeviceToken");
    await transport.SendAsync(target, packet, CancellationToken.None);
    using var data = JsonDocument.Parse(RecordingHandler.LastBody!);
    Check(data.RootElement.GetProperty("LocalToken").GetUInt32() == 123456 && data.RootElement.GetProperty("ScreenList")[0].GetProperty("LcdId").GetInt32() == 4, "LocalToken is included in PC data without changing LCD targeting");
    Check(!DeviceSecrets.Redact(RecordingHandler.LastBody!).Contains("123456"), "diagnostic requests hide LocalToken");
}
using (var transport = new DivoomTransport(unmapped))
{
    await Reject(() => transport.SendAsync(new("127.0.0.1", 42, 0, 0, 999), packet, CancellationToken.None), "unmapped hardware errors do not invent a clock fallback");
    Check(unmapped.ClockIds.SequenceEqual(new int?[] { null }), "unmapped hardware never sends a compatibility ClockId");
}
var noClock = new FakeTransport();
using (var sender = new SendingService(noClock, () => snapshot))
{
    await sender.StartAsync(new("127.0.0.1", 42, 0, 0, 999), gpu.Id, disk.Id);
    await noClock.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await sender.StopAsync();
    Check(noClock.ClockCalls == 0 && noClock.SendCalls == 1, "sender skips clock selection but sends PC data for other hardware");
}
using (var sender = new SendingService(new FakeTransport(), () => snapshot))
{
    bool rejected = false;
    try { await sender.StartAsync(new("127.0.0.1", 0, 0, 0, 530), gpu.Id, disk.Id); } catch (ArgumentException) { rejected = true; }
    Check(rejected && !sender.IsRunning, "missing API DeviceId is rejected before any device request");
}
using (var transport = new DivoomTransport(new RecordingHandler("{\"ReturnCode\":100,\"ReturnMessage\":\"Error\"}")))
    await Reject(() => transport.SendAsync(new("127.0.0.1", 0, 0, 0, 90), packet, CancellationToken.None), "device errors are surfaced");
string fullError = JsonSerializer.Serialize(new { ReturnCode = 100, ReturnMessage = new string('x', 600) + "END-OF-DEVICE-ERROR", LocalToken = 123456 });
using (var transport = new DivoomTransport(new RecordingHandler(fullError)))
{
    try { await transport.SendAsync(new("127.0.0.1", 0, 0, 0, 511), packet, CancellationToken.None); Check(false, "long device errors must fail"); }
    catch (HttpRequestException error) { Check(error.Message.Contains("END-OF-DEVICE-ERROR") && !error.Message.Contains("123456"), "complete device errors retain their tail and redact LocalToken"); }
}
using (var transport = new DivoomTransport(new RecordingHandler("{}", HttpStatusCode.InternalServerError)))
    await Reject(() => transport.SendAsync(new("127.0.0.1", 0, 0, 0, 90), packet, CancellationToken.None), "HTTP errors are surfaced");
using (var transport = new DivoomTransport(new HangingHandler()))
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
    bool canceled = false;
    try { await transport.SendAsync(new("127.0.0.1", 0, 0, 0, 90), packet, cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
    Check(canceled, "in-flight request can be canceled");
}
using (var smart = new SmartctlProvider(Environment.ProcessPath!))
{
    var frame = smart.Read();
    Check(frame.Readings.Count > 0 && frame.Readings.All(x => x.Temperature?.Value == 37 + int.Parse(x.Id[5..])), "smartctl process arguments and physical disk mapping work end to end");
}
Environment.SetEnvironmentVariable("DIVOOM_TEST_SMART_TIMEOUT", "1");
try
{
    using var smart = new SmartctlProvider(Environment.ProcessPath!);
    var clock = System.Diagnostics.Stopwatch.StartNew();
    var frame = smart.Read();
    Check(clock.Elapsed < TimeSpan.FromSeconds(13) && frame.Diagnostics.Any(x => x.Contains("磁盘 0")), "hung smartctl process is killed within its deadline");
}
finally { Environment.SetEnvironmentVariable("DIVOOM_TEST_SMART_TIMEOUT", null); }
Check(new AppSettings().SmartctlMode == SmartctlMode.Automatic, "new installations automatically use the bundled smartctl");
Check(AppSettings.Parse("{\"EnableSmartctl\":false}").SmartctlMode == SmartctlMode.Automatic, "legacy default-off settings migrate to automatic mode");
Check(AppSettings.Parse("{\"EnableSmartctl\":true,\"SmartctlPath\":\"custom.exe\"}").SmartctlMode == SmartctlMode.Custom, "legacy custom executable choice is preserved");
Check(AppSettings.Parse("{\"SmartctlMode\":1,\"EnableSmartctl\":true,\"SmartctlPath\":\"custom.exe\"}").SmartctlMode == SmartctlMode.Disabled, "explicit disable survives legacy migration");
string bundledSmartctl = SmartctlRuntime.Resolve(new() { SmartctlPath = "removed-legacy-file.exe" });
Check(bundledSmartctl == Path.Combine(AppContext.BaseDirectory, "third_party", "smartmontools", "smartctl.exe"), "automatic mode locates the published bundle independently of old paths");
Check(SmartctlRuntime.Resolve(new() { SmartctlMode = SmartctlMode.Custom, SmartctlPath = Environment.ProcessPath! }) == Environment.ProcessPath, "custom executable override remains available");
bool disabled = false, absent = false;
try { SmartctlRuntime.Resolve(new() { SmartctlMode = SmartctlMode.Disabled }); } catch (InvalidOperationException) { disabled = true; }
try { SmartctlRuntime.Resolve(new(), Path.Combine(AppContext.BaseDirectory, "nonexistent-bundle")); } catch (FileNotFoundException) { absent = true; }
Check(disabled && absent, "disabled mode and missing bundles cannot silently run another executable");
var fresh = Metric.Create(46, "℃", "primary");
var primaryDisk = new SensorReading("disk", "disk/0", "SSD", "", null, fresh);
var fallbackDisk = primaryDisk with { Temperature = Metric.Create(37, "℃", "smartctl · SMART") };
var merged = new List<SensorReading> { primaryDisk };
SensorMerge.Add(merged, [fallbackDisk], DateTimeOffset.UtcNow);
Check(merged.Single().Temperature == fresh, "valid primary temperature is preferred over smartctl");
merged = [primaryDisk with { Temperature = Metric.Missing("℃", "primary", "not supported") }];
SensorMerge.Add(merged, [fallbackDisk], DateTimeOffset.UtcNow);
Check(merged.Single().Temperature == fallbackDisk.Temperature, "smartctl replaces an unavailable primary metric");
merged = [primaryDisk with { Temperature = fresh with { SampledAt = DateTimeOffset.UtcNow.AddSeconds(-20) } }];
SensorMerge.Add(merged, [fallbackDisk], DateTimeOffset.UtcNow);
Check(merged.Single().Temperature == fallbackDisk.Temperature, "smartctl replaces stale primary temperature");
using (var smart = new SmartctlProvider(Environment.ProcessPath!, () => [(0, "A"), (1, "B")]))
{
    var frame = smart.ReadMissing([primaryDisk]);
    Check(frame.Readings.Count == 1 && frame.Readings[0].Id == "disk/1" && frame.Readings[0].Temperature?.Value == 38, "automatic fallback queries only the disk with missing temperature");
    Check(ReferenceEquals(frame, smart.ReadMissing([])), "fallback polling respects the thirty-second cache");
}
using (var smart = new SmartctlProvider(Environment.ProcessPath!, () => [(0, "A")]))
    Check(smart.ReadMissing([primaryDisk]).Readings.Count == 0, "complete primary readings do not spawn a disk query");
using (var version = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(bundledSmartctl, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }))
{
    string text = await version!.StandardOutput.ReadToEndAsync();
    await version.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Check(version.ExitCode == 0 && text.Contains("smartctl 7.5") && text.Contains("x86_64"), "the bundled official x64 executable runs without installation");
}
var fake = new FakeTransport();
using (var sender = new SendingService(fake, () => snapshot))
{
    await sender.StartAsync(new("127.0.0.1", 0, 0, 0, 90), gpu.MatchKey, disk.Id);
    await fake.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await sender.StopAsync();
    Check(fake.ClockCalls == 1 && fake.SendCalls == 1 && !sender.IsRunning, "sending starts once and cancels promptly");
}
var secondGpu = gpu with { Id = "gpu/2", Load = M(79) };
var dualGpu = snapshot with { Gpus = [gpu, secondGpu] };
{
    var recorder = new FakeTransport();
    using var selectedSender = new SendingService(recorder, () => dualGpu);
    await selectedSender.StartAsync(new("127.0.0.1", 0, 0, 0, 90), secondGpu.Id, disk.Id);
    await recorder.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await selectedSender.StopAsync();
    Check(recorder.LastPacket?.ScreenList[0].DispData[1] == "79%", "selected identical GPU keeps its own load");
}
Console.WriteLine($"All {passed} regression checks passed.");

sealed class RecordingHandler(string body = "{\"ReturnCode\":0}", HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public static string? LastBody;
    public static Uri? LastUri;
    public static long? LastContentLength;
    public int Calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        LastUri = request.RequestUri;
        Calls++;
        LastContentLength = request.Content!.Headers.ContentLength;
        LastBody = await request.Content!.ReadAsStringAsync(token);
        return new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
sealed class ClockCompatibilityHandler : HttpMessageHandler
{
    public List<int?> ClockIds = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
        int? clock = json.RootElement.TryGetProperty("ClockId", out var id) ? id.GetInt32() : null;
        ClockIds.Add(clock);
        return new(HttpStatusCode.OK) { Content = new StringContent(clock is null ? "{\"ReturnCode\":1,\"ReturnMessage\":\"Only accept JSON parameters\"}" : "{\"ReturnCode\":0}", Encoding.UTF8, "application/json") };
    }
}
sealed class FakeTransport : IDeviceTransport
{
    public int ClockCalls, SendCalls;
    public DevicePacket? LastPacket;
    public TaskCompletionSource Sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task SelectClockAsync(DeviceTarget target, CancellationToken token) { ClockCalls++; return Task.CompletedTask; }
    public Task SendAsync(DeviceTarget target, DevicePacket packet, CancellationToken token) { LastPacket = packet; SendCalls++; Sent.TrySetResult(); return Task.CompletedTask; }
    public void Dispose() { }
}
sealed class HangingHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        await Task.Delay(Timeout.Infinite, token);
        return new(HttpStatusCode.OK);
    }
}
