using System;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionNotifications
{
    internal static ReferenceBatteryProtocol.Reading DecodeRapoo(byte[] b)
    {
        Require(b.Length is 9 or 10 && b[0] == 0xBD && b[1] == 1 && b[7] is 1 or 2 && b[8] <= 100,
            "Invalid Rapoo V700 notification");
        return Value(b[8], b[7] == 2);
    }
    internal static ReferenceBatteryProtocol.Reading DecodeAula(byte[] b)
    {
        Require(b.Length == 20 && b[0] == 19 && b[1] == 0x0A && b[2] == 1 && b[3] == 0 &&
            b[4] == 4 && b[5] == 5 && b[6] <= 100, "Not a keyboard percentage notification");
        int sum = 0;
        for (int i = 0; i < 19; i++) sum += b[i];
        Require((sum & 255) == b[19], "Keyboard notification checksum mismatch");
        return Value(b[6]);
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire, bool rapoo)
    {
        ReferenceBatteryProtocol.Reading last = null;
        for (int i = 0; i < 2; i++)
        {
            var b = wire.Wait(b =>
            {
                try { if (rapoo) DecodeRapoo(b); else DecodeAula(b); return true; }
                catch (System.IO.InvalidDataException) { return false; }
            }, 1500);
            last = rapoo ? DecodeRapoo(b) : DecodeAula(b);
        }
        return last;
    }
}
