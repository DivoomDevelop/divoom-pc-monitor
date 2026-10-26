using System.Globalization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Divoom.HardwareMonitor;

public sealed record DeviceInfo(int DeviceId, int Hardware, string DeviceName, string DevicePrivateIP)
{
    public override string ToString() => $"{DeviceName} · {DevicePrivateIP}";
}
public sealed record DeviceTarget(string Ip, int DeviceId, int LcdIndex, int LcdIndependence, int Hardware = 0, uint? LocalToken = null)
{
    public bool UsesLegacyEndpoint => Hardware is 90 or 92 or 400;
    public bool UsesScreenList => UsesLegacyEndpoint || Hardware is 402 or 511;
    public int? PcClockId => Hardware switch { 90 or 92 or 96 or 400 or 402 => 625, 530 => 3191, _ => null };
    public Uri PostUri
    {
        get
        {
            if (!IPAddress.TryParse(Ip.Trim(), out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || IPAddress.Any.Equals(address) || address.GetAddressBytes()[0] >= 224)
                throw new ArgumentException("请输入有效的设备 IPv4 地址，例如 192.168.1.100。");
            if (LcdIndex is < 0 or > 4 || LcdIndependence is < 0 or > 1) throw new ArgumentException("LCD 设置无效。");
            if (Hardware < 0) throw new ArgumentException("Hardware 型号不能为负数。");
            return UsesLegacyEndpoint ? new Uri($"http://{address}:80/post") : new Uri($"http://{address}:9000/divoom_api");
        }
    }
}
public static class DeviceSecrets
{
    public static uint? ParseLocalToken(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        return uint.TryParse(input.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out uint value) ? value : throw new ArgumentException(Localizer.Get("LocalToken 必须为非负整数。") + " " + Localizer.Get("APP → Wi-Fi 设备 → 设置 → 本地令牌"));
    }
    public static string Redact(string json)
    {
        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(json);
            void Hide(System.Text.Json.Nodes.JsonNode? item)
            {
                if (item is System.Text.Json.Nodes.JsonObject obj)
                    foreach (string key in obj.Select(x => x.Key).ToArray())
                        if (key.Equals("LocalToken", StringComparison.OrdinalIgnoreCase) || key.Equals("DeviceToken", StringComparison.OrdinalIgnoreCase)) obj[key] = "[已隐藏]";
                        else Hide(obj[key]);
                else if (item is System.Text.Json.Nodes.JsonArray array) foreach (var child in array) Hide(child);
            }
            Hide(node); return node?.ToJsonString(AppSettings.JsonOptions) ?? json;
        }
        catch (JsonException) { return json; }
    }
}
public sealed record DeviceScreen(int LcdId, string[] DispData);
public sealed record DevicePacket(string Command, DeviceScreen[] ScreenList);
public sealed record DeviceDisplayItem(string AppTitle, string AppData);
public sealed record ApiDevicePacket(string Command, int DeviceId, string AppName, DeviceDisplayItem[] DispData,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ClockId = null);

public static class DeviceProtocol
{
    public static ApiDevicePacket ToApiPacket(DevicePacket packet, int deviceId)
    {
        if (deviceId <= 0) throw new ArgumentException("9000 接口需要有效 DeviceId，请刷新选择设备或手动填写 DeviceId。");
        string[] titles = ["CPU Load", "GPU Load", "CPU Temp", "GPU Temp", "Memory", "Disk Temp"];
        if (packet.ScreenList.Length != 1 || packet.ScreenList[0].DispData.Length != titles.Length) throw new ArgumentException("PC 数据必须包含六项指标。");
        return new(packet.Command, deviceId, "PC Monitor", packet.ScreenList[0].DispData.Select((value, index) => new DeviceDisplayItem(titles[index], value)).ToArray());
    }
    public static string Format(Metric? metric, string suffix, DateTimeOffset now)
    {
        if (metric is null || !metric.IsAvailable(now)) return "--";
        return Math.Round(metric.Value!.Value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + suffix;
    }
    public static DevicePacket Create(HardwareSnapshot snapshot, string gpuId, string diskId, int lcdIndex, DateTimeOffset now)
    {
        var gpu = snapshot.Gpus.FirstOrDefault(x => x.Id == gpuId);
        var disk = snapshot.Disks.FirstOrDefault(x => x.Id == diskId);
        return new("Device/UpdatePCParaInfo", [new(lcdIndex,
            [Format(snapshot.CpuLoad, "%", now), Format(gpu?.Load, "%", now), Format(snapshot.CpuTemperature, "C", now),
             Format(gpu?.Temperature, "C", now), Format(snapshot.MemoryLoad, "%", now), Format(disk?.Temperature, "C", now)])]);
    }
}

public interface IDeviceTransport : IDisposable
{
    Task SelectClockAsync(DeviceTarget target, CancellationToken token);
    Task SendAsync(DeviceTarget target, DevicePacket packet, CancellationToken token);
}

public sealed class DivoomTransport : IDeviceTransport
{
    private sealed class DeviceResponseException(int code, string response) : HttpRequestException($"设备返回错误 {code}: {response}")
    {
        public bool NeedsClockCompatibility => code == 1 && response.Contains("Only accept JSON parameters", StringComparison.OrdinalIgnoreCase);
    }
    private readonly HttpClient _local;
    private readonly HttpClient _cloud;
    private readonly ConcurrentDictionary<string, byte> _clockCompatibility = new();
    public DivoomTransport(HttpMessageHandler? handler = null)
    {
        _local = handler is null ? new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) }) : new(handler);
        _local.Timeout = TimeSpan.FromSeconds(2);
        _cloud = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(5) }) { Timeout = TimeSpan.FromSeconds(5) };
    }
    public Task SelectClockAsync(DeviceTarget target, CancellationToken token) => target.PcClockId is { } clockId ? Post(target, new
    {
        Command = "Channel/SetClockSelectId", target.DeviceId, LcdIndex = target.LcdIndex, target.LcdIndependence, ClockId = clockId
    }, token) : Task.CompletedTask;
    public async Task SendAsync(DeviceTarget target, DevicePacket packet, CancellationToken token)
    {
        if (target.UsesScreenList) { await Post(target, packet, token); return; }
        var apiPacket = DeviceProtocol.ToApiPacket(packet, target.DeviceId);
        string key = target.PostUri + "|" + target.DeviceId;
        if (target.PcClockId is { } cachedClock && _clockCompatibility.ContainsKey(key)) apiPacket = apiPacket with { ClockId = cachedClock };
        try { await Post(target, apiPacket, token); }
        catch (DeviceResponseException error) when (apiPacket.ClockId is null && target.PcClockId.HasValue && error.NeedsClockCompatibility)
        {
            await Post(target, apiPacket with { ClockId = target.PcClockId }, token);
            _clockCompatibility[key] = 1;
        }
    }
    private async Task Post<T>(DeviceTarget target, T packet, CancellationToken token)
    {
        // Firmware expects a complete JSON body with Content-Length, not chunked JsonContent.
        // Buffer here so desktop requests and diagnostic requests use the same wire format.
        var jsonPacket = System.Text.Json.JsonSerializer.SerializeToNode(packet, AppSettings.JsonOptions)!.AsObject();
        if (target.LocalToken is { } localToken) jsonPacket["LocalToken"] = localToken;
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(jsonPacket, AppSettings.JsonOptions);
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        content.Headers.ContentLength = payload.Length;
        using var response = await _local.PostAsync(target.PostUri, content, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (body.Length == 0) return;
        using var json = JsonDocument.Parse(body);
        foreach (string name in new[] { "ReturnCode", "error_code" })
            if (json.RootElement.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int code) && code != 0)
                    throw new DeviceResponseException(code, DeviceSecrets.Redact(body));
                if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } error && error != "0")
                    throw new HttpRequestException($"设备返回错误（{target.PostUri}）: {DeviceSecrets.Redact(body)}；如需令牌，请在 APP 设备设置中查看本地令牌并填写 LocalToken。");
            }
    }
    public async Task<List<DeviceInfo>> DiscoverAsync(CancellationToken token)
    {
        using var response = await _cloud.GetAsync("http://app.divoom-gz.com/Device/ReturnSameLANDevice", token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        if (!json.RootElement.TryGetProperty("DeviceList", out var list) || list.ValueKind != JsonValueKind.Array) return [];
        var devices = new List<DeviceInfo>();
        foreach (var item in list.EnumerateArray())
        {
            string ip = item.TryGetProperty("DevicePrivateIP", out var value) ? value.GetString() ?? "" : "";
            if (!IPAddress.TryParse(ip, out _)) continue;
            devices.Add(new(item.TryGetProperty("DeviceId", out value) ? value.GetInt32() : 0,
                item.TryGetProperty("Hardware", out value) ? value.GetInt32() : 0,
                item.TryGetProperty("DeviceName", out value) ? value.GetString() ?? "Divoom" : "Divoom", ip));
        }
        return devices;
    }
    public async Task<int> ReadIndependenceAsync(int deviceId, CancellationToken token)
    {
        using var response = await _cloud.GetAsync($"http://app.divoom-gz.com/Channel/Get5LcdInfoV2?DeviceType=LCD&DeviceId={deviceId}", token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false));
        if (json.RootElement.TryGetProperty("LcdIndependence", out var mode) && mode.TryGetInt32(out int result) && result is 0 or 1) return result;
        throw new IOException("服务未返回 LCD 独立模式，可手动设置。");
    }
    public void Dispose() { _local.Dispose(); _cloud.Dispose(); }
}

public sealed class SendingService(IDeviceTransport transport, Func<HardwareSnapshot?> snapshot) : IDisposable
{
    private CancellationTokenSource? _stop;
    private Task? _task;
    public bool IsRunning => _task is { IsCompleted: false };
    public event Action<string>? StatusChanged;
    public async Task StartAsync(DeviceTarget target, string gpuKey, string diskId)
    {
        _ = target.PostUri;
        if (!target.UsesScreenList && target.DeviceId <= 0) throw new ArgumentException("9000 接口需要有效 DeviceId，请刷新选择设备或手动填写 DeviceId。");
        await StopAsync();
        _stop = new();
        CancellationToken token = _stop.Token;
        _task = Task.Run(async () =>
        {
            int failures = 0;
            bool selected = false;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var started = DateTimeOffset.UtcNow;
                    try
                    {
                        if (!selected)
                        {
                            StatusChanged?.Invoke(target.PcClockId is { } clock ? $"正在连接设备并选择表盘 {clock}…" : "正在连接设备…");
                            if (target.PcClockId.HasValue) await transport.SelectClockAsync(target, token);
                            selected = true;
                        }
                        var current = snapshot();
                        if (current is null) { StatusChanged?.Invoke("等待首份硬件数据…"); await Task.Delay(1000, token); continue; }
                        var gpu = current.Gpus.FirstOrDefault(x => x.Id == gpuKey || x.MatchKey == gpuKey);
                        await transport.SendAsync(target, DeviceProtocol.Create(current, gpu?.Id ?? "", diskId, target.LcdIndex, DateTimeOffset.UtcNow), token);
                        failures = 0;
                        StatusChanged?.Invoke($"已连接 {target.Ip} · LCD {target.LcdIndex + 1} · 最近发送 {DateTime.Now:HH:mm:ss}");
                        var remaining = TimeSpan.FromSeconds(2) - (DateTimeOffset.UtcNow - started);
                        if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                    catch (Exception e)
                    {
                        selected = false;
                        int seconds = RetrySeconds(failures++);
                        string status = $"发送失败，{seconds} 秒后重试: {e.Message}";
                        AppLog.Write(status);
                        StatusChanged?.Invoke(status);
                        await Task.Delay(TimeSpan.FromSeconds(seconds), token);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }, token);
    }
    public static int RetrySeconds(int failures) => failures >= 4 ? 30 : 2 << Math.Clamp(failures, 0, 3);
    public async Task StopAsync()
    {
        _stop?.Cancel();
        if (_task is not null) { try { await _task.ConfigureAwait(false); } catch (OperationCanceledException) { } }
        _stop?.Dispose(); _stop = null; _task = null;
        StatusChanged?.Invoke("已停止发送 · 本机监控继续运行");
    }
    public void Dispose() { _stop?.Cancel(); transport.Dispose(); }
}
