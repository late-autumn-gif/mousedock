using System;
using System.IO;
using static MouseDock.Services.Detection.ExpansionFacts;

namespace MouseDock.Services.Detection;

internal static class HeadsetExpansionRazerPa
{
    internal static byte[] Request(bool blackshark, bool? remote = null)
    {
        var b = new byte[64];
        if (blackshark)
        {
            new byte[] { 2, 128, 8 }.CopyTo(b, 0);
            new byte[] { 80, 65, 8 }.CopyTo(b, 5);
            b[9] = 3; b[10] = 33;
            if (remote.HasValue)
            { b[2] = 7; b[7] = 14; b[9] = 2; b[10] = 225; b[11] = remote.Value ? (byte)1 : (byte)0; }
        }
        else
        {
            new byte[] { 1, 128, 8, 80, 65, 8, 8, 3, 33 }.CopyTo(b, 0);
            if (remote.HasValue)
            { b[2] = 7; b[5] = 14; b[7] = 2; b[8] = 225; b[9] = remote.Value ? (byte)1 : (byte)0; }
        }
        return b;
    }
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b, bool blackshark)
    {
        Require(b.Length is >= 16 and <= 64, "Invalid PA response size");
        int at = blackshark ? 12 : 13;
        Require(blackshark ? b[0] == 2 :
            b[0] == 1 && b[1] == 128 && b[3] == 80 && b[4] == 73 && b[5] == 8, "Invalid PA response header");
        Require(b.Length > at + 3 && b[at] == 33 && (blackshark ? b[at + 1] == 1 : b[at + 1] is 1 or 2) &&
            b[at + 2] is >= 1 and <= 32 && b.Length >= at + 3 + b[at + 2] && b[at + 3] <= 100,
            "Invalid PA battery payload");
        return Value(b[at + 3]);
    }
    internal static ReferenceBatteryProtocol.Reading Read(ExpansionWire wire, bool blackshark)
    {
        ReferenceBatteryProtocol.Reading result = null;
        for (int round = 0; round < 2; round++)
        {
            wire.Drain();
            bool enableAttempted = false;
            try
            {
                wire.Send(Request(blackshark, true), beforeWrite: () => enableAttempted = true);
                wire.Link.Delay(blackshark ? 35 : 50);
                result = null;
                for (int attempt = 0; attempt < (blackshark ? 3 : 4); attempt++)
                {
                    if (blackshark)
                    { wire.Send(Request(true, true)); wire.Link.Delay(35); }
                    try
                    {
                        var b = wire.Exchange(Request(blackshark), b =>
                        {
                            try { Decode(b, blackshark); return true; }
                            catch (InvalidDataException) { return false; }
                        }, blackshark ? 150 : 900);
                        result = Decode(b, blackshark); break;
                    }
                    catch (IOException) { }
                }
                Require(result != null, "No matching PA battery response");
            }
            finally { if (enableAttempted) wire.CleanupSend(Request(blackshark, false)); }
        }
        return result;
    }
}
