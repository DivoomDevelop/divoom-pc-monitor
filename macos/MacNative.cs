using System.Runtime.InteropServices;
using System.Text;

namespace Divoom.HardwareMonitor.Mac;

internal sealed class MacNative : IDisposable
{
    internal const string SystemLibrary = "/usr/lib/libSystem.B.dylib";
    private readonly uint _host = mach_host_self();
    private uint[]? _ticks;
    internal static uint TaskPort
    {
        get
        {
            nint lib = NativeLibrary.Load(SystemLibrary);
            try { return unchecked((uint)Marshal.ReadInt32(NativeLibrary.GetExport(lib, "mach_task_self_"))); }
            finally { NativeLibrary.Free(lib); }
        }
    }
    public string CpuName { get; } = ReadText("machdep.cpu.brand_string") is { Length: > 0 } cpu ? cpu : ReadText("hw.model");
    public long TotalMemory { get; } = ReadInt64("hw.memsize");
    public Metric CpuLoad()
    {
        var ticks = new uint[4]; uint count = 4;
        int code = host_statistics(_host, 3, ticks, ref count);
        if (code != 0 || count != 4) return Metric.Missing("%", "Mach host_statistics", "CPU 调用失败: " + code);
        double? load = _ticks is null ? null : MacParsing.CpuPercent(_ticks, ticks);
        _ticks = ticks;
        return load is { } value ? Metric.Percent(value, "Mach CPU tick 差分") : Metric.Missing("%", "Mach", "等待下一次差分采样");
    }
    public (Metric Load, long Available) Memory()
    {
        var data = new uint[64]; uint count = (uint)data.Length;
        int code = host_statistics64(_host, 4, data, ref count);
        if (code != 0 || count < 36 || host_page_size(_host, out uint pageSize) != 0 || TotalMemory <= 0)
            return (Metric.Missing("%", "Mach VM", "内存调用失败: " + code), 0);
        long used = MacParsing.VmMemoryUsed(TotalMemory, data, pageSize);
        return (Metric.Percent(used * 100.0 / TotalMemory, "Mach VM · 应用+固定+压缩内存"), TotalMemory - used);
    }
    public static string ReadText(string name)
    {
        nuint size = 0;
        if (sysctlbyname(name, null, ref size, 0, 0) != 0 || size == 0 || size > 4096) return "";
        var buffer = new byte[(int)size];
        return sysctlbyname(name, buffer, ref size, 0, 0) == 0 ? Encoding.UTF8.GetString(buffer).TrimEnd('\0') : "";
    }
    private static long ReadInt64(string name)
    {
        nuint size = 8; var bytes = new byte[8];
        return sysctlbyname(name, bytes, ref size, 0, 0) == 0 && size == 8 ? BitConverter.ToInt64(bytes) : 0;
    }
    public void Dispose() => mach_port_deallocate(TaskPort, _host);
    [DllImport(SystemLibrary)] private static extern uint mach_host_self();
    [DllImport(SystemLibrary)] private static extern int mach_port_deallocate(uint task, uint name);
    [DllImport(SystemLibrary)] private static extern int host_statistics(uint host, int flavor, [Out] uint[] data, ref uint count);
    [DllImport(SystemLibrary)] private static extern int host_statistics64(uint host, int flavor, [Out] uint[] data, ref uint count);
    [DllImport(SystemLibrary)] private static extern int host_page_size(uint host, out uint pageSize);
    [DllImport(SystemLibrary)] private static extern int sysctlbyname([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [Out] byte[]? buffer, ref nuint size, nint newValue, nuint newSize);
}
