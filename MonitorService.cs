using System.Diagnostics;
using System.Text.Json;

namespace Divoom.HardwareMonitor;

public sealed class MonitorService : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource? _workerStop;
    private SensorFrame? _sensors;
    private HardwareSnapshot? _current;
    private string _workerStatus = "温度采集器启动中";
    private Task? _nativeTask, _sensorTask;
    public HardwareSnapshot? Current => Volatile.Read(ref _current);
    public event Action<HardwareSnapshot>? Updated;
    public void Start()
    {
        _nativeTask = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    using var native = new NativeMetrics();
                    while (!_stop.IsCancellationRequested)
                    {
                        var snapshot = native.Read(Volatile.Read(ref _sensors), _workerStatus);
                        Volatile.Write(ref _current, snapshot);
                        Updated?.Invoke(snapshot);
                        await Task.Delay(1000, _stop.Token);
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { break; }
                catch (Exception e) { AppLog.Write("基础采集失败: " + e); }
                await Task.Delay(5000, _stop.Token);
            }
        }, _stop.Token);
        RestartSensors();
    }
    public void RestartSensors()
    {
        _workerStop?.Cancel();
        var previous = _sensorTask;
        _workerStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        var token = _workerStop.Token;
        _sensorTask = Task.Run(async () =>
        {
            if (previous is not null) { try { await previous; } catch (OperationCanceledException) { } }
            await RunWorker(token);
        }, token);
    }
    public static ProcessStartInfo SelfStart(params string[] arguments)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(typeof(MonitorService).Assembly.Location);
        foreach (string arg in arguments) info.ArgumentList.Add(arg);
        return info;
    }
    private async Task RunWorker(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var process = new Process { StartInfo = SelfStart("--sensors", Environment.ProcessId.ToString()) };
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            try
            {
                _workerStatus = "温度采集器初始化中";
                process.Start();
                // Drain stderr to avoid a blocked child if a native SDK prints diagnostics.
                var stderr = process.StandardError.ReadToEndAsync(token);
                while (!token.IsCancellationRequested)
                {
                    string? line = await process.StandardOutput.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), token);
                    if (line is null) throw new IOException("温度采集进程已退出");
                    if (line.Length > 2_000_000 || !line.StartsWith('{')) continue;
                    try
                    {
                        var frame = JsonSerializer.Deserialize<SensorFrame>(line);
                        if (frame is null) continue;
                        Volatile.Write(ref _sensors, frame);
                        _workerStatus = "";
                    }
                    catch (JsonException) { /* A vendor library may write unrelated diagnostic lines. */ }
                }
                _ = await stderr;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception e)
            {
                _workerStatus = "温度采集暂不可用，30 秒后重试: " + e.Message;
                AppLog.Write(_workerStatus);
            }
            finally
            {
                try { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } } catch (InvalidOperationException) { }
            }
            await Task.Delay(TimeSpan.FromSeconds(30), token);
        }
    }
    public void Dispose()
    {
        _stop.Cancel();
        _workerStop?.Cancel();
        try { Task.WhenAll(new[] { _nativeTask, _sensorTask }.OfType<Task>()).Wait(TimeSpan.FromSeconds(3)); } catch (AggregateException) { }
        _workerStop?.Dispose();
        _stop.Dispose();
    }
}
