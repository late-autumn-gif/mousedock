using System;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionFa09
{
    internal static byte[] Request(byte command,bool kzzi = false)
    {
        var b = Packet(20,19,command,1,0,(byte)(kzzi ? 6 : 0));
        int sum = 0;
        for (int i = 0; i < 19; i++) sum += b[i];
        b[19] = (byte)sum;
        return b;
    }
    internal static byte[] Payload(byte[] b,byte command,int length)
    {
        Require(b.Length == 20 && b[0] == 19 && b[1] == command && b[2] == 1 &&
            b[3] == 0 && b[4] == length,"Invalid FA09 command framing");
        int sum = 0;
        for (int i = 0; i < 19; i++) sum += b[i];
        Require((sum & 255) == b[19],"FA09 checksum mismatch");
        return b.AsSpan(5,length).ToArray();
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        bool kzzi = wire.Revision == 0x2005, f99 = wire.Revision == 0x2007;
        if (!kzzi && !f99) return KeyboardExpansionNotifications.Read(wire,false);
        Require(wire.Output == 20 && wire.Feature == 0,"FA09 dedicated model requires exact I/O20/Feature0");
        void Identity()
        {
            var b = wire.Exchange(Request(5,kzzi),b => b.Length == 20 && b[0] == 19 && b[1] == 5);
            var identity = Payload(b,5,6);
            Require(identity.AsSpan().SequenceEqual(new byte[] {3,0,0,0,(byte)(kzzi ? 3 : 1),(byte)(kzzi ? 0xA2 : 0x58)}),
                "FA09 downstream model mismatch");
        }
        Identity(); Identity();
        ReferenceBatteryProtocol.Reading result = null;
        if (f99)
        {
            for (int i = 0; i < 2; i++)
            {
                var b = wire.Exchange(Request(74),b => b.Length == 20 && b[0] == 19 && b[1] == 74);
                byte[] data = Payload(b,74,2);
                Require(data[0] <= 100,"Invalid F99 Pro battery");
                result = Value(data[0]);
            }
            Identity();
        }
        else
        {
            wire.CheckInput = b =>
            {
                if (b.Length == 20 && b[0] == 19 && b[1] == 10)
                {
                    Payload(b,10,4);
                    Require(b[5] != 2 && (b[7] & 0xF0) != 0x10,"KZZI link changed or numeric battery unavailable");
                }
            };
            result = KeyboardExpansionNotifications.Read(wire,false);
        }
        return result with { Model = kzzi ? "KZZI K75 V2" : "AULA F99 Pro",
            Identity = kzzi ? "0300000003A2" : "030000000158" };
    }
}
