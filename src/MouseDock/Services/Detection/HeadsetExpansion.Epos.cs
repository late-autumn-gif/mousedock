using System;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionEpos
{
    internal static byte Crc(ReadOnlySpan<byte> payload)
    {
        byte crc = 255;
        foreach (byte value in payload)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++) crc = unchecked((byte)((crc << 1) ^ ((crc & 128) != 0 ? 7 : 0)));
        }
        return crc;
    }
    internal static byte[] Request(int size)
    {
        var b = Packet(size, 1, 0xEB, 0xFE, 1, 0, 4, 0, 5, 0, 4, 0);
        new byte[] { 0x12, 0, 3, 0 }.CopyTo(b, 13);
        b[11] = Crc(b.AsSpan(13, 4)); return b;
    }
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b)
    {
        Require(b.Length is >= 19 and <= 1024 && b.AsSpan(0, 5).SequenceEqual(new byte[] { 1, 0xEB, 0xFE, 1, 0 }) &&
            b[6] == 0 && U16(b, 9) == 6 && b.AsSpan(13, 4).SequenceEqual(new byte[] { 0x12, 0, 3, 0 }) &&
            Crc(b.AsSpan(13, 6)) == b[11] && (b[17] | b[18]) <= 100, "Invalid EPOS battery CRC/framing");
        return Value(b[17] | b[18]);
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire)
    {
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
            result = Decode(wire.Exchange(Request(wire.Output),
                b => b.Length >= 19 && b[0] == 1 && b[1] == 0xEB && b[2] == 0xFE, 6000));
        return result;
    }
}
