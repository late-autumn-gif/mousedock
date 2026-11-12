using System;
using System.Buffers.Binary;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static class MchoseA7UltraDpi
{
    internal const byte ReportId = 0x12;
    internal const int ReportLength = 65;
    private const byte ReadCommand = 0x67;
    private const byte WriteCommand = 0x40;

    private sealed record Configuration(MouseDpiState State, byte UsbIndex, int[] X, int[] Y, byte Reserved);

    internal static byte[] ReadRequest()
    {
        var request = new byte[ReportLength];
        Array.Fill(request, (byte)0xFF);
        request[0] = ReportId;
        request[1] = ReadCommand ^ 0xFF;
        return request;
    }

    private static Configuration Parse(byte[] report)
    {
        if (report is not { Length: ReportLength } || report[0] != ReportId ||
            report[1] != (ReadCommand ^ 0xFF)) return null;
        var bytes = new byte[ReportLength];
        for (int i = 2; i < bytes.Length; i++) bytes[i] = (byte)(report[i] ^ 0xFF);
        byte profile = bytes[2], count = bytes[18];
        // The driver reads the four bit fields as one big-endian word, then
        // consumes them from its least-significant nibble in little-endian mode.
        byte wireless = (byte)(bytes[4] & 0x0F), usb = (byte)(bytes[3] & 0x0F);
        if (profile > 7 || count is < 1 or > 6 || wireless >= count || usb >= count) return null;

        var x = new int[6];
        var y = new int[6];
        bool separate = false;
        for (int i = 0; i < x.Length; i++)
        {
            x[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6 + i * 2));
            y[i] = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(53 + i * 2));
            if (i >= count) continue;
            if (x[i] is < 50 or > 42000 || (bytes[52] != 0 && y[i] is < 50 or > 42000)) return null;
            separate |= bytes[52] != 0 && x[i] != y[i];
        }
        bool writable = !separate && x.All(dpi => dpi is 0 or >= 50 and <= 42000) &&
            (x.SequenceEqual(y) || y.All(dpi => dpi == 0));
        var state = new MouseDpiState(profile, wireless, count, separate, x[..count], MouseDpiProtocol.MchoseA7Ultra,
            VendorProtocol.A7UltraName, Writable: writable);
        return new(state, usb, x, y, bytes[5]);
    }

    private static byte[] Selection(Configuration configuration, byte index)
    {
        var frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[1] = WriteCommand;
        // M HUB sets both connection indexes when selecting a stage in the active profile.
        frame[2] = frame[3] = index;
        frame[4] = configuration.Reserved;
        frame[17] = configuration.State.Count;
        frame[18] = 0xFF;
        for (int i = 0; i < configuration.X.Length; i++)
        {
            ushort dpi = (ushort)configuration.X[i];
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5 + i * 2), dpi);
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(19 + i * 2), dpi);
        }
        for (int i = 1; i < frame.Length; i++) frame[i] ^= 0xFF;
        return frame;
    }

    internal static MouseDpiState ReadOrChange(MouseDpiChange change, Func<byte[], byte[]> exchange,
        Func<byte[], bool> write, Func<bool> online)
    {
        var before = Parse(exchange(ReadRequest()));
        if (before == null || !online()) return null;
        if (change == null) return before.State;
        if (!before.State.Writable || change.Expected.Protocol != MouseDpiProtocol.MchoseA7Ultra ||
            change.Expected.Identity != VendorProtocol.A7UltraName || change.Expected.Profile != before.State.Profile ||
            !change.Expected.Values.SequenceEqual(before.State.Values) || change.Index < 0 || change.Index >= before.State.Count)
            return null;
        if (change.Index == before.State.Index) return before.State;
        if (!write(Selection(before, (byte)change.Index))) return null;
        var after = Parse(exchange(ReadRequest()));
        return after != null && after.State.Profile == before.State.Profile && after.State.Index == change.Index &&
            after.UsbIndex == change.Index && !after.State.SeparateAxes && after.X.SequenceEqual(before.X) &&
            after.Y.SequenceEqual(before.X) && online()
            ? after.State : null;
    }
}
