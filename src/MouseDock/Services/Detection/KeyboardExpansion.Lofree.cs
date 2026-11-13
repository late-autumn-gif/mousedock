using System;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionLofree
{
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        byte Get(byte command)
        {
            bool startAttempted = false;
            try
            {
                wire.Exchange(Packet(32,4,0,0,1),b => b.Length >= 4 && b[0] == 4 && b[3] == 1,
                    1500,allowEcho:true,beforeWrite:() => startAttempted = true);
                var b = wire.Exchange(Packet(32,4,0,0,command),b =>
                    b.Length is >= 9 and <= 32 && b[0] == 4 && (b[3] == command || b[3] == 0) &&
                    b[5] == 0 && b[6] == 0,1500,false);
                Require(b.AsSpan(1).IndexOfAnyExcept((byte)0) >= 0,"Empty Lofree response");
                return b[8];
            }
            finally
            {
                if (startAttempted)
                {
                    var stop = Packet(32,4,0,0,2);
                    bool sent = wire.Link.Write(stop,250);
                    wire.Trace?.Add("keyboard-cleanup-output",sent,raw:stop,keep:true);
                    Require(sent,"Lofree session stop failed");
                }
            }
        }
        int level = -1;
        for (int i = 0; i < 2; i++)
        {
            Require(Get(0xAA) == 1,"Lofree keyboard offline");
            level = Get(0x1A);
            Require(level <= 100,"Invalid Lofree percentage");
        }
        Require(Get(0xAA) == 1,"Lofree keyboard disconnected after read");
        return Value(level);
    }
}
