using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static readonly IReadOnlyDictionary<int, string> RawmReceivers = new Dictionary<int, string>
    {
        [0x232A] = "USB Receiver",
        [0x232B] = "USB Receiver 4K",
        [0x2335] = "USB Receiver 8Kn",
        [0x2336] = "RAWM DH8",
        [0x233B] = "RAWM HS Receiver",
        [0x233C] = "RAWM Receiver",
        [0x2346] = "RAWM HS Receiver",
        [0x234B] = "RAWM HS Receiver",
    };

    private static readonly IReadOnlyDictionary<int, string> RawmMice = new Dictionary<int, string>
    {
        [0x2329] = "SA-ML01",
        [0x232C] = "SA-MH01",
        [0x232D] = "SA-SL01",
        [0x232E] = "SA-SH01",
        [0x2331] = "SA-MH01Pro",
        [0x2332] = "SA-SH01Pro",
        [0x2333] = "ES21",
        [0x2334] = "ES21Pro",
        [0x2337] = "ES21M",
        [0x2338] = "ER21Pro",
        [0x2339] = "ER21M",
        [0x233A] = "G11",
        [0x233E] = "SA-SH01S",
        [0x233F] = "ER21",
        [0x2340] = "SA-SL01C",
        [0x2341] = "ES11",
        [0x2343] = "G11Plus",
        [0x2344] = "G11Pro",
        [0x2349] = "ES21Pro(AkamiKarubi Model)",
        [0x234A] = "LEVIATHAN V4",
        [0x2352] = "ES21Pro",
    };

    private static ReferenceBatteryProtocol.Reading MouseExpansionRawm(ushort pid,ReferenceBatteryProtocol.ILink link)
    {
        if (!RawmReceivers.TryGetValue(pid, out string receiver)) throw new IOException("Unknown RAWM receiver");
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
                            if (value.TryGetProperty("vi", out var vi) && (vi.ValueKind != JsonValueKind.Number || !vi.TryGetInt32(out int vendor) || vendor != 0x1915))
                                throw new IOException("RAWM vendor mismatch");
                            bool receiverChannel = channel == 255;
                            bool validName = receiverChannel ? name == receiver : RawmMice.Values.Contains(name);
                            if (!validName) throw new IOException("RAWM device name mismatch");
                            if (value.TryGetProperty("pi", out var pi) &&
                                (pi.ValueKind != JsonValueKind.Number || !pi.TryGetInt32(out int productId) ||
                                 (receiverChannel ? productId != pid : !RawmMice.TryGetValue(productId, out var model) || model != name)))
                                throw new IOException("RAWM device identity mismatch");
                            if(channel==255)return null;
                            identified=name;
                            if(!value.TryGetProperty("battery",out var battery))continue;
                            if(battery.ValueKind!=JsonValueKind.Number||!battery.TryGetInt32(out int level)||level is <0 or >100)
                                throw new IOException("invalid RAWM battery");
                            bool? charging = null;
                            if(value.TryGetProperty("chr",out var chr)&&
                                !(chr.ValueKind is JsonValueKind.True or JsonValueKind.False||
                                    chr.ValueKind==JsonValueKind.Number&&chr.TryGetInt32(out int charge)&&charge is 0 or 1))
                                throw new IOException("invalid RAWM charging field");
                            if (chr.ValueKind != JsonValueKind.Undefined)
                                charging = chr.ValueKind == JsonValueKind.True || chr.ValueKind == JsonValueKind.Number && chr.GetInt32() == 1;
                            return MouseExpansionValue(level,"RAWM reported percent; receiver and mouse JSON identities confirmed",
                                charging:charging,model:"RAWM "+name,identity:"RAWM:"+name);
                        }
                    }
                    if(channel==0&&identified!=null&&(command[0]&15)==11&&command.Length>=3&&command[2]==23)
                    {
                        if(command.Length!=5||command[3]>100||command[4]>1)throw new IOException("invalid RAWM battery notification");
                        return MouseExpansionValue(command[3],"RAWM fresh notification after confirmed mouse identity",
                            charging:command[4] == 1,model:"RAWM "+identified,identity:"RAWM:"+identified);
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
