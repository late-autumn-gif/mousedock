using System;
using System.Buffers.Binary;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static class AtkZeroDpi
{
    internal const string Model = "ATK ZERO";
    private const byte ReadAddress = 0x17;
    private const byte SelectStage = 0x02;

    private sealed record Table(MouseDpiState State, byte[] Bytes);

    internal static byte[] ReadRequest(bool receiver, ushort address)
    {
        var request = VendorProtocol.BuildAtkZeroRequest(ReadAddress, receiver);
        request[3] = 10;
        request[7] = 13;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(8), address);
        request[10] = 10;
        SetChecksum(request);
        return request;
    }

    private static byte[] SelectionRequest(bool receiver, Table table, int index)
    {
        var request = VendorProtocol.BuildAtkZeroRequest(SelectStage, receiver);
        request[3] = 12;
        request[7] = 10;
        request[8] = (byte)index;
        // X, Y and BGR come from the fresh device table. The vendor sends zero in the next byte.
        table.Bytes.AsSpan(2 + index * 8, 7).CopyTo(request.AsSpan(9));
        request[17] = 1;
        SetChecksum(request);
        return request;
    }

    private static void SetChecksum(byte[] request)
    {
        int sum = 0;
        for (int i = 2; i < request.Length; i++) sum += request[i];
        request[1] = unchecked((byte)sum);
    }

    private static Table ParseTable(byte[] bytes, byte profile)
    {
        int slots = bytes[1];
        if (slots != 8 || bytes[0] >= slots) return null;
        var values = new int[slots];
        byte count = 0;
        bool split = false, preserved = true;
        for (int i = 0; i < slots; i++)
        {
            int offset = 2 + i * 8;
            int x = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
            int y = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 2));
            if (x == 0 && y == 0) continue;
            if (i != count || x is < 50 or > 50000 || y is < 50 or > 50000) return null;
            values[i] = x;
            count++;
            split |= x != y;
            preserved &= bytes[offset + 7] == 0;
        }
        if (bytes[0] >= count) return null;
        return new(new(profile, bytes[0], count, split, values, MouseDpiProtocol.AtkZero, Model,
            Writable: preserved && !split), bytes);
    }

    internal static MouseDpiState ReadOrChange(bool receiver, MouseDpiChange change,
        Func<byte[], byte, Func<byte[], bool>, byte[]> query)
    {
        byte[] ReadBlock(ushort address) => query(ReadRequest(receiver, address), ReadAddress, frame =>
            frame[6] == 13 && BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(7)) == address && frame[9] == 10);

        byte? ReadProfile()
        {
            var request = VendorProtocol.BuildAtkZeroRequest(0x09, receiver);
            request[3] = 19;
            request[7] = 17;
            SetChecksum(request);
            return query(request, 0x09, frame => frame[6] == 17 && frame[7] < 8)?[7];
        }

        Table Read()
        {
            byte? profile = ReadProfile();
            if (!profile.HasValue) return null;
            var bytes = new byte[70];
            for (int chunk = 0; chunk < 7; chunk++)
            {
                var frame = ReadBlock((ushort)(1 + chunk * 10));
                if (frame == null) return null;
                frame.AsSpan(10, 10).CopyTo(bytes.AsSpan(chunk * 10));
            }
            var first = ReadBlock(1);
            if (first == null || !first.AsSpan(10, 10).SequenceEqual(bytes.AsSpan(0, 10)) ||
                ReadProfile() != profile) return null;
            return ParseTable(bytes, profile.Value);
        }

        bool SameMouse() => !receiver ||
            VendorProtocol.IsAtkZeroPairedModel(query(VendorProtocol.BuildAtkZeroRequest(0x89, true), 0x89, null));

        var before = Read();
        if (change == null) return before?.State;
        if (before == null || !before.State.Writable || change.Expected.Protocol != MouseDpiProtocol.AtkZero ||
            change.Expected.Identity != Model || change.Expected.Profile != before.State.Profile ||
            change.Expected.Count != before.State.Count || !change.Expected.Values.SequenceEqual(before.State.Values) ||
            change.Index < 0 || change.Index >= before.State.Count || !SameMouse()) return null;
        if (change.Index == before.State.Index) return before.State;

        query(SelectionRequest(receiver, before, change.Index), SelectStage, null);
        var after = Read();
        return after != null && after.State.Profile == before.State.Profile &&
            after.State.Index == change.Index && after.Bytes.AsSpan(1, 65).SequenceEqual(before.Bytes.AsSpan(1, 65)) &&
            SameMouse() ? after.State : null;
    }
}
