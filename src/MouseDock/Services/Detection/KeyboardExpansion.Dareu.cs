using System;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionDareu
{
    internal static byte[] Request(byte target,bool power,bool flex)
    {
        return Packet(65,(byte)(flex ? 10 : 0),target,
            (byte)(power ? flex ? 4 : 3 : flex ? 3 : 7),(byte)(power ? 7 : 0),(byte)(power ? 0x80 : 0xA0),0,0);
    }
    internal static byte Target(byte[] b,ushort pid)
    {
        int count = b[6];
        Require(count <= 15,"Invalid DAREU paired-target count");
        byte found = 0;
        for (int i = 0; i < count; i++)
        {
            int at = 7 + i * 3;
            if (b[at] != 1 || (b[at + 1] << 8 | b[at + 2]) != pid) continue;
            Require(found == 0,"Ambiguous DAREU keyboard target");
            found = (byte)((i + 1) << 4);
        }
        Require(found != 0,"No connected known DAREU keyboard");
        return found;
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire,ushort receiver)
    {
        bool direct = receiver == 0x0051, flex = receiver == 0x0211;
        byte[] Get(byte target,bool power)
        {
            var packet = Request(target,power,flex);
            wire.CheckTime();
            Require(wire.Link.SetFeature(packet),"DAREU Feature request failed");
            wire.Trace?.Add("keyboard-set-feature",true,raw:packet,keep:true);
            if (!direct && !flex) wire.Link.Delay(50);
            for (int i = 0; i < 20; i++)
            {
                wire.CheckTime();
                var raw = wire.Link.GetFeature(wire.FeatureId,65);
                wire.CheckTime();
                Require(raw?.Length == 65 && raw[0] == wire.FeatureId,"Invalid DAREU Feature framing");
                wire.Trace?.Add("keyboard-get-feature",true,raw:raw,keep:true);
                var b = raw[1..];
                Require(b[2] == (power ? 7 : 0) && b[3] == (power ? 0x80 : 0xA0) &&
                    ((b[0] & 0xF0) == 0 || (b[0] & 0xF0) == target),"Unrelated DAREU target/command");
                int state = b[0] & 15;
                if (state <= 1) { wire.Link.Delay(10); continue; }
                Require(state == 2,"DAREU command rejected");
                return b;
            }
            throw new System.IO.IOException("DAREU reply remained pending");
        }
        byte target = direct ? (byte)0 : Target(Get(0,false),flex ? (ushort)0x0210 : (ushort)0x0051);
        int level = -1;
        bool charging = false;
        for (int i = 0; i < 2; i++)
        {
            var b = Get(target,true);
            Require(b[7] <= 100 && (!flex || b[7] > 0) && b[6] <= (flex ? 1 : 2),
                "Invalid DAREU power fields");
            level = b[7]; charging = b[6] != 0;
        }
        if (!direct) Require(Target(Get(0,false),flex ? (ushort)0x0210 : (ushort)0x0051) == target,
            "DAREU keyboard target changed");
        return Value(-1,flex ? charging : null,flex ? "DAREU FLEX87" : "DAREU A98",
            flex ? "260D:0210" : "260D:0051") with
            { Diagnostic = $"device-scale={level}/100; exact percentage unavailable" };
    }
}
