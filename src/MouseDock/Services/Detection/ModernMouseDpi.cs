using System;
using System.Buffers.Binary;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static class ModernMouseDpi
{
    internal static byte[] BuildRequest(ushort command, ReadOnlySpan<byte> data)
    {
        var frame = new byte[64];
        frame[0] = 0x4D;
        frame[1] = 1;
        frame[2] = 1;
        frame[3] = (byte)data.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), command);
        data.CopyTo(frame.AsSpan(8));
        frame[8 + data.Length] = Checksum(frame.AsSpan(2, 6 + data.Length));
        return frame;
    }

    private static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        byte sum = 0;
        foreach (byte value in bytes) sum ^= value;
        return sum;
    }

    internal static bool TryPayload(byte[] frame, ushort command, int minimum, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (frame?.Length != 64 || frame[0] != 0x4D || frame[1] != 1 || frame[2] != 1 ||
            frame[3] < minimum || frame[3] > 55 || BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(4)) != command ||
            frame[6] != 0 || frame[7] != 0 || frame[8 + frame[3]] != Checksum(frame.AsSpan(2, 6 + frame[3])))
            return false;
        payload = frame.AsSpan(8, frame[3]);
        return true;
    }

    internal static MouseDpiState ParseSettings(byte[] frame, byte profile)
    {
        if (!TryPayload(frame, 3, 17, out var data) || profile > 2 || data[0] != profile || data[1] != 0 ||
            data[2] is < 1 or > 6 || data[3] >= data[2] || data[4] > 1) return null;
        var values = new int[6];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt16LittleEndian(data[(5 + i * 2)..]);
            if (i < data[2] && values[i] is < 200 or > 50000) return null;
        }
        return new(profile, data[3], data[2], data[4] == 1, values);
    }

    internal static byte[] BuildSelection(MouseDpiState state, int index)
    {
        if (state.SeparateAxes || index < 0 || index >= state.Count) return null;
        Span<byte> data = stackalloc byte[17];
        data[0] = state.Profile;
        data[1] = data[2] = 0;
        data[3] = state.Count;
        data[4] = (byte)index;
        for (int i = 0; i < state.Values.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(data[(5 + i * 2)..], (ushort)state.Values[i]);
        return BuildRequest(0x0103, data);
    }

    internal static MouseDpiState ReadSettings(ReferenceBatteryProtocol.ILink link, MouseDpiChange change)
    {
        long deadline = link.Now + 1700;
        byte[] Exchange(ushort command, byte[] request, int minimum)
        {
            int drain = 0;
            while (link.Now < deadline && drain < 24 && link.Read(1) is { Length: > 0 }) drain++;
            if (drain == 24 || link.Now >= deadline || !link.Write(request, 200)) return null;
            long end = Math.Min(deadline, link.Now + 350);
            while (link.Now < end)
            {
                var reply = link.Read((int)Math.Max(1, end - link.Now));
                if (reply == null) return null;
                if (!reply.AsSpan().SequenceEqual(request) && TryPayload(reply, command, minimum, out _)) return reply;
            }
            return null;
        }

        MouseDpiState Read()
        {
            var performance = Exchange(2, BuildRequest(2, []), 9);
            if (!TryPayload(performance, 2, 9, out var settings) || settings[0] > 2) return null;
            byte profile = settings[0];
            return ParseSettings(Exchange(3, BuildRequest(3, [profile, 0]), 17), profile);
        }

        var before = Read();
        if (change == null) return before;
        if (before == null || change.Expected.Protocol != MouseDpiProtocol.Mhub || before.Profile != change.Expected.Profile || before.Count != change.Expected.Count ||
            before.SeparateAxes || !before.Values.SequenceEqual(change.Expected.Values)) return null;
        if (before.Index == change.Index) return before;
        var request = BuildSelection(before, change.Index);
        if (request == null) return null;

        Exchange(0x0103, request, 0);
        var after = Read();
        return after != null && after.Profile == before.Profile && after.Index == change.Index &&
            !after.SeparateAxes && after.Count == before.Count && after.Values.SequenceEqual(before.Values) ? after : null;
    }
}
