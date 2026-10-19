using System.Text;
using System.Text.Json;

namespace Divoom.HardwareMonitor;

// Private newline-delimited JSON over redirected standard I/O; no listening port.
// Requests are serialized. Tokens are used only by Start and never persisted or logged.
public static class DesktopBridge
{
    public static async Task<int> RunAsync(Func<HardwareSnapshot?> current, Action start, Action restart,
        Action<Action<HardwareSnapshot>> subscribe, string platform)
    {
        var options = new JsonSerializerOptions(AppSettings.JsonOptions) { WriteIndented = false };
        using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
        var gate = new object();
        bool closed = false;
        void Write(object message)
        {
            lock (gate)
            {
                if (closed) return;
                try { output.WriteLine(JsonSerializer.Serialize(message, options)); }
                catch (Exception error) when (error is IOException or ObjectDisposedException) { closed = true; }
            }
        }
        using var lifetime = new CancellationTokenSource();
        using var transport = new DivoomTransport();
        using var sender = new SendingService(transport, current);
        var settings = AppSettings.Load(); Localizer.Configure(settings.Language);
        string status = "本机监控启动中 · 尚未发送";
        sender.StatusChanged += message => { status = message; Write(new { type = "status", data = Localizer.Ui(message), running = sender.IsRunning, severity = message.StartsWith("发送失败") ? "error" : "info" }); };
        subscribe(snapshot =>
        {
            if (status == "本机监控启动中 · 尚未发送")
            {
                status = "本机监控运行中 · 尚未发送";
                Write(new { type = "status", data = Localizer.Ui(status), running = false, severity = "info" });
            }
            Write(new { type = "snapshot", data = snapshot });
        });
        start();
        try
        {
            while (await input.ReadLineAsync() is { } line)
            {
                long id = 0;
                try
                {
                    if (line.Length > 128_000) throw new ArgumentException("Request too large");
                    using var request = JsonDocument.Parse(line);
                    var root = request.RootElement;
                    id = root.GetProperty("id").GetInt64();
                    string command = root.GetProperty("command").GetString() ?? "";
                    var payload = root.TryGetProperty("payload", out var supplied) ? supplied : default;
                    object? result;
                    AppSettings ReadSettings()
                    {
                        var value = payload.GetProperty("settings").Deserialize<AppSettings>(options) ?? throw new ArgumentException("Settings missing");
                        if (value.DeviceHardware < 0 || value.DeviceId < 0 || value.LcdIndex is < 0 or > 4 || value.LcdIndependence is < 0 or > 1
                            || !Enum.IsDefined(value.SmartctlMode) || (value.Language != "auto" && !Localizer.Languages.Any(l => l.Code == value.Language))) throw new ArgumentException("Invalid settings");
                        value.DeviceIp = value.DeviceIp.Trim();
                        value.TimeGate = value.DeviceHardware is 400 or 402;
                        return value;
                    }
                    switch (command)
                    {
                        case "init":
                            result = new { settings, language = Localizer.Code, languages = Localizer.Languages, platform,
                                version = typeof(DesktopBridge).Assembly.GetName().Version?.ToString(3), snapshot = current(), running = sender.IsRunning, status = Localizer.Ui(status),
                                systemVolume = platform == "windows" ? Path.GetPathRoot(Environment.SystemDirectory) : "/" };
                            break;
                        case "snapshot": result = new { snapshot = current(), running = sender.IsRunning }; break;
                        case "discover": result = await transport.DiscoverAsync(lifetime.Token); break;
                        case "lcdMode": result = await transport.ReadIndependenceAsync(payload.GetProperty("deviceId").GetInt32(), lifetime.Token); break;
                        case "start":
                        {
                            var proposed = ReadSettings();
                            uint? localToken = DeviceSecrets.ParseLocalToken(payload.TryGetProperty("localToken", out var token) ? token.GetString() : null);
                            var target = new DeviceTarget(proposed.DeviceIp, proposed.DeviceId, proposed.TimeGate ? proposed.LcdIndex : 0,
                                proposed.TimeGate ? proposed.LcdIndependence : 0, proposed.DeviceHardware, localToken);
                            _ = target.PostUri;
                            if (current() is null) throw new InvalidOperationException(Localizer.Get("等待首份硬件数据…"));
                            await sender.StartAsync(target, proposed.GpuId, proposed.DiskId);
                            settings = proposed; settings.Save(); result = new { running = sender.IsRunning }; break;
                        }
                        case "stop": await sender.StopAsync(); result = new { running = false }; break;
                        case "preferences":
                            if (sender.IsRunning) throw new InvalidOperationException("Stop sending before changing the device");
                            settings = ReadSettings(); settings.Save(); result = true; break;
                        case "save":
                        {
                            var proposed = ReadSettings();
                            if (proposed.SmartctlMode == SmartctlMode.Custom && !File.Exists(proposed.SmartctlPath)) throw new ArgumentException(Localizer.Get("请先选择存在的 smartctl 文件。"));
                            settings = proposed; settings.Save(); Localizer.Configure(settings.Language); restart();
                            result = new { settings, language = Localizer.Code }; break;
                        }
                        case "rescan": restart(); result = true; break;
                        case "diagnostics":
                        {
                            string logPath = Path.Combine(AppSettings.DataDirectory, "monitor.log");
                            string log = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "";
                            if (log.Length > 64000) log = log[^64000..];
                            result = new { Version = typeof(DesktopBridge).Assembly.GetName().Version?.ToString(3), UI = "Tauri", Platform = platform,
                                OS = Environment.OSVersion.ToString(), Runtime = Environment.Version.ToString(), Snapshot = current(), RecentLog = log }; break;
                        }
                        case "quit": await sender.StopAsync(); Write(new { id, ok = true, data = true }); return 0;
                        default: throw new ArgumentException("Unknown desktop command");
                    }
                    Write(new { id, ok = true, data = result });
                }
                catch (Exception error) when (error is not OutOfMemoryException)
                {
                    Write(new { id, ok = false, error = Localizer.Ui(error.Message) });
                }
            }
            return 0;
        }
        catch (IOException) { return 0; } // UI went away: dispose the worker in the caller.
        finally { lifetime.Cancel(); await sender.StopAsync(); }
    }
}
