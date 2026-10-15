using System.Text.Json;

namespace Divoom.HardwareMonitor;

// Explicit, bounded LAN test: uses the same collector, protocol and sender as the desktop UI.
internal static class DeviceSendTest
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        var statuses = new List<string>();
        using var trace = new TraceHandler();
        try
        {
            if (args.Length < 3 || !int.TryParse(args[2], out int hardware)) throw new ArgumentException("用法: --send-test IPv4 Hardware [发送秒数 2..120] [DeviceId]");
            int seconds = args.Length > 3 ? Math.Clamp(int.Parse(args[3]), 2, 120) : 20;
            int deviceId = args.Length > 4 ? int.Parse(args[4]) : 0;
            var target = new DeviceTarget(args[1], deviceId, 0, 0, hardware, DeviceSecrets.ParseLocalToken(Environment.GetEnvironmentVariable("DIVOOM_LOCAL_TOKEN")));
            _ = target.PostUri;
            using var monitor = new MonitorService();
            monitor.Start();
            await Task.Delay(6000);
            var initial = monitor.Current ?? throw new IOException("本机采集未就绪。");
            var gpu = initial.Gpus.FirstOrDefault(x => x.IsDiscrete) ?? initial.Gpus.FirstOrDefault();
            int? systemDisk = initial.Volumes.FirstOrDefault(x => x.Name.Equals(Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase))?.DiskIndex;
            var disk = initial.Disks.FirstOrDefault(x => x.Index == systemDisk) ?? initial.Disks.FirstOrDefault();
            bool untraced = args.Contains("--untraced");
            using var transport = untraced ? new DivoomTransport() : new DivoomTransport(trace);
            using var sender = new SendingService(transport, () => monitor.Current);
            sender.StatusChanged += status => statuses.Add(status);
            await sender.StartAsync(target, gpu?.Id ?? "", disk?.Id ?? "");
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            await sender.StopAsync();
            int sent = statuses.Count(x => x.StartsWith("已连接 "));
            bool success = sent > 0 && !statuses.Any(x => x.StartsWith("发送失败"));
            output.WriteLine(JsonSerializer.Serialize(new { Success = success, Transport = untraced ? "Default (same as desktop)" : "Recorded", Endpoint = target.PostUri, Hardware = hardware, DurationSeconds = seconds, SuccessfulUpdates = sent, Cpu = initial.CpuName, Gpu = gpu?.Name, Disk = disk?.Name, Statuses = statuses, Exchanges = trace.Exchanges }, AppSettings.JsonOptions));
            return success ? 0 : 1;
        }
        catch (Exception error)
        {
            output.WriteLine(JsonSerializer.Serialize(new { Success = false, Error = error.Message, Statuses = statuses, Exchanges = trace.Exchanges }, AppSettings.JsonOptions));
            return 1;
        }
    }
    private sealed record Exchange(DateTimeOffset At, string Uri, string Request, int? HttpStatus, string? Response, string? Error);
    private sealed class TraceHandler() : DelegatingHandler(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) })
    {
        public List<Exchange> Exchanges { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(token);
            try
            {
                var response = await base.SendAsync(request, token);
                string result = await response.Content.ReadAsStringAsync(token);
                Exchanges.Add(new(DateTimeOffset.UtcNow, request.RequestUri!.ToString(), DeviceSecrets.Redact(body), (int)response.StatusCode, DeviceSecrets.Redact(result), null));
                return response;
            }
            catch (Exception error) { Exchanges.Add(new(DateTimeOffset.UtcNow, request.RequestUri!.ToString(), DeviceSecrets.Redact(body), null, null, error.Message)); throw; }
        }
    }
}
