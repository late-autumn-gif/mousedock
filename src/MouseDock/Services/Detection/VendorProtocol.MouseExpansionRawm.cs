using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionRawm(ushort pid,ReferenceBatteryProtocol.ILink link)
    {
        var mice=new Dictionary<int,string>{{9001,"SA-ML01"},{9004,"SA-MH01"},{9005,"SA-SL01"},{9006,"SA-SH01"},
            {9009,"SA-MH01Pro"},{9010,"SA-SH01Pro"},{9012,"ES21Pro"},{9015,"ES21M"},{9016,"ER21Pro"},{9017,"ER21M"}};
        string receiver=pid switch {0x232A=>"USB Receiver",0x232B=>"USB Receiver 4K",0x2335=>"USB Receiver 8Kn",_=>null};
        ReferenceBatteryProtocol.Reading Channel(int channel)
        {
            bool drained=false;
            for(int n=0;n<32;n++)if(link.Read(1) is not {Length:>0}){drained=true;break;}
            if(!drained)throw new IOException("RAWM queue did not drain");
            int offset=channel==0?2:1;
            var request=MouseExpansionPacket(65,0);
            if(channel==0)request[1]=0xC0;
            new byte[]{0x8D,1,13,3,0,0}.CopyTo(request,offset);
            ulong stamp=(ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            for(int n=0;n<8;n++)request[offset+6+n]=(byte)(stamp>>(8*n));
            if(!link.Write(request,300))throw new IOException("RAWM query write failed");
            var stream=new List<byte>();
            string identified=null;
            long deadline=link.Now+2000;
            for(int n=0;n<96&&link.Now<deadline;n++)
            {
                var raw=link.Read(50);
                if(raw==null)continue;
                if(raw.Length!=64)throw new IOException("invalid RAWM input length");
                int start=0,source=255;
                if((raw[0]&0xC0)==0xC0){source=raw[0]&63;start=1;}
                if(source!=channel)continue;
                int kind=raw[start]&0xC0,size=raw[start]&63;
                if(kind==0x40)continue;
                if(kind is not (0 or 0x80)||size>63-start)throw new IOException("invalid RAWM packet kind or size");
                stream.AddRange(raw.AsSpan(start+1,size).ToArray());
                if(stream.Count>8192)throw new IOException("RAWM stream exceeded bound");
                while(stream.Count>=4)
                {
                    if(stream.Take(4).Any(v=>v!=255))throw new IOException("invalid RAWM command prefix");
                    if(stream.Count<6)break;
                    int length=((stream[4]&0xF0)<<4)|stream[5];
                    if(length<2)throw new IOException("invalid RAWM command length");
                    if(stream.Count<length+4)break;
                    var command=stream.GetRange(4,length).ToArray();
                    stream.RemoveRange(0,length+4);
                    if((command[0]&15)==2)
                    {
                        int jsonLength=command.Length-2;
                        if(command[^1]==0)jsonLength--;
                        JsonDocument json;
                        try { json=JsonDocument.Parse(command.AsMemory(2,jsonLength),new JsonDocumentOptions{MaxDepth=16}); }
                        catch(JsonException){throw new IOException("invalid RAWM device JSON");}
                        using(json)
                        {
                            var value=json.RootElement;
                            if(value.ValueKind!=JsonValueKind.Object)throw new IOException("RAWM JSON is not an object");
                            var fields=new HashSet<string>(StringComparer.Ordinal);
                            foreach(var field in value.EnumerateObject())if(!fields.Add(field.Name))throw new IOException("duplicate RAWM JSON field");
                            if(!value.TryGetProperty("dn",out var dn)||dn.ValueKind!=JsonValueKind.String)
                                throw new IOException("missing RAWM device identity");
                            string name=dn.GetString();
                            int identity=channel==255?pid:mice.FirstOrDefault(p=>p.Value==name).Key;
                            if(identity==0||channel==255&&name!=receiver||
                                value.TryGetProperty("vi",out var vi)&&(vi.ValueKind!=JsonValueKind.Number||!vi.TryGetInt32(out int v)||v!=0x1915)||
                                value.TryGetProperty("pi",out var pi)&&(pi.ValueKind!=JsonValueKind.Number||!pi.TryGetInt32(out int p)||p!=identity))
                                throw new IOException("RAWM device identity mismatch");
                            if(channel==255)return null;
                            identified=name;
                            if(!value.TryGetProperty("battery",out var battery))continue;
                            if(battery.ValueKind!=JsonValueKind.Number||!battery.TryGetInt32(out int level)||level is <0 or >100)
                                throw new IOException("invalid RAWM battery");
                            if(value.TryGetProperty("chr",out var chr)&&
                                !(chr.ValueKind is JsonValueKind.True or JsonValueKind.False||
                                    chr.ValueKind==JsonValueKind.Number&&chr.TryGetInt32(out int charge)&&charge is 0 or 1))
                                throw new IOException("invalid RAWM charging field");
                            return MouseExpansionValue(level,"RAWM reported percent; receiver and mouse JSON identities confirmed",
                                model:"RAWM "+name,identity:"RAWM:"+name);
                        }
                    }
                    if(channel==0&&identified!=null&&(command[0]&15)==11&&command.Length>=3&&command[2]==23)
                    {
                        if(command.Length!=5||command[3]>100||command[4]>1)throw new IOException("invalid RAWM battery notification");
                        return MouseExpansionValue(command[3],"RAWM fresh notification after confirmed mouse identity",
                            model:"RAWM "+identified,identity:"RAWM:"+identified);
                    }
                }
            }
            throw new IOException(stream.Count>0?"incomplete RAWM stream":"no identity-confirmed RAWM battery");
        }
        Channel(255);
        var first=Channel(0);
        var second=Channel(0);
        if(first.Identity!=second.Identity)throw new IOException("RAWM paired mouse changed");
        return second;
    }
}
