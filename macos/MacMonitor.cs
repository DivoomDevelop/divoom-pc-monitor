using System.Diagnostics;
using System.Text.Json;

namespace Divoom.HardwareMonitor.Mac;

public sealed class MacMonitor : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _workerStop;
    private Task? _worker;
    private HardwareSnapshot? _current;
    public HardwareSnapshot? Current => Volatile.Read(ref _current);
    public string Status { get; private set; } = "本机采集启动中";
    public event Action<HardwareSnapshot>? Updated;
    public void Start() => Restart();
    public void Restart()
    {
        _workerStop?.Cancel();
        var previous = _worker;
        _workerStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var token = _workerStop.Token;
        _worker = Task.Run(async () =>
        {
            if (previous is not null) { try { await previous; } catch (OperationCanceledException) { } }
            await Run(token);
        }, token);
    }
    private async Task Run(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(typeof(MacMonitor).Assembly.Location);
            start.ArgumentList.Add("--collect"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            using var process = new Process { StartInfo = start };
            try
            {
                Status = "本机采集初始化中"; process.Start();
                var stderr = process.StandardError.ReadToEndAsync(token);
                while (!token.IsCancellationRequested)
                {
                    string? line = await process.StandardOutput.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), token);
                    if (line is null)
                    {
                        string error = await stderr;
                        throw new IOException($"采集进程退出（{process.ExitCode}）: " + error[..Math.Min(error.Length, 2000)]);
                    }
                    if (!line.StartsWith('{') || line.Length > 2_000_000) continue;
                    try
                    {
                        var snapshot = JsonSerializer.Deserialize<HardwareSnapshot>(line);
                        if (snapshot is null) continue;
                        Volatile.Write(ref _current, snapshot); Status = "本机监控运行中"; Updated?.Invoke(snapshot);
                    }
                    catch (JsonException) { }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e) { Status = "采集失败，30 秒后重试: " + e.Message; AppLog.Write(Status); }
            finally { try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } } catch (InvalidOperationException) { } }
            await Task.Delay(TimeSpan.FromSeconds(30), token);
        }
    }
    public static async Task<int> Collect(int parentPid)
    {
        using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        try
        {
            using var collector = new MacCollector();
            while (true)
            {
                if (parentPid > 0)
                {
                    try { using var parent = Process.GetProcessById(parentPid); if (parent.HasExited) break; }
                    catch (ArgumentException) { break; }
                }
                output.WriteLine(JsonSerializer.Serialize(await collector.ReadAsync(CancellationToken.None)));
                await Task.Delay(2000);
            }
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    public void Dispose()
    {
        _stop.Cancel(); _workerStop?.Cancel();
        try { _worker?.Wait(TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
        _workerStop?.Dispose(); _stop.Dispose();
    }
}
