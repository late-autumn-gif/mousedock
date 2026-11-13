using System;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionRazerV3
{
    internal static byte[] Request(byte command)
    {
        Require(command is 0x20 or 0x21, "Unsupported BlackShark V3 query");
        var b = Packet(64, 2);
        b[2] = 0x60; b[6] = 4; b[9] = command == 0x20 ? (byte)0 : (byte)0x80; b[10] = command;
        for (int i = 0; i < 62; i++) b[62] ^= b[i];
        return b;
    }
    internal static int DecodePro(byte[] b, byte command)
    {
        Require(b.Length == 64 && b[0] == 2 && b[1] == 2 && b[2] == 0x60 &&
            b[7] == 0 && b[8] == 0 && b[9] == (command == 0x20 ? 0 : 128) &&
            b[10] == command && b[11] == 1, "Invalid BlackShark V3 Pro response header");
        byte xor = 0; for (int i = 0; i < 62; i++) xor ^= b[i];
        int size = command == 0x20 ? 2 : 1;
        Require(xor == b[62] && b[12] == size && b[6] == 4 + size &&
            (command == 0x20 ? b[13] <= 1 && b[14] == 0 : b[13] <= 100), "Invalid V3 Pro checksum/payload");
        return b[13];
    }
    internal static ReferenceBatteryProtocol.Reading ReadPro(ExpansionWire wire, bool playStation)
    {
        int Get(byte command)
        {
            var b = wire.Exchange(Request(command), b =>
                b.Length == 64 && b[0] == 2 && b[10] == command, 1000, setOutput: true);
            return DecodePro(b, command);
        }
        int level = -1;
        for (int round = 0; round < 2; round++)
        {
            if (playStation) Require(Get(0x20) == 1, "BlackShark V3 Pro offline");
            level = Get(0x21);
        }
        if (playStation) Require(Get(0x20) == 1, "BlackShark V3 Pro disconnected");
        return Value(level);
    }
    internal static ReferenceBatteryProtocol.Reading ReadPassive(ExpansionWire wire)
    {
        wire.Drain();
        bool recovered = false;
        var raw = wire.Wait(b =>
        {
            if (b.Length != 64 || b[0] != 2) return false;
            if (!recovered && b[10] == 0x20 && b[11] is 1 or 2 && b[12] >= 1 && b[13] == 1)
            {
                recovered = true; wire.Link.Delay(600); wire.Send(Request(0x21), true);
            }
            return b[10] == 0x21 && b[11] is 1 or 2 && b[12] == 1 && b[13] <= 100;
        }, 3000);
        return Value(raw[13]);
    }
}
