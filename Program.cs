using System.Diagnostics;
using System.Text.Json;

namespace Divoom.HardwareMonitor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--bridge")
        {
            using var monitor = new MonitorService();
            return DesktopBridge.RunAsync(() => monitor.Current, monitor.Start, monitor.RestartSensors, handler => monitor.Updated += handler, "windows").GetAwaiter().GetResult();
        }
        if (args.FirstOrDefault() == "--sensors") return SensorWorker.Run(args.Length > 1 && int.TryParse(args[1], out int pid) ? pid : 0);
        if (args.FirstOrDefault() == "--send-test") return DeviceSendTest.RunAsync(args).GetAwaiter().GetResult();
        if (args.FirstOrDefault() == "--probe")
        {
            int seconds = args.Length > 1 && int.TryParse(args[1], out int value) ? Math.Clamp(value, 2, 300) : 8;
            using var monitor = new MonitorService();
            monitor.Start();
            Thread.Sleep(seconds * 1000);
            using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            output.WriteLine(JsonSerializer.Serialize(monitor.Current, AppSettings.JsonOptions));
            return monitor.Current is null ? 1 : 0;
        }
        Localizer.Configure(AppSettings.Load().Language);
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => { AppLog.Write(e.Exception.ToString()); MessageBox.Show("操作失败，程序已记录诊断信息。\n" + e.Exception.Message, "Divoom 硬件监控", MessageBoxButtons.OK, MessageBoxIcon.Warning); };
        try { Application.Run(new MainForm()); return 0; }
        catch (Exception e) { AppLog.Write(e.ToString()); MessageBox.Show("启动失败: " + e.Message, "Divoom 硬件监控"); return 1; }
    }
}
