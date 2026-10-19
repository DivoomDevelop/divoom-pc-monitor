using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using System.Text.Json;

namespace Divoom.HardwareMonitor.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS() && !args.Contains("--ui-preview"))
        {
            Console.Error.WriteLine("此版本需要 macOS；Windows 请使用 Divoom.HardwareMonitor.exe。"); return 2;
        }
        if (args.FirstOrDefault() == "--collect") return MacMonitor.Collect(args.Length > 1 && int.TryParse(args[1], out int pid) ? pid : 0).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == "--bridge")
        {
            using var monitor = new MacMonitor();
            return DesktopBridge.RunAsync(() => monitor.Current, monitor.Start, monitor.Restart, handler => monitor.Updated += handler, "macos").GetAwaiter().GetResult();
        }
        if (args.FirstOrDefault() == "--probe") return Probe(args).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == "--send-test") return SendTest(args).GetAwaiter().GetResult();
        Localizer.Configure(AppSettings.Load().Language);
        try { AppBuilder.Configure<MacApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args); return 0; }
        catch (Exception e) { AppLog.Write(e.ToString()); Console.Error.WriteLine(e); return 1; }
    }
    private static async Task<int> Probe(string[] args)
    {
        int seconds = args.Length > 1 && int.TryParse(args[1], out int n) ? Math.Clamp(n, 2, 120) : 10;
        using var monitor = new MacMonitor(); monitor.Start(); await Task.Delay(seconds * 1000);
        using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        output.WriteLine(JsonSerializer.Serialize(new { monitor.Status, Snapshot = monitor.Current }, AppSettings.JsonOptions));
        return monitor.Current is null ? 1 : 0;
    }
    private static async Task<int> SendTest(string[] args)
    {
        if (args.Length < 5 || !int.TryParse(args[2], out int hardware) || !int.TryParse(args[3], out int seconds) || !int.TryParse(args[4], out int deviceId))
        { Console.Error.WriteLine("--send-test IP Hardware Seconds DeviceId"); return 2; }
        using var monitor = new MacMonitor(); monitor.Start(); await Task.Delay(6000);
        using var transport = new DivoomTransport(); using var sender = new SendingService(transport, () => monitor.Current);
        var statuses = new List<string>(); int successes = 0;
        sender.StatusChanged += status => { lock (statuses) { statuses.Add(status); if (status.StartsWith("已连接")) successes++; } };
        var snapshot = monitor.Current;
        try
        {
            await sender.StartAsync(new(args[1], deviceId, 0, 0, hardware, DeviceSecrets.ParseLocalToken(Environment.GetEnvironmentVariable("DIVOOM_LOCAL_TOKEN"))), snapshot?.Gpus.FirstOrDefault()?.Id ?? "", snapshot?.Disks.FirstOrDefault()?.Id ?? "");
            await Task.Delay(Math.Clamp(seconds, 2, 120) * 1000); await sender.StopAsync();
            using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            output.WriteLine(JsonSerializer.Serialize(new { Success = successes > 0, SuccessfulUpdates = successes, Statuses = statuses, Snapshot = monitor.Current }, AppSettings.JsonOptions));
            return successes > 0 ? 0 : 1;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}

public sealed class MacApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            desktop.MainWindow = new MainWindow(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
