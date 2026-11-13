using System;
using System.Linq;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionRoyuan
{
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] raw, bool neon)
    {
        Require(raw.Length == 65 && raw[0] == 0, "Invalid ROYUAN Feature framing");
        var b = raw.AsSpan(1);
        Require(b.IndexOfAnyExcept((byte)0) >= 0 && b[0] <= 1, "Empty or invalid ROYUAN status");
        if (neon)
        {
            Require(b[3] == 0 && b[5] <= 1 && b[9] <= 1 && b[1] is >= 1 and <= 100,
                "Neon keyboard offline or battery invalid");
            return Value(b[1], b[9] == 1);
        }
        int offline = b[3], level = b[1];
        if (offline > 1) { offline = b[4]; level = b[2]; }
        Require(offline == 0 && level <= 100, "IROK keyboard offline or battery invalid");
        return Value(level);
    }
    internal static ReferenceBatteryProtocol.Reading DecodeLegacy(byte[] raw)
    {
        Require(raw.Length == 65 && raw[0] == 0, "Invalid legacy ROYUAN framing");
        var b = raw.AsSpan(1);
        Require(b[0] == 0 && b[2] == 0 && b[3] == 1 && b[1] is >= 1 and <= 100 &&
            b[4..].IndexOfAnyExcept((byte)0) < 0, "Legacy ROYUAN offline, busy or invalid status");
        return Value(b[1]);
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire, string family)
    {
        bool legacy = family == "RoyuanLegacy";
        if (legacy)
        {
            var service = wire.FeatureRead(wire.FeaturePacket(0xFD));
            Require(service[1] == 0xAA && service[2] == 0x55 &&
                service.AsSpan(3).IndexOfAnyExcept((byte)0) < 0, "ROYUAN wireless service not confirmed");
        }
        var packet = wire.FeaturePacket(legacy ? (byte)0xFB : (byte)0xF7);
        if (!legacy) packet[8] = 8;
        ReferenceBatteryProtocol.Reading result = null;
        for (int i = 0; i < 2; i++)
        {
            var next = legacy ? DecodeLegacy(wire.FeatureRead(packet)) :
                Decode(wire.FeatureRead(packet), family == "Neon");
            Require(!legacy || result == null || result.Level == next.Level, "ROYUAN battery changed during confirmation");
            result = next;
        }
        return result;
    }

    private static (int Level, bool Ready, bool CanSend) Status(KeyboardExpansionWire wire)
    {
        var b = wire.FeatureRead(wire.FeaturePacket(0xF7), 10)[1..];
        Require(b.Length >= 9 && new[] { 0, 3, 4, 5, 8 }.All(i => b[i] <= 1) &&
            b[6] is 1 or 3 && b[3] == 0 && b[8] == 0 && b[1] <= 100,
            "Relay keyboard offline, booting or invalid status");
        return (b[1], b[0] == 1, b[5] == 1);
    }
    private static int Idle(KeyboardExpansionWire wire)
    {
        var value = Status(wire);
        Require(!value.Ready && value.CanSend, "Relay receiver is busy");
        return value.Level;
    }
    private static uint Identity(KeyboardExpansionWire wire, bool valkyrie)
    {
        Idle(wire);
        wire.FeatureSend(0xF6, 0x0A);
        wire.Link.Delay(10);
        wire.FeatureSend(0x8F, 0, 0, 0, 0, 0, 0, 0x70, 0);
        for (int i = 0; i < 5; i++)
        {
            wire.Link.Delay(100);
            if (!Status(wire).Ready) continue;
            wire.FeatureSend(0xFC);
            wire.Link.Delay(10); wire.CheckTime();
            var raw = wire.Link.GetFeature(wire.FeatureId, wire.Feature);
            wire.CheckTime();
            Require(raw?.Length == wire.Feature && raw[0] == wire.FeatureId && raw[1] == 0x8F,
                "Invalid relay identity framing");
            wire.Trace?.Add("keyboard-get-feature", true, raw: raw, keep: true);
            uint id = U32(raw, 2);
            Require(valkyrie ? id is 3116 or 3165 or 3907 or 3997 : id is 1326 or 2382,
                "Unknown relay keyboard model");
            return id;
        }
        throw new System.IO.IOException("Relay identity did not become ready");
    }
    internal static ReferenceBatteryProtocol.Reading ReadRelay(KeyboardExpansionWire wire, bool valkyrie)
    {
        uint id = Identity(wire, valkyrie);
        int first = Idle(wire), second = Idle(wire);
        Require(valkyrie || first == second, "K19 battery changed during confirmation");
        Require(Identity(wire, valkyrie) == id, "Relay keyboard identity changed");
        int final = Idle(wire);
        Require(valkyrie || final == second, "K19 final battery confirmation changed");
        string model = id switch { 3116 => "Valkyrie VK99 MAX", 3165 => "Valkyrie VK99 Ultra",
            3907 => "Valkyrie VK99 V2", 3997 => "Valkyrie VK99 MAX V2", _ => "Keycool K19" };
        return Value(final, model: model, identity: id.ToString());
    }
}
