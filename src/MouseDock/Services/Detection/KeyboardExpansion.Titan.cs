using System;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionTitan
{
    internal static byte[] Request(byte command)
    {
        Require(command is 0x55 or 0x12 or 0x1F,"Not a Titan battery query");
        var b = Packet(32,6,command,0,0,24);
        b[5] = unchecked((byte)(command + 24)); b[6] = (byte)((command + 24) >> 8);
        return b;
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        byte[] Get(byte command)
        {
            var packet = Request(command);
            return wire.Exchange(packet,b => b.Length == 32 && b[0] == 6 &&
                b.AsSpan(1,3).SequenceEqual(packet.AsSpan(1,3)),800)[1..];
        }
        void Online() => Require(Get(0x55)[7] == 1,"Titan keyboard offline");
        byte[] Identity()
        {
            var b = Get(0x12);
            Require(b[7] == 0 && b[8] == 0 && U16(b,9) is > 0 and < 65535 &&
                b[16] is 1 or 2 or 3,"Invalid Titan paired keyboard identity");
            return new byte[] { b[9],b[10],b[16] };
        }
        Online();
        var before = Identity();
        int level = -1;
        for (int i = 0; i < 2; i++)
        {
            var b = Get(0x1F);
            Require(b[7] <= 100 && b[8] <= 2 && b[9] <= 1,"Invalid Titan power fields");
            level = b[7];
        }
        Require(Identity().AsSpan().SequenceEqual(before),"Titan downstream keyboard changed");
        Online();
        return Value(-1,identity:Convert.ToHexString(before)) with
        { Diagnostic = $"device-scale={level}/100; exact percentage and retail model unavailable" };
    }
}
