using System;
using System.Threading;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionVgn
{
    private static int sequence;
    internal static ReferenceBatteryProtocol.Reading DecodeV4(byte[] b, byte report, bool exx)
    {
        Require(b.Length is 64 or 65 && b.AsSpan(64).IndexOfAnyExcept((byte)0) < 0 &&
            b[0] == report && b[1] == 0x87 && b[2] == 0 && b[4] == 1 && b[5] == 0 &&
            b[6] is >= 2 and <= 56 && b[7] <= 100 &&
            (!exx || b[8] is 0 or 1 or 16 or 17), "Invalid VGN V4/EXX response");
        return Value(b[7], exx ? (b[8] & 0xF0) != 0 : null);
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire, string family)
    {
        ReferenceBatteryProtocol.Reading result = null;
        for (int round = 0; round < 2; round++)
        {
            byte[] packet;
            if (family is "VgnV4" or "Exx")
            {
                packet = Packet(wire.Output, wire.OutputId, 0x87, 0, 0, 1, 0, 0);
                int sum = 0;
                for (int i = 0; i < 63; i++) sum += packet[i];
                packet[63] = unchecked((byte)(255 - sum));
                result = DecodeV4(wire.Exchange(packet,
                    b => b.Length >= 64 && b[0] == wire.InputId && b[1] == 0x87, 1500),
                    wire.InputId, family == "Exx");
            }
            else if (family == "VgnV3")
            {
                var b = wire.Exchange(KeyboardExpansionMchoseBy.Request(wire.Output),
                    b => b.Length >= 20 && b[0] == 19 && b[1] == 74, 1500);
                Require(b.Length is >= 20 and <= 65 && b.AsSpan(20).IndexOfAnyExcept((byte)0) < 0 &&
                    b[2] == 1 && b[3] == 0 && b[5] <= 100, "Invalid VGN V3 power report");
                result = Value(b[5]);
            }
            else
            {
                ushort seq = family == "AtkV75" ? (ushort)(Interlocked.Increment(ref sequence) % 65535 + 1) : (ushort)0;
                packet = Packet(wire.Output, 4, (byte)seq, (byte)(seq >> 8), 0x1A,
                    family == "AtkV75" ? (byte)0 : (byte)6, 0, 0, 0);
                var b = wire.Exchange(packet,
                    b => b.Length >= 14 && b[0] == 4 && b[1] == packet[1] &&
                        b[2] == packet[2] && b[3] == 0x1A, 1500);
                Require(b[8] <= 100 && b.AsSpan(8, 6).IndexOfAnyExcept((byte)0) >= 0,
                    "VGN battery unavailable");
                if (family == "AtkV75")
                    Require(b.Length is 64 or 65 && b.AsSpan(64).IndexOfAnyExcept((byte)0) < 0 && b[9] <= 2,
                        "Invalid ATK V75X response");
                // The reference marks V75X charging as unverified.
                result = Value(b[8]);
            }
        }
        return result;
    }
}
