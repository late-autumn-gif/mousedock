using System;
using System.Buffers.Binary;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static class RogOmniMouseDpi
{
    private const byte ReportId = 3;
    private const byte ReadCommand = 0x12;
    private sealed record Table(MouseDpiState State, int[] Y);

    internal static bool Matches(byte[] report, byte subcommand, ushort index) =>
        report is { Length: 64 } && report[0] == ReportId && report[1] == ReadCommand &&
        report[2] == subcommand && BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(3)) == index;

    internal static MouseDpiState ReadOrChange(MouseDpiChange change, Func<byte, ushort, byte[]> query,
        Func<byte, bool> select, Func<bool> stillPaired)
    {
        Table Read()
        {
            var profile = query(0, 0);
            if (!Matches(profile, 0, 0) || profile[11] > 7 || profile[19] is < 1 or > 4 ||
                profile[12] == 0 || profile[12] > profile[19]) return null;
            var axes = query(4, 2);
            if (!Matches(axes, 4, 2)) return null;
            var confirmation = query(0, 0);
            if (!Matches(confirmation, 0, 0) || profile[19] != confirmation[19] ||
                !profile.AsSpan(11, 4).SequenceEqual(confirmation.AsSpan(11, 4))) return null;

            byte count = profile[19], index = (byte)(profile[12] - 1);
            var x = new int[count];
            var y = new int[count];
            for (int i = 0; i < count; i++)
            {
                x[i] = Decode(axes, 5 + i * 4);
                y[i] = Decode(axes, 7 + i * 4);
                if (x[i] is < 100 or > 42000 || y[i] is < 100 or > 42000) return null;
            }
            bool split = !x.SequenceEqual(y);
            return new(new(profile[11], index, count, split, x, MouseDpiProtocol.RogKerisIIOmni,
                RogOmniMouseProtocol.Model, Writable: !split), y);
        }

        var before = Read();
        if (before == null || !stillPaired()) return null;
        if (change == null) return before.State;
        if (!before.State.Writable || change.Expected.Protocol != MouseDpiProtocol.RogKerisIIOmni ||
            change.Expected.Identity != RogOmniMouseProtocol.Model || change.Expected.Profile != before.State.Profile ||
            !change.Expected.Values.SequenceEqual(before.State.Values) || change.Index < 0 || change.Index >= before.State.Count)
            return null;
        if (change.Index == before.State.Index) return before.State;
        if (!select((byte)(change.Index + 1))) return null;
        var after = Read();
        return after != null && after.State.Profile == before.State.Profile && after.State.Index == change.Index &&
            after.State.Values.SequenceEqual(before.State.Values) && after.Y.SequenceEqual(before.Y) && stillPaired()
            ? after.State : null;
    }

    private static int Decode(byte[] report, int offset) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(report.AsSpan(offset)) + 1) * 50;
}
