using System;
using System.Linq;
using System.Security.Cryptography;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionSony
{
    internal static byte[] Request(byte command, ushort transaction)
    {
        Require(command is 1 or 2 or 4 && transaction >= 2, "Unsupported Sony HCI GET");
        byte[] b = [1, 0, 0xFC, 8, 0x96, 0xC3, command == 1 ? (byte)0x21 : (byte)0x41,
            command, 1, (byte)transaction, (byte)(transaction >> 8), 0];
        b[11] = unchecked((byte)b.Skip(4).Take(7).Sum(v => (int)v)); return b;
    }
    internal static byte[] Payload(byte[] b, byte command, ushort transaction)
    {
        if (b.Length is < 12 or > 62 || b[0] != 4 || b[1] != 255 || b[2] != b.Length - 3 ||
            b[3] != 0 || b[4] != 0x96 || b[5] != 0xC3 ||
            unchecked((byte)b.Skip(3).Take(b.Length - 4).Sum(v => (int)v)) != b[^1] ||
            b[6] != (command == 1 ? 0x12 : 0x14) || b[7] != command || b[8] != 0x10 ||
            U16(b, 9) != transaction) return null;
        return b[11..^1];
    }
    internal static void Online(byte[] data) =>
        Require(data?.Length == 1 && data[0] == 1, "Sony headset disconnected/pairing");
    internal static ReferenceBatteryProtocol.Reading Battery(byte[] data)
    {
        Require(data?.Length == 2 && data[0] <= 1 && data[1] <= 100, "Sony battery payload unavailable");
        return Value(-1, diagnostic: $"device-scale={data[1]}/100; exact percentage and charging unavailable");
    }
    internal static ReferenceBatteryProtocol.Reading ReadH5(ExpansionWire wire)
    {
        ushort transaction = (ushort)RandomNumberGenerator.GetInt32(2, 65528);
        byte[] Get(byte command)
        {
            transaction = (ushort)(transaction % 65534 + 2);
            var b = Packet(64, 2, 12);
            Request(command, transaction).CopyTo(b, 2);
            byte[] payload = null;
            wire.Exchange(b, raw =>
            {
                if (raw.Length != 64 || raw[0] != 2 || raw[1] is < 12 or > 62) return false;
                payload = Payload(raw.AsSpan(2, raw[1]).ToArray(), command, transaction);
                return payload != null;
            }, 800);
            return payload;
        }
        Online(Get(1)); Battery(Get(4)); var result = Battery(Get(4)); Online(Get(1));
        return result;
    }
}
