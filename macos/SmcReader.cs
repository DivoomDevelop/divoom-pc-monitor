using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Divoom.HardwareMonitor.Mac;

// Read-only AppleSMC user client; no fan control, writes, driver installation or elevation.
public sealed class SmcReader : IDisposable
{
    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private uint _connection;
    private readonly List<string> _cpu = [], _gpu = [];
    private readonly Dictionary<string, (uint Size, uint Type)> _info = new();
    public string Status { get; private set; } = "";
    public IReadOnlyList<string> CpuKeys => _cpu;
    public IReadOnlyList<string> GpuKeys => _gpu;
    public SmcReader()
    {
        uint service = IOServiceGetMatchingService(0, IOServiceMatching("AppleSMC"));
        if (service == 0) { Status = "未找到 AppleSMC，温度不可用"; return; }
        int code;
        try { code = IOServiceOpen(service, MacNative.TaskPort, 0, out _connection); }
        finally { IOObjectRelease(service); }
        if (code != 0) { Status = $"AppleSMC 打开失败: 0x{code:X8}（可能受系统权限限制）"; return; }
        var countBytes = ReadRaw("#KEY");
        if (countBytes is null || countBytes.Length < 4) { Status = "AppleSMC 无法枚举传感器"; return; }
        int count = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(countBytes), 16384u);
        for (int i = 0; i < count; i++)
        {
            var input = new byte[80]; input[42] = 8;
            BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(44), (uint)i);
            var output = Call(input);
            if (output is null) continue;
            uint key = BinaryPrimitives.ReadUInt32LittleEndian(output);
            string name = new([(char)(key >> 24), (char)((key >> 16) & 255), (char)((key >> 8) & 255), (char)(key & 255)]);
            if (IsCpuKey(name, RuntimeInformation.ProcessArchitecture == Architecture.Arm64)) _cpu.Add(name);
            if (IsGpuKey(name, RuntimeInformation.ProcessArchitecture == Architecture.Arm64)) _gpu.Add(name);
        }
        Status = $"AppleSMC 已连接，CPU 候选 {_cpu.Count} 项，GPU 候选 {_gpu.Count} 项";
    }
    public static bool IsCpuKey(string key, bool apple) => apple
        ? Regex.IsMatch(key, @"^T[pef][0-9A-Za-z]{2}$")
        : key is "TCAD" or "TC0D" or "TC0F" || Regex.IsMatch(key, @"^TC[0-9A-Fa-f][Cc]$");
    public static bool IsGpuKey(string key, bool apple) => apple ? Regex.IsMatch(key, @"^Tg[0-9A-Za-z]{2}$") : key is "TCGC" or "TG0D" or "TGDD";
    public Metric CpuTemperature() => MaxTemperature(_cpu, "未读到 CPU 核心/封装温度");
    public Metric GpuTemperature() => MaxTemperature(_gpu, "未读到 GPU 核心温度");
    private Metric MaxTemperature(IEnumerable<string> keys, string error)
    {
        var readings = keys.Select(key => (Key: key, Value: ReadTemperature(key))).Where(x => x.Value is > 0 and < 150).OrderByDescending(x => x.Value).ToArray();
        return readings.Length == 0 ? Metric.Missing("℃", "AppleSMC", error + "；" + Status) : Metric.Create(readings[0].Value!.Value, "℃", "AppleSMC · 最高核心 " + readings[0].Key);
    }
    private double? ReadTemperature(string key)
    {
        byte[]? bytes = ReadRaw(key);
        return bytes is null || !_info.TryGetValue(key, out var info) ? null : Decode(info.Type, bytes);
    }
    public static double? Decode(uint type, ReadOnlySpan<byte> bytes)
    {
        if (type == Key("sp78") && bytes.Length >= 2) return BinaryPrimitives.ReadInt16BigEndian(bytes) / 256.0;
        if (type == Key("flt ") && bytes.Length >= 4)
        {
            float value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
            return float.IsFinite(value) ? value : null;
        }
        if (type == Key("fpe2") && bytes.Length >= 2) return BinaryPrimitives.ReadUInt16BigEndian(bytes) / 4.0;
        return null;
    }
    public static uint Key(string key) => key.Length == 4 ? ((uint)key[0] << 24) | ((uint)key[1] << 16) | ((uint)key[2] << 8) | key[3] : throw new ArgumentException("SMC key 必须为四字符");
    private byte[]? ReadRaw(string key)
    {
        if (_connection == 0) return null;
        var input = new byte[80]; BinaryPrimitives.WriteUInt32LittleEndian(input, Key(key));
        if (!_info.TryGetValue(key, out var info))
        {
            input[42] = 9;
            var header = Call(input);
            if (header is null) return null;
            info = (BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(28)), BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(32)));
            if (info.Size == 0 || info.Size > 32) return null;
            _info[key] = info;
        }
        BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(28), info.Size); input[42] = 5;
        var output = Call(input);
        return output?.AsSpan(48, (int)info.Size).ToArray();
    }
    private byte[]? Call(byte[] input)
    {
        var output = new byte[80]; nuint size = 80;
        int code = IOConnectCallStructMethod(_connection, 2, input, 80, output, ref size);
        return code == 0 && size >= 80 && output[40] == 0 ? output : null;
    }
    public void Dispose() { if (_connection != 0) IOServiceClose(_connection); _connection = 0; }
    [DllImport(IOKit)] private static extern nint IOServiceMatching([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(IOKit)] private static extern uint IOServiceGetMatchingService(uint port, nint matching);
    [DllImport(IOKit)] private static extern int IOServiceOpen(uint service, uint task, uint type, out uint connection);
    [DllImport(IOKit)] private static extern int IOObjectRelease(uint service);
    [DllImport(IOKit)] private static extern int IOServiceClose(uint connection);
    [DllImport(IOKit)] private static extern int IOConnectCallStructMethod(uint connection, uint selector, byte[] input, nuint inputSize, [Out] byte[] output, ref nuint outputSize);
}
