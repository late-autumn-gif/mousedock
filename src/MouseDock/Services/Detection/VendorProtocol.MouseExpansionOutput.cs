using System;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionReadOutput(string family, ushort vid,
        ushort pid, MouseExpansionLink l)
    {
        byte[] Query(byte[] request, Func<byte[],bool> valid, int budget = 1200) => l.Exchange(request,valid,budget);
        ReferenceBatteryProtocol.Reading Decoded(byte[] request, string decodeFamily = null, int budget = 1200)
        {
            var raw = Query(request,b => MouseExpansionDecode(decodeFamily ?? family,b) != null,budget);
            return raw == null ? null : MouseExpansionDecode(decodeFamily ?? family,raw);
        }
        bool Online0B() => Query(MouseExpansionPacket(64,11,20),
            b => b.Length == 64 && MouseExpansionPrefix(b,11,20) && b[3] == 0 && b[4] != 0,
            family == "hechi" ? 3000 : 400) != null;
        if (family is "hechi" or "ausdom")
        {
            if (!Online0B()) return MouseExpansionSleep(family + " mouse not online");
            var value = Decoded(MouseExpansionPacket(64,11,19),budget: family == "hechi" ? 3000 : 400);
            return value != null && Online0B() ? value : ReferenceBatteryProtocol.Reading.Failed(family + " live status changed");
        }
        if (family == "hecate")
        {
            bool Online() => Query(MouseExpansionReport4(0xAA), b =>
                b.Length == 64 && b[0] == 4 && b[3] == 0xAA && b[7] is not (0xFE or 0xFF) && b[8] == 255,1000) != null;
            ReferenceBatteryProtocol.Reading value = null;
            for (int i = 0; i < 2; i++)
            {
                if (!Online()) return MouseExpansionSleep("HECATE mouse not online");
                value = Decoded(MouseExpansionReport4(0x1A),budget:1000);
                if (value == null) return ReferenceBatteryProtocol.Reading.Failed("HECATE battery reply missing");
            }
            return Online() ? value : MouseExpansionSleep("HECATE mouse went offline");
        }
        if (family == "vgn-se") return MouseExpansionReadVgnSe(l);
        if (family == "vgn-master")
        {
            bool Online() => Query(MouseExpansionCompx(3),
                b => b.Length == 17 && MouseExpansionPrefix(b,8,3,0) && b[6] == 1) != null;
            if (!Online()) return MouseExpansionSleep("VGN master mouse offline");
            ReferenceBatteryProtocol.Reading value = null;
            for (int i=0;i<2;i++) value = Decoded(MouseExpansionCompx(4),"compx");
            return value != null && Online() ? value : ReferenceBatteryProtocol.Reading.Failed("VGN master live read failed");
        }
        if (family == "vgn-ld")
        {
            ReferenceBatteryProtocol.Reading value = null;
            for (int i=0;i<2;i++)
            {
                var raw = Query(MouseExpansionPacket(l.Output,0xA0,2,0x25),
                    b => b.Length >= 4 && l.Inputs.Contains(b[0]) && b[1] == 2 && b[2] == 0x25 && b[3] <= 100,600);
                if (raw == null) return ReferenceBatteryProtocol.Reading.Failed("VGN LD fresh response missing");
                value = MouseExpansionValue(raw[3],"VGN LD device-reported percentage");
            }
            return value;
        }
        if (family == "waizowl") return MouseExpansionReadWaizowl(l);
        byte[] request = family switch
        {
            "sc525" => MouseExpansionPacket(16,14,32),
            "scyrox" => MouseExpansionScyroxRequest(l.OutputId),
            "zynoo" => MouseExpansionPacket(17,0,0x9A,0,0,8),
            "nordic54" => MouseExpansionPacket(64,8,0x7D,0x72,2,0,(byte)(pid==0x1154?0:1),7,1),
            "compx" or "hitscan" or "hts" or "nk" => MouseExpansionCompx(4),
            "steel" => MouseExpansionPacket(65,0,0xD2),
            "akko" => MouseExpansionPacket(64,4,0,0,0x1A,6),
            "cube" => MouseExpansionCubeRequest(),
            "aula-v9" or "delux" => MouseExpansionReport4(0x1A),
            "rog-standard" => MouseExpansionPacket(65,0,0x12,7),
            "rog8k" => MouseExpansionPacket(l.Output,3,0x12,7,0,0),
            "nzxt" => MouseExpansionPacket(64,0x4E,2,0x81,0,0xB0),
            "x7" => MouseExpansionPacket(65,0,0xAA,0x12,0,0),
            "mchose4d" => MouseExpansionPacket(64,0x4D,1,1,0,0,9,0,0,8),
            _ => null
        };
        if (family == "keychron")
        {
            ReferenceBatteryProtocol.Reading value = null;
            for (int i=0;i<2;i++)
            {
                if (!l.Drain() || !l.SetFeature(MouseExpansionPacket(64,0xB3,6))) return ReferenceBatteryProtocol.Reading.Failed("Keychron query failed");
                long deadline=l.Now+700;
                value=null;
                for(int n=0;n<16 && l.Now<deadline;n++)
                {
                    value=MouseExpansionDecode("keychron",l.Read(50));
                    if(value!=null) break;
                }
                if(value==null) return ReferenceBatteryProtocol.Reading.Failed("Keychron fresh response missing");
            }
            return value;
        }
        if (request == null) return ReferenceBatteryProtocol.Reading.Failed("Mouse battery output family unavailable");
        ReferenceBatteryProtocol.Reading result = null;
        for (int count = 0; count < 2; count++)
        {
            result = Decoded(request);
            if (result == null) return ReferenceBatteryProtocol.Reading.Failed(family + " fresh battery response missing");
            if (result.Level < 0) return result;
        }
        return result;
    }

    private static byte[] MouseExpansionScyroxRequest(byte reportId)
    {
        var packet=MouseExpansionPacket(64,reportId,0x2E,0,3);
        packet[63]=(byte)MouseExpansionSum(packet.AsSpan(0,63)); return packet;
    }
    private static byte[] MouseExpansionReport4(byte command)
    {
        var packet=MouseExpansionPacket(64,4,0,0,command,(byte)(command==0x1A?6:0));
        int sum=packet.Skip(3).Sum(x=>(int)x);
        packet[1]=(byte)sum; packet[2]=(byte)(sum>>8); return packet;
    }
    private static byte[] MouseExpansionCubeRequest()
    {
        var packet=MouseExpansionPacket(64,4,0,0,0x1A,6);
        int crc=0xFFFF;
        foreach(byte value in packet.AsSpan(3,29))
        {
            crc^=value;
            for(int i=0;i<8;i++) crc=(crc&1)!=0?(crc>>1)^0xA001:crc>>1;
        }
        packet[1]=(byte)crc;packet[2]=(byte)(crc>>8);return packet;
    }
    private static ReferenceBatteryProtocol.Reading MouseExpansionReadPassive(string family, ushort pid, MouseExpansionLink l)
    {
        if(!l.Drain()) return ReferenceBatteryProtocol.Reading.Failed("Mouse passive input queue did not drain");
        int count=0;long deadline=l.Now+8000;
        for(int attempt=0;attempt<128 && l.Now<deadline;attempt++)
        {
            var raw=l.Read(100);
            if(raw==null) continue;
            if(family=="delux-pro" && !MouseExpansionPid(raw.Length>1?(ushort)raw[1]:(ushort)0,
                pid==0xFA65?new ushort[]{0x21,0x30,0x32,0x34,0x37,0x38,0x39,0x3A}:new ushort[]{0x10,0x91,0x29,0x2A,0x2B})) continue;
            var value=MouseExpansionDecode(family=="sc900"?"incott":family,raw);
            if(value?.Level>=0) { if(++count>=2) return value; }
            else count=0;
        }
        return ReferenceBatteryProtocol.Reading.Failed("Mouse passive protocol did not produce two fresh reports");
    }
    private static ReferenceBatteryProtocol.Reading MouseExpansionReadVgnSe(MouseExpansionLink l)
    {
        bool Online()=>l.Exchange(MouseExpansionPacket(65,0,0x55,0xED),
            b=>b.Length==64&&b[1]==0xED&&b[8] is not (0 or 1 or 255))!=null;
        if(!Online()) return MouseExpansionSleep("VGN SE offline");
        ReferenceBatteryProtocol.Reading value=null;
        for(int i=0;i<2;i++)
        {
            var raw=l.Exchange(MouseExpansionPacket(65,0,0x55,0x30),
                b=>b.Length==64&&b[1]==0x30&&b[8]<=100&&b[9]==0);
            if(raw==null) return ReferenceBatteryProtocol.Reading.Failed("VGN SE charging percentage unavailable");
            value=MouseExpansionValue(raw[8],"VGN SE device-reported percentage");
        }
        return Online()?value:MouseExpansionSleep("VGN SE went offline");
    }
    private static ReferenceBatteryProtocol.Reading MouseExpansionReadWaizowl(MouseExpansionLink l)
    {
        byte[] Command(byte command)
        {
            var packet=MouseExpansionPacket(65,0,command,0,0x81,1);
            packet[64]=unchecked((byte)(0xA1-MouseExpansionSum(packet.AsSpan(1,63))));return packet;
        }
        var status=l.Exchange(Command(1),b=>b.Length==64&&b[0]==1&&b[3]==1&&(b[4]&8)!=0);
        if(status==null)return MouseExpansionSleep("WAIZOWL receiver offline");
        var identities=new[]{"F2070000","F4070000","F6070000","F3070000","F5070000","B0070000"};
        string identity=Convert.ToHexString(status.AsSpan(11,4));
        if(!identities.Contains(identity))return ReferenceBatteryProtocol.Reading.Failed("WAIZOWL paired model not allowed");
        l.Delay(100);
        var raw=l.Exchange(Command(0x41),b=>b.Length==64&&b[0]==0x41&&b[3] is 1 or 3&&b[6]<=100);
        return raw==null?ReferenceBatteryProtocol.Reading.Failed("WAIZOWL battery response missing"):
            MouseExpansionValue(raw[6],"WAIZOWL device-reported percentage",identity:identity);
    }
}
