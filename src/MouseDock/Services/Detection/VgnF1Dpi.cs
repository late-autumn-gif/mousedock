using System;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static class VgnF1Dpi
{
    private const byte GetProfile = 0x0E;
    private const byte ReadSetting = 0x08;
    private const byte WriteSetting = 0x07;
    private const byte StageCount = 2;
    private const byte CurrentStage = 4;
    private const byte StageTable = 12;

    private sealed record Table(MouseDpiState State, byte[] RawStages);

    internal static byte[] Request(byte command, byte address = 0, byte length = 0, byte value = 0)
    {
        var frame = new byte[17];
        frame[0] = 8;
        frame[1] = command;
        frame[4] = address;
        frame[5] = length;
        if (command == WriteSetting)
        {
            frame[6] = value;
            frame[7] = (byte)(0x55 - value);
        }
        VgnF1Protocol.SetChecksum(frame);
        return frame;
    }

    internal static bool MatchesResponse(byte[] frame, byte[] request)
    {
        if (!VgnF1Protocol.ValidEnvelope(frame, request) || !frame.AsSpan(3, 2).SequenceEqual(request.AsSpan(3, 2)))
            return false;
        return request[1] switch
        {
            GetProfile => frame[5] == 1 && frame[6] <= 7,
            ReadSetting => frame[5] == request[5],
            WriteSetting => true,
            _ => false
        };
    }

    internal static MouseDpiState ReadOrChange(MouseDpiChange change, string identity,
        Func<byte[], byte[]> exchange, Func<bool> stillPaired)
    {
        byte[] Read(byte command, byte address = 0, byte length = 0)
        {
            var request = Request(command, address, length);
            var frame = exchange(request);
            return MatchesResponse(frame, request) ? frame : null;
        }

        Table ReadTable()
        {
            var profile = Read(GetProfile);
            var settings = Read(ReadSetting, StageCount, 4);
            if (profile == null || settings == null || !ValidPair(settings, 6) || !ValidPair(settings, 8)) return null;
            byte count = settings[6], index = settings[8];
            if (count is < 1 or > 8 || index >= count) return null;
            var x = new int[count];
            var raw = new byte[count * 4];
            bool separate = false;
            for (int slot = 0; slot < count; slot += 2)
            {
                byte length = (byte)(slot + 1 < count ? 8 : 4);
                var chunk = Read(ReadSetting, (byte)(StageTable + slot * 4), length);
                if (chunk == null) return null;
                chunk.AsSpan(6, length).CopyTo(raw.AsSpan(slot * 4));
                for (int i = 0; i < length / 4; i++)
                {
                    var record = chunk.AsSpan(6 + i * 4, 4);
                    if (((record[0] + record[1] + record[2] + record[3]) & 0xFF) != 0x55) return null;
                    int axes = record[2];
                    x[slot + i] = Decode(record[0], axes & 15);
                    int y = Decode(record[1], axes >> 4);
                    if (x[slot + i] > 52000 || y > 52000) return null;
                    separate |= x[slot + i] != y;
                }
            }
            var confirmedSettings = Read(ReadSetting, StageCount, 4);
            var confirmedProfile = Read(GetProfile);
            if (confirmedSettings == null || confirmedProfile == null || confirmedProfile[6] != profile[6] ||
                !settings.AsSpan(6, 4).SequenceEqual(confirmedSettings.AsSpan(6, 4))) return null;
            return new(new(profile[6], index, count, separate, x, MouseDpiProtocol.VgnF1Moba,
                identity, Writable: !separate), raw);
        }

        var before = ReadTable();
        if (before == null || !stillPaired()) return null;
        if (change == null) return before.State;
        if (!before.State.Writable || change.Expected.Protocol != MouseDpiProtocol.VgnF1Moba ||
            change.Expected.Identity != identity || change.Expected.Profile != before.State.Profile ||
            !change.Expected.Values.SequenceEqual(before.State.Values) || change.Index < 0 || change.Index >= before.State.Count)
            return null;
        if (change.Index == before.State.Index) return before.State;
        exchange(Request(WriteSetting, CurrentStage, 2, (byte)change.Index));
        var after = ReadTable();
        return after != null && after.State.Index == change.Index && after.State.Profile == before.State.Profile &&
            after.RawStages.AsSpan().SequenceEqual(before.RawStages) && stillPaired() ? after.State : null;
    }

    private static bool ValidPair(byte[] frame, int offset) => ((frame[offset] + frame[offset + 1]) & 0xFF) == 0x55;

    // Each axis uses two high bits and two doubling flags in its half-byte.
    private static int Decode(byte low, int flags) => (1 + low + ((flags >> 2) << 8)) * 50 *
        (1 << ((flags & 1) + ((flags >> 1) & 1)));
}
