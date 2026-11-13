using System;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading MouseExpansionDecode(string family, byte[] raw)
    {
        if (raw == null) return null;
        ReadOnlySpan<byte> b = raw;
        int level;
        switch (family)
        {
            case "hechi":
            case "ausdom":
                if (b.Length != 64 || !MouseExpansionPrefix(b,11,19) || b[3] != 0 ||
                    family == "ausdom" && b[4] > 7 || b.SequenceEqual(MouseExpansionPacket(64,11,19))) return null;
                return MouseExpansionValue(b[18], family + " device-reported percentage");
            case "sc525":
                if (b.Length != 32 || !MouseExpansionPrefix(b,14,32)) return null;
                return (b[9] & 128) == 0 ? MouseExpansionSleep("SC525 offline") :
                    MouseExpansionValue(b[8] & 127,"SC525 device-reported percentage");
            case "scyrox":
                if (b.Length != 64 || b[0] == 0 || b[1] != 46 || b[62] == 1 ||
                    b[5] > 1 || b[6] > 1) return null;
                return b[6] == 0 ? MouseExpansionSleep("Scyrox mouse offline") :
                    MouseExpansionValue(b[4],"Scyrox device-reported percentage");
            case "zynoo":
                if (b.Length == 17 && b[0] == 0) b = b[1..];
                if (b.Length != 16 || !MouseExpansionPrefix(b,0x9A,0,0) ||
                    b.SequenceEqual(MouseExpansionPacket(16,0x9A,0,0,8))) return null;
                return b[11] == 0 ? MouseExpansionSleep("ZYNOO mouse offline") :
                    MouseExpansionValue(b[10] & 127,"ZYNOO device-reported percentage");
            case "compx":
            case "hts":
            case "nk":
            case "hitscan":
            case "vgn-master":
                if (b.Length != 17 || !MouseExpansionPrefix(b,8,4) || b[2] == 255 || b[7] > 1 ||
                    MouseExpansionSum(b) != 85 || b.SequenceEqual(MouseExpansionCompx(4)) ||
                    family is "hts" or "hitscan" or "nk" && b[2] != 0 || family == "hitscan" && b[5] != 2) return null;
                return MouseExpansionValue(b[6],family + " device-reported percentage");
            case "nordic54":
                if (b.Length != 64 || !MouseExpansionPrefix(b,8,0x72) || b[2] > 1 || b[5] != 7 || b[6] != 1) return null;
                return b[2] == 1 ? MouseExpansionSleep("Nordic54 mouse offline") :
                    MouseExpansionValue(b[7],"Nordic54 device-reported percentage");
            case "steel":
                if (b.Length != 64 || b[0] != 0xD2 || (b[1] & 127) > 21) return null;
                level = b[1] & 127;
                return level == 0 ? MouseExpansionSleep("Aerox asleep") :
                    MouseExpansionValue((level - 1) * 5,"Aerox coarse 5% battery steps");
            case "ipi":
                return b.Length == 64 && b[0] == 3 && b[4] == 2 ?
                    MouseExpansionValue(b[6],"IPI device-reported percentage") : null;
            case "feature83":
                if (b.Length == 64 && b[0] == 0xA1)
                {
                    var padded = new byte[65]; b.CopyTo(padded.AsSpan(1)); b = padded;
                }
                if (b.Length != 65 || b[0] != 0 || b[1] != 0xA1 || b[4] != 2 ||
                    b[6] != 0x83 || b[7] > 1) return null;
                return MouseExpansionValue(b[8],"Feature83 device-reported percentage");
            case "akko":
            case "cube":
            case "delux":
            case "aula-v9":
                if (b.Length != 64 || b[0] != 4 || b[3] != 0x1A) return null;
                if (family == "akko" && b[9] > 1 ||
                    family == "cube" && b[4] != 6 ||
                    family == "delux" && (b[4] is not (0 or 6) || b[5] != 0 || b[6] != 0 ||
                        b[7] is 0xFE or 0xFF || b[9] != 0 || b[8..14].ToArray().All(x => x == 0))) return null;
                return MouseExpansionValue(b[8],family + " device-reported percentage");
            case "keychron":
                return b.Length == 64 && MouseExpansionPrefix(b,0xB4,6) ?
                    MouseExpansionValue(b[20],"Keychron device-reported percentage") : null;
            case "nzxt":
                return b.Length == 64 && MouseExpansionPrefix(b,0x4E,2,0x97) ?
                    MouseExpansionValue(b[17] | b[18] << 8,"NZXT device-reported percentage") : null;
            case "hecate":
                if (b.Length != 64 || b[0] != 4 || b[3] != 0x1A || b[4] != 6 ||
                    b[5] != 0 || b[6] != 0 || b[7] is 0xFE or 0xFF || b[9] > 2) return null;
                return MouseExpansionValue(b[8],"HECATE device-reported percentage");
            case "rog-standard":
                if (b.Length != 64 || !MouseExpansionPrefix(b,0x12,7) || b[4] > 100) return null;
                return b[4] == 0 && b[9] == 0 ? MouseExpansionSleep("ROG mouse offline") :
                    MouseExpansionValue(b[4],"ROG device-reported percentage");
            case "rog8k":
                if (b.Length is < 21 or > 65 || !MouseExpansionPrefix(b,3,0x12,7,0,0) || b[10] > 1) return null;
                return b[5] == 0 ? MouseExpansionSleep("ROG 8K mouse offline") :
                    MouseExpansionValue(b[5],"ROG 8K device-reported percentage");
            case "x7":
                if (b.Length != 64 || !MouseExpansionPrefix(b,0x55,0x12) || b[2] is < 5 or > 60 ||
                    b[4] > 1 || (b[7] & 15) != 1 || b[8] != 1) return null;
                return MouseExpansionValue(b[5],"X7 device-reported percentage");
            case "mchose4d":
                if (b.Length != 64 || !MouseExpansionPrefix(b,0x4D,1,1) || b[3] is <14 or >55 ||
                    b[4] != 0 || b[5] != 9 || b[6]!=0 || b[7]!=0 ||
                    (b[8] | b[9] << 8) != 0x3837 || b[19] > 1 ||
                    !MouseExpansionPid((ushort)(b[10] | b[11] << 8),0x4026,0x4028,0x4033,0x4034,0x4037,0x4038,0x4040)) return null;
                byte xor=0;
                foreach(byte value in b.Slice(2,6+b[3]))xor^=value;
                if(xor!=b[8+b[3]])return null;
                return b[20] == 0 ? MouseExpansionSleep("MHUB mouse offline") :
                    MouseExpansionValue(b[20],"MHUB4D device-reported percentage", identity: $"{b[10] | b[11] << 8:X4}");
            case "incott":
                if (b.Length == 33)
                {
                    if (b[8..].ToArray().Any(x => x != 0)) return null;
                    b = b[..8];
                }
                return b.Length == 8 && b[0] == 9 ?
                    MouseExpansionValue(b[1] & 127,"PixArt fresh input percentage") : null;
            case "rapoo-short":
                if (b.Length != 13 || b[0] != 7 || (b[1] & 15) > 2 || b[7] > 2) return null;
                return b[7] == 0 ? MouseExpansionSleep("Rapoo mouse offline") :
                    MouseExpansionValue(b[8],"Rapoo fresh input percentage");
            case "rapoo":
                return ReferenceBatteryProtocol.TryDecode(ReferenceProfiles.Protocol.Rapoo,b,out var rapoo)
                    ? rapoo with { Charging = false, ChargingKnown = false, Status = rapoo.Level >= 0 ?
                        ReferenceBatteryProtocol.State.Online : rapoo.Status, Diagnostic = "Rapoo fresh input; charging unknown" } : null;
            case "delux-pro":
                if (b.Length is < 5 or > 64 || b[0] != 3 || b[2] != 0x40 || b[3] > 1 ||
                    b[4] == 0 || b[5..].ToArray().Any(x => x != 0)) return null;
                return MouseExpansionValue(b[4],"Delux fresh input percentage");
            case "x23-pro":
                if (b.Length != 32 || b[0] != 4 || b[1] > 1 || b[5] > 2) return null;
                return b[1] == 0 ? MouseExpansionSleep("X23 Pro offline") :
                    MouseExpansionValue(b[4],"X23 Pro device-reported percentage; full flag does not synthesize percentage");
            default: return null;
        }
    }
    private static byte[] MouseExpansionCompx(byte command, byte hold = 0)
    {
        var packet = MouseExpansionPacket(17,8,command);
        if (command == 3) packet[6] = hold;
        packet[16] = unchecked((byte)(85 - MouseExpansionSum(packet.AsSpan(0,16))));
        return packet;
    }
}
