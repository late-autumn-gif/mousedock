using System;
using System.Buffers.Binary;
using System.Linq;
using System.Security.Cryptography;

namespace MouseDock.Services.Detection;

internal static class ScyroxV12Dpi
{
    private const byte ReportId = 8;
    private const byte ReadProfiles = 0x01;
    private const byte SelectDpi = 0x0A;
    private const byte ReadPairedDevice = 0x2E;
    private const byte AxisValues = 0x23;
    private const byte AxisFlags = 0x25;

    internal static bool Supports(ReferenceProfiles.Profile profile) =>
        profile is { Vid: 0x395A, Pid: 0x0006, Battery: ReferenceProfiles.Protocol.ScyroxV12 };

    internal static byte[] Request(ReadOnlySpan<byte> payload)
    {
        var report = new byte[64];
        report[0] = ReportId;
        payload.CopyTo(report.AsSpan(1));
        int checksum = ReportId;
        foreach (byte value in payload) checksum += value;
        report[^1] = unchecked((byte)checksum);
        return report;
    }

    private static int ReadWord(byte[] frame, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(offset, 2));

    internal static MouseDpiState ReadOrChange(ReferenceBatteryProtocol.ILink link, MouseDpiChange change)
    {
        long deadline = link.Now + 5000;
        bool Send(byte[] request)
        {
            // A prior battery query can leave the rest of the four-profile stream pending.
            for (int i = 0; i < 160 && link.Now < deadline; i++)
                if (link.Read(3) is not { Length: > 0 })
                    return link.Now < deadline && link.Write(request, 250);
            return false;
        }

        byte[] Receive(long until)
        {
            while (link.Now < until)
            {
                var frame = link.Read((int)Math.Min(75, until - link.Now));
                // S-Center rejects the error flag at payload offset 61.
                if (frame is { Length: 64 } && frame[0] == ReportId && frame[62] == 0) return frame;
            }
            return null;
        }

        byte[] PairedDevice()
        {
            if (!Send(Request([ReadPairedDevice, 0, 3]))) return null;
            long until = Math.Min(deadline, link.Now + 650);
            while (Receive(until) is { } frame)
            {
                if (frame[1] != ReadPairedDevice || frame[2] != 0 || frame[3] is 0 or > 58) continue;
                if (frame[6] != 1 || ReadWord(frame, 12) != 0x395A || ReadWord(frame, 14) != 0x0005)
                    return null;
                return frame.AsSpan(16, 13).ToArray();
            }
            return null;
        }

        MouseDpiState ReadState()
        {
            if (!Send(Request([ReadProfiles]))) return null;
            var configs = new byte[4][];
            var flags = new byte[4][];
            var axes = new byte[4][];
            int active = -1, profile = -1;
            long until = Math.Min(deadline, link.Now + 1400);
            while (Receive(until) is { } frame)
            {
                switch (frame[1])
                {
                    case ReadProfiles when frame[3] == 0:
                        return frame[2] == 0 && active >= 0
                            ? Parse(configs[active], flags[active], axes[active], (byte)active) : null;
                    case ReadProfiles:
                        if (frame[4] > 3 || frame[5] > 3 || configs[frame[5]] != null ||
                            (active >= 0 && active != frame[4])) return null;
                        active = frame[4];
                        profile = frame[5];
                        configs[profile] = frame;
                        break;
                    case AxisFlags when profile >= 0:
                        if (flags[profile] != null) return null;
                        flags[profile] = frame;
                        break;
                    case AxisValues when profile >= 0:
                        if (axes[profile] != null) return null;
                        axes[profile] = frame;
                        break;
                }
            }
            return null;
        }

        var identity = PairedDevice();
        if (identity == null) return null;
        var before = ReadState();
        if (before == null) return null;
        var confirmed = PairedDevice();
        if (confirmed == null || !identity.AsSpan().SequenceEqual(confirmed)) return null;
        string pairingKey = identity.Any(value => value is > 32 and < 127)
            ? Convert.ToHexString(SHA256.HashData(identity)) : null;
        before = before with { Identity = pairingKey, Writable = before.Writable && pairingKey != null };
        if (change == null) return before;
        if (!before.Writable || change.Expected.Protocol != MouseDpiProtocol.ScyroxV12 ||
            change.Expected.Identity != pairingKey || change.Expected.Profile != before.Profile ||
            !change.Expected.Values.SequenceEqual(before.Values) ||
            change.Index < 0 || change.Index >= before.Count) return null;
        if (change.Index == before.Index) return before;

        var payload = new byte[5 + before.Count * 2];
        payload[0] = SelectDpi;
        payload[2] = (byte)(2 + before.Count * 2);
        payload[3] = (byte)change.Index;
        payload[4] = before.Count;
        for (int i = 0; i < before.Count; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(5 + i * 2), (ushort)before.Values[i]);
        if (!Send(Request(payload))) return null;
        long ackUntil = Math.Min(deadline, link.Now + 650);
        byte[] ack;
        do { ack = Receive(ackUntil); } while (ack != null && ack[1] != SelectDpi);
        if (ack == null) return null;

        var after = ReadState();
        confirmed = PairedDevice();
        return after != null && after.Writable && after.Index == change.Index && after.Profile == before.Profile &&
            after.Values.SequenceEqual(before.Values) && confirmed != null && identity.AsSpan().SequenceEqual(confirmed)
            ? after with { Identity = pairingKey } : null;
    }

    private static MouseDpiState Parse(byte[] config, byte[] flags, byte[] axes, byte profile)
    {
        if (config == null || flags == null || config[3] is < 2 or > 58 || flags[2] != 0) return null;
        byte index = config[6], count = config[7];
        if (count is < 1 or > 5 || index >= count || flags[3] < count || flags[3] > 5) return null;
        var values = new int[count];
        bool split = false;
        for (int i = 0; i < count; i++)
        {
            int dpi = ReadWord(config, 8 + i * 2);
            if (flags[4 + i] > 1 || dpi is < 100 or > 50000) return null;
            if (flags[4 + i] == 1)
            {
                if (axes == null || axes[2] != 0 || axes[3] <= i || axes[3] > 5) return null;
                dpi = ReadWord(axes, 5 + i * 4);
                int y = ReadWord(axes, 7 + i * 4);
                if (dpi is < 100 or > 50000 || y is < 100 or > 50000) return null;
                split = true;
            }
            values[i] = dpi;
        }
        return new(profile, index, count, split, values, MouseDpiProtocol.ScyroxV12, Writable: !split);
    }
}
