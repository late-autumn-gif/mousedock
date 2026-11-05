using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using MouseDock.Models;

namespace MouseDock.Services.Detection;

/// <summary>RK R98Pro V2 read-only identity, connection, and battery queries.</summary>
internal static class RkKeyboardProtocol
{
    // Official RK desktop driver: Dev/02B8/KB.ini, CDev3632::GetPower (0x40E3F0).
    // The receiver PID is shared; require identity 0A0000000046 before reading power.
    public static byte[] CreateIdentityQuery() => Query(0x05);
    public static byte[] CreateConnectionQuery() => Query(0x07);
    public static byte[] CreateBatteryQuery() => Query(0x4A);

    private static byte[] Query(byte command)
    {
        byte[] report = new byte[20];
        report[0] = 0x13;
        report[1] = command;
        report[2] = 1;
        report[19] = (byte)(0x13 + command + 1);
        return report;
    }

    private static bool Valid(byte[] report, byte command, byte length)
    {
        if (report == null || report.Length != 20 || report[0] != 0x13 ||
            report[1] != command || report[2] != 1 || report[3] != 0 || report[4] != length)
            return false;
        int checksum = 0;
        for (int i = 0; i < 19; i++) checksum += report[i];
        return (byte)checksum == report[19];
    }

    public static bool IsR98Identity(byte[] report) =>
        Valid(report, 0x05, 6) &&
        report.AsSpan(5, 6).SequenceEqual(new byte[] { 0x0A, 0, 0, 0, 0, 0x46 });

    public static bool? ParseConnection(byte[] report) =>
        Valid(report, 0x07, 1) && report[5] <= 1 ? report[5] == 1 : null;

    public static int ParseBattery(byte[] report) =>
        Valid(report, 0x4A, 2) && report[5] <= 100 ? report[5] : -1;

    public static bool IsSupportedInterface(ushort usagePage, ushort usage, ushort input, ushort output) =>
        usagePage == 0xFF02 && usage == 2 && input == 20 && output == 20;

    public static DetectedDevice TryRead(ushort vid, ushort pid, bool safeMode)
    {
        if (safeMode || vid != 0x258A || (pid != 0x01BB && pid != 0x0233)) return null;
        try { return ReadAsync(vid, pid).GetAwaiter().GetResult(); }
        catch { return null; }
    }

    private static async Task<DetectedDevice> ReadAsync(ushort vid, ushort pid)
    {
        foreach (var node in HidProbe.Enumerate(vid, pid).Where(n =>
            IsSupportedInterface(n.Caps.UsagePage, n.Caps.Usage,
                n.Caps.InputReportByteLength, n.Caps.OutputReportByteLength)))
        {
            using var handle = CreateFile(node.Path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (handle.IsInvalid) continue;
            using var stream = new FileStream(handle, FileAccess.ReadWrite, 20, true);
            using var timeout = new CancellationTokenSource(2000);
            var result = await ReadReceiverAsync(vid, pid,
                query => Exchange(stream, query, timeout.Token)).ConfigureAwait(false);
            if (result != null) return result;
        }
        return null;
    }

    internal static async Task<DetectedDevice> ReadReceiverAsync(ushort vid, ushort pid,
        Func<byte[], Task<byte[]>> exchange)
    {
        // The receiver answers status while disconnected; keyboard identity queries time out.
        bool? connected = ParseConnection(await exchange(CreateConnectionQuery()).ConfigureAwait(false));
        if (connected == null) return null;
        if (connected.Value &&
            !IsR98Identity(await exchange(CreateIdentityQuery()).ConfigureAwait(false))) return null;
        int level = connected.Value
            ? ParseBattery(await exchange(CreateBatteryQuery()).ConfigureAwait(false))
            : -1;
        return new DetectedDevice
        {
            Id = $"hid-{vid:X4}-{pid:X4}",
            Name = connected.Value ? "RK R98Pro V2" : "RK 无线接收器",
            Kind = DeviceKind.Keyboard,
            Transport = "2.4G",
            Level = level,
            Connected = connected.Value,
            Detail = $"{vid:X4}:{pid:X4}",
            Note = !connected.Value ? "接收器已插入，键盘未通过此通道连接"
                : level >= 0 ? "RK 官方协议电量；充电状态尚未确认"
                : "已识别 RK R98Pro V2，暂未读到电量"
        };
    }

    private static async Task<byte[]> Exchange(FileStream stream, byte[] query, CancellationToken cancellation)
    {
        await stream.WriteAsync(query, cancellation).ConfigureAwait(false);
        while (true)
        {
            byte[] response = new byte[20];
            int count = await stream.ReadAsync(response, cancellation).ConfigureAwait(false);
            if (count == 0) return Array.Empty<byte>();
            if (count == 20 && response[0] == 0x13 && response[1] == query[1]) return response;
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);
}
