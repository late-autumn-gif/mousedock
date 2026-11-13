using System;
using System.Linq;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionGlw
{
    internal static byte[] Request(byte command, int offset, int count)
    {
        Require(command == 4 && offset == 0 && count == 56 ||
            command == 5 && offset is >= 0 and < 256 && (offset % 64 == 0 && count == 56 ||
            offset % 64 == 56 && count == 8), "Invalid GLW battery range");
        return Packet(65,0,0x55,command,0,(byte)(count + (offset & 255) + (offset >> 8)),
            (byte)count,(byte)offset,(byte)(offset >> 8),0);
    }
    internal static int ActiveSlot(byte[] b)
    {
        Require(b.Length == 56 && b[0] <= 3,"Invalid GLW active configuration");
        bool ordered = b.AsSpan(2,4).ToArray().Order().SequenceEqual(new byte[] {0,1,2,3});
        Require(!ordered || b[1] <= 4,"Invalid GLW configuration count");
        return ordered ? b[2 + b[0]] : b[0];
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        byte[] Read(byte command,int offset,int count,bool drain)
        {
            var b = wire.Exchange(Request(command,offset,count),
                b => b.Length == 64 && b[0] == 0xAA && b[1] == command &&
                    b[4] == count && U16(b,5) == offset,500,drain);
            return b.AsSpan(8,count).ToArray();
        }
        ReferenceBatteryProtocol.Reading value = null;
        for (int round = 0; round < 2; round++)
        {
            var basis = Read(4,0,56,true);
            int slot = ActiveSlot(basis);
            var data = Read(5,64 * slot,56,false).Concat(Read(5,64 * slot + 56,8,false)).ToArray();
            Require(Read(4,0,56,false).AsSpan(0,6).SequenceEqual(basis.AsSpan(0,6)),
                "GLW active configuration changed");
            int level = data[32] & 127;
            Require(level <= 100,"Invalid GLW percentage");
            value = Value(level,(data[32] & 128) != 0);
        }
        return value;
    }
}
