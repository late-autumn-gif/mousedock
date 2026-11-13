using System.Text.Json;
using System.Text;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
internal static class SupportAuditRegression
{
    internal static int Run()
    {
        int count=0;void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
        foreach(string name in new[]{"8K Wireless Receiver","8K Receiver","ATK 8K Wireless Receiver"})
        foreach(bool third in new[]{false,true})
        {
            var b=new HidDiscovery.Bucket {Vid=0x1234,Pid=0x5678,Product=name,InstanceId="test-instance",Kind=DeviceKind.Mouse};
            b.Counts[DeviceKind.Mouse]=1;b.Counts[DeviceKind.Keyboard]=3;if(third)b.Counts[DeviceKind.Controller]=1;
            var d=b.ToDevice();
            Check(d.Kind==DeviceKind.Other&&d.Name==name&&d.ConfirmedModel==""&&d.ReceiverDiscovered&&!d.PeripheralConfirmed,"Unknown 8K composite receiver cannot be classified by auxiliary count");
        }
        var keyboard=new HidDiscovery.Bucket {Vid=0x320F,Pid=0x5088,Product="ATK V75X 8K Wireless Receiver",Kind=DeviceKind.Mouse};
        keyboard.Counts[DeviceKind.Mouse]=1;keyboard.Counts[DeviceKind.Keyboard]=3;keyboard.Counts[DeviceKind.Controller]=1;
        Check(keyboard.ToDevice().Kind==DeviceKind.Keyboard,"Known keyboard evidence beats marketing/collection count");
        var zero=new HidDiscovery.Bucket {Vid=0x373B,Pid=0x1155,Product="8K Wireless Receiver",Kind=DeviceKind.Mouse,HandledBy=HandledBy.ZeroRainy,ZeroRainyModel="ATK ZERO"};
        zero.Counts[DeviceKind.Mouse]=1;zero.Counts[DeviceKind.Keyboard]=3;zero.Counts[DeviceKind.Controller]=1;
        Check(zero.ToDevice().Kind==DeviceKind.Mouse&&zero.ToDevice().ConfirmedModel=="ATK ZERO","Dedicated identity route retains confirmed mouse");
        Check(HidDiscovery.IsKnownHubSwitch(0xC019,0x0401)&&!HidDiscovery.IsKnownHubSwitch(0xC019,0x0402),"Exact reported hub switch excluded; adjacent PID preserved");
        foreach(string name in new[]{"扬声器 (HyperX Cloud III Wireless)","Speakers (HyperX Cloud III S Wireless)","HyperX Cloud III","Speakers (2- HyperX Cloud III S Wireless)"})
        {
            var e=new AudioEndpointInfo("test",name,false,Guid.Empty,1,50,false);
            Check(e.IsHeadset&&e.Name==name,"Cloud III speaker-form endpoint classified without renaming model");
            Check(!(e with{Capture=true}).IsHeadset,"Cloud microphone never becomes playback");
        }
        Check(!new AudioEndpointInfo("x","Speakers (HyperX Cloud III Keyboard)",false,Guid.Empty,1,50,false).IsHeadset,"Non-model suffix rejected");
        foreach(ushort pid in new ushort[]{0x06BE,0x02CC,0x05B7,0x0C9D,0x0FCC})
            Check(ExtraDeviceRoutes.Find(0x03F0,pid,"HyperX Cloud III Wireless")?.Kind == DeviceKind.Headset,
                "HyperX finite profiles enter the descriptor-validated headset reader");
        Check(!DeviceRoutes.IsKnown(0x03F0,0x0ACE,"HyperX Cloud III Wireless"),
            "Unlisted HyperX identity remains outside HID battery admission");
        Check(VendorProtocol.TryReadCloud3S(0x03F0,0x06BE,out bool chg)==-1&&!chg,"Direct legacy HyperX entry exits at policy gate without native I/O");
        var research=VendorProtocol.BuildCloud3SRequest(62,2);research[6]=2;
        Check(!VendorProtocol.TryParseCloud3SDeviceInfo(research,research),"A guessed device-info byte cannot prove RF online");
        return count;
    }
    internal static void Export(string input,string output)
    {
        using var doc=JsonDocument.Parse(System.IO.File.ReadAllText(input));
        var csv=new StringBuilder("source,index,name,vid,pid,declared_protocol,upstream_verified,runtime_candidate_only,descriptor_and_identity_required,status\r\n");
        string Q(string s)=>"\""+s.Replace("\"","\"\"")+"\"";
        foreach(var r in doc.RootElement.EnumerateArray())
        {
            ushort vid=(ushort)r.GetProperty("vid").GetInt32(),pid=(ushort)r.GetProperty("pid").GetInt32();string name=r.GetProperty("name").GetString()??"";
            bool eligible=vid!=0&&pid!=0&&DeviceRoutes.IsKnown(vid,pid,name);
            string status=vid==0||pid==0?"无直接VID/PID，不能由此行验证":eligible?"仅允许进入候选；仍需真实集合/身份/响应":"此目录名称/编号未开放主动电量；不等于永久不支持";
            if((vid is 0x03F0 or 0x0951 || VendorProtocol.IsMchoseV9Pro(vid,pid,name)) &&
                ExtraDeviceRoutes.Find(vid,pid,name)?.Kind != DeviceKind.Headset)
                status="耳机仅系统音频，未启用HID电量";
            csv.AppendLine(string.Join(',',Q(r.GetProperty("source").GetString()??""),r.GetProperty("index").GetInt32(),Q(name),$"{vid:X4}",$"{pid:X4}",Q(r.GetProperty("declared_protocol").ToString()),r.GetProperty("upstream_declared_verified").GetBoolean(),eligible,true,Q(status)));
        }
        System.IO.File.WriteAllText(output,csv.ToString(),new UTF8Encoding(true));
        Console.WriteLine($"Audited {doc.RootElement.GetArrayLength()} catalogue source/PID cases; no hardware I/O; not a hardware support certificate.");
    }
}


