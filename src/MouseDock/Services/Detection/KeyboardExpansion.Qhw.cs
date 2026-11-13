using System;
using System.Threading;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionQhw
{
    private static int sequence;
    internal static byte[] Request(int command, byte seq, int size)
    {
        Require(command is 0x0900 or 0x0904 && size is 32 or 64,"Invalid QHW query");
        var b = Packet(size,77,1,3,0,(byte)command,(byte)(command >> 8),0,seq);
        for (int i = 2; i < 8; i++) b[8] ^= b[i];
        return b;
    }
    internal static ReferenceBatteryProtocol.Reading Decode(byte[] b,int command,byte seq,int size)
    {
        Require(b.Length == size && b[0] == 77 && b[1] == 1 && b[2] == 3 &&
            b[3] is > 0 and <= 23 && U16(b,4) == command && b[6] == 0 && b[7] == seq,
            "Invalid QHW reply header");
        int count = b[3];
        byte xor = 0;
        for (int i = 2; i < 8 + count; i++) xor ^= b[i];
        Require(b[8 + count] == xor,"QHW reply XOR mismatch");
        int charge,level;
        if (command == 0x0900)
        {
            Require(count >= 16 && U16(b,8) == 0x3837 && U16(b,10) is 0x1015 or 0x2025 &&
                b[18] == 2,"Unexpected QHW board or offline connection");
            charge = b[19]; level = b[20];
        }
        else
        {
            Require(command == 0x0904 && count == 2,"Invalid QHW battery payload");
            charge = b[8]; level = b[9];
        }
        Require(charge <= 2 && level <= 100,"Invalid QHW power fields");
        return Value(level,charge == 1);
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        byte[] Read(int command)
        {
            byte seq = unchecked((byte)Interlocked.Increment(ref sequence));
            var b = wire.Exchange(Request(command,seq,wire.Output),
                b => b.Length == wire.Input && b[0] == 77 && b[4] == (byte)command &&
                    b[5] == (byte)(command >> 8) && b[7] == seq);
            Decode(b,command,seq,wire.Input);
            return b;
        }
        var first = Read(0x0900);
        Read(0x0904);
        var second = Read(0x0904);
        var final = Read(0x0900);
        Require(first.AsSpan(8,4).SequenceEqual(final.AsSpan(8,4)),"QHW board identity changed");
        return Decode(second,0x0904,second[7],wire.Input);
    }
}
