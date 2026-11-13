using System;
using System.Collections.Generic;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionRog
{
    internal static ReferenceBatteryProtocol.Reading DecodeDirect(byte[] b)
    {
        Require(b.Length == 64 && b[0] == 0x12 && b[1] == 1 && b[5] <= 100 && b[8] <= 1 &&
            b.AsSpan(2).IndexOfAnyExcept((byte)0) >= 0, "Invalid ROG direct power response");
        return Value(b[5], b[8] == 1);
    }
    internal static ReferenceBatteryProtocol.Reading ReadDirect(KeyboardExpansionWire wire, bool chargingKnown)
    {
        ReferenceBatteryProtocol.Reading value = null;
        for (int i = 0; i < 2; i++)
            value = DecodeDirect(wire.Exchange(Packet(wire.Output, 0, 0x12, 1),
                b => b.Length == 64 && b[0] == 0x12 && b[1] == 1, 1500));
        return chargingKnown ? value : value with { Charging = false, ChargingKnown = false,
            Status = ReferenceBatteryProtocol.State.Online };
    }

    private static readonly Dictionary<ushort, (string Name, int Offset)> Omni = new()
    {
        [0x1A85] = ("ROG Azoth", 6), [0x1B42] = ("ROG Azoth Extreme", 6),
        [0x1CF1] = ("ROG Azoth Extreme SE", 6), [0x1AB0] = ("ROG Strix Scope II 96", 6),
        [0x1B7A] = ("ROG Strix Scope II 96 RX", 6), [0x1B06] = ("ROG Falchion RX LP", 11),
        // Retain the existing C# route's verified Azoth X mapping.
        [0x1C25] = ("ROG Azoth X", 6)
    };
    private static readonly Dictionary<ushort, (string Name, int Offset)> EightK = new()
    {
        [0x1C12] = ("ROG Azoth 98 HE / LITE", 6), [0x1A85] = ("ROG Azoth", 6),
        [0x1B42] = ("ROG Azoth Extreme", 6), [0x1C25] = ("ROG Azoth X", 6),
        [0x1CF1] = ("ROG Azoth Extreme SE", 6), [0x1DA4] = ("ROG Azoth Extreme 20th", 6)
    };
    internal static ushort Paired(byte[] b, bool eightK)
    {
        Require(b.Length == 64 && b[0] == 1 && b[1] == 0xA0 &&
            !b.AsSpan().SequenceEqual(Packet(64, 1, 0xA0)), "Invalid ROG pairing response");
        var models = eightK ? EightK : Omni;
        int count = eightK ? U16(b, 3) : 14;
        Require(!eightK || b[2] == 0 && count <= 14, "Invalid ROG 8K pairing count");
        ushort found = 0;
        int keyboards = 0;
        for (int i = 5; i < 5 + count * 4; i += 4)
        {
            ushort pid = (ushort)U16(b, i);
            if (!eightK && pid == 0) break;
            bool known = models.ContainsKey(pid);
            if (eightK && (b[i + 2] == 2 || known))
            {
                Require(b[i + 2] == 2 && b[i + 3] == 4, "Invalid ROG 8K keyboard route");
                keyboards++;
                if (known) found = pid;
            }
            else if (!eightK && known) { keyboards++; found = pid; }
        }
        Require(keyboards == 1 && found != 0, "No unique known ROG keyboard pairing");
        return found;
    }
    internal static ReferenceBatteryProtocol.Reading DecodeOmni(byte[] b, ushort pid, bool eightK)
    {
        var models = eightK ? EightK : Omni;
        Require(models.TryGetValue(pid, out var model) && b.Length == 64 &&
            b[0] == 2 && b[1] == 0x12 && b[2] == 1 && b[5] != 0xFA &&
            !(b[5] == 0xFF && b[6] == 0xAA) && b[9] <= 1 &&
            (!eightK || b[3] == 0 && b[4] == 0) &&
            !b.AsSpan().SequenceEqual(Packet(64, 2, 0x12, 1)), "Invalid ROG power response");
        Require(b[model.Offset] <= 100, "Invalid ROG percentage");
        return Value(b[model.Offset], eightK ? null : b[9] == 1,
            model.Name, pid.ToString("X4"));
    }
    internal static ReferenceBatteryProtocol.Reading ReadOmni(KeyboardExpansionWire pair,
        KeyboardExpansionWire power, bool eightK)
    {
        byte[] Pair() => pair.Exchange(Packet(64, 1, 0xA0),
            b => b.Length == 64 && b[0] == 1 && b[1] == 0xA0, 1500);
        void Online()
        {
            byte[] b = power.Exchange(Packet(64, 2, 0x12, 3),
                b => b.Length == 64 && b[0] == 2 && b[1] == 0x12 && b[2] == 3, 1500);
            Require(b[3] == 0 && b[4] == 0 && b[5] <= 2, "ROG 8K keyboard is offline");
        }
        ushort pid = Paired(Pair(), eightK);
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
        {
            if (eightK) Online();
            result = DecodeOmni(power.Exchange(Packet(64, 2, 0x12, 1),
                b => b.Length == 64 && b[0] == 2 && b[1] == 0x12 && b[2] == 1, 1500), pid, eightK);
        }
        Require(Paired(Pair(), eightK) == pid, "ROG keyboard pairing changed");
        if (eightK) Online();
        return result;
    }
}
