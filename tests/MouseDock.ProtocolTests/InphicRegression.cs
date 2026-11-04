using MouseDock.Models;
using MouseDock.Services.Detection;

internal static class InphicRegression
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }

        // 身份：网页驱动本地桥认 1D57:FA60 / FA65，桌面驱动认 248A/249A 那一组。
        Check(VendorProtocol.IsInphicDevice(0x1D57,0xFA60),"Web bridge receiver FA60");
        Check(VendorProtocol.IsInphicDevice(0x1D57,0xFA65),"Web bridge receiver FA65");
        Check(!VendorProtocol.IsInphicDevice(0x1D57,0xFA61),"Another 1D57 product is not Inphic");
        Check(VendorProtocol.IsInphicDevice(0x248A,0x5C2F),"Desktop 2.4G receiver");
        Check(VendorProtocol.IsInphicDevice(0x249A,0x5C2F),"Alternate 2.4G receiver id");
        Check(VendorProtocol.IsInphicDevice(0x248A,0x5C2E),"Wired IN9 body");
        Check(!VendorProtocol.IsInphicDevice(0x248A,0x1234),"Unknown PID under the shared OEM vendor id");
        Check(!VendorProtocol.IsInphicDevice(0x248A,0x5C2F,"AJAZZ AJ159 P"),"Do not claim the AJAZZ receiver that shares 248A:5C2F");
        Check(VendorProtocol.IsInphicDevice(0x248A,0x5C2F,"Wireless Receiver"),"Unnamed receiver stays with Inphic");

        // 原始报告：有编号报告多一个 report id 位；无编号报告直接从机型号开始。
        var numbered = new byte[] { 0x08, VendorProtocol.InphicModelIn9Pro, 0x40, 0x01, 0x50 };
        Check(VendorProtocol.TryParseInphicStatus(numbered,out int level,out bool charging) && level==80 && !charging,"Numbered report gives 80 percent");

        var unnumbered = new byte[] { VendorProtocol.InphicModelIn9Pro, 0x40, 0x01, 0x1E };
        Check(VendorProtocol.TryParseInphicStatus(unnumbered,out level,out charging) && level==30 && !charging,"Report without report id still parses");

        var chargingFrame = new byte[] { 0x08, VendorProtocol.InphicModelIn9Pro, 0x41, 0x03, 0x2A };
        Check(VendorProtocol.TryParseInphicStatus(chargingFrame,out level,out charging) && level==42 && charging,"Status 3 is charging and keeps the percentage");

        var fullFrame = new byte[] { 0x08, VendorProtocol.InphicModelIn9Pro, 0x40, 0x02, 0x00 };
        Check(VendorProtocol.TryParseInphicStatus(fullFrame,out level,out charging) && level==100 && !charging,"Status 2 means full regardless of the percentage byte");

        var zero = new byte[] { 0x08, VendorProtocol.InphicModelIn9Pro, 0x40, 0x01, 0x00 };
        Check(VendorProtocol.TryParseInphicStatus(zero,out level,out _) && level==0,"A real 0 percent must survive");

        foreach (byte percent in new byte[]{1,50,99,100})
        {
            var frame = new byte[] { 0x08, VendorProtocol.InphicModelIn9Pro, 0x40, 0x01, percent };
            Check(VendorProtocol.TryParseInphicStatus(frame,out level,out _) && level==percent,"Valid percentage "+percent);
        }
        foreach (byte percent in new byte[]{101,200,255})
        {
            var frame = new byte[] { 0x08, VendorProtocol.InphicModelIn9Pro, 0x40, 0x01, percent };
            Check(!VendorProtocol.TryParseInphicStatus(frame,out _,out _),"Reject percentage above 100: "+percent);
        }

        Check(!VendorProtocol.TryParseInphicStatus(new byte[5],out _,out _),"All-zero report is not a status frame");
        Check(!VendorProtocol.TryParseInphicStatus(new byte[4],out _,out _),"Truncated report is rejected");
        var otherModel = new byte[] { 0x08, 0xFE, 0x40, 0x01, 0x50 };
        Check(!VendorProtocol.TryParseInphicStatus(otherModel,out _,out _),"Unknown model id is not accepted as Inphic");
        var otherTag = new byte[] { 0x08, VendorProtocol.InphicModelIn9Pro, 0x42, 0x01, 0x50 };
        Check(!VendorProtocol.TryParseInphicStatus(otherTag,out _,out _),"Subcommand other than 0x40/0x41 is ignored");
        var badStatus = new byte[] { 0x08, VendorProtocol.InphicModelIn9Pro, 0x40, 0x04, 0x50 };
        Check(!VendorProtocol.TryParseInphicStatus(badStatus,out _,out _),"Unknown status code is not a fresh reading");

        // 长报告里状态帧可能不在最前面，扫描要能找到。
        var padded = new byte[64];
        padded[10]=0x08; padded[11]=VendorProtocol.InphicModelIn9Pro; padded[12]=0x40; padded[13]=0x03; padded[14]=0x11;
        Check(VendorProtocol.TryParseInphicStatus(padded,out level,out charging) && level==17 && charging,"Status frame inside a longer report is found");

        // 主动查询用的唤醒帧：官方 open 时发的那 10 个字节，长度不足的应用报告长度补齐。
        var wake = VendorProtocol.BuildInphicWakeRequest(10);
        Check(wake.Length==10 && wake[0]==0x0C && wake[1]==0x0A && wake[2]==0x01 && wake[3]==0xFE
              && wake[4]==0x01 && wake[5]==0xFE && wake[6]==0,"Official open-time feature frame");
        Check(VendorProtocol.BuildInphicWakeRequest(34).Length==34,"Pad the wake frame to the declared feature report length");
        Check(VendorProtocol.BuildInphicWakeRequest(34).Skip(10).All(b=>b==0),"Wake frame padding stays zero");
        Check(VendorProtocol.BuildInphicWakeRequest(4).Length==10,"Never truncate the wake frame below its own size");

        // 报告中的 FA60 在 MI_02 上拆成独立 Feature 和 Input 集合。
        Check(VendorProtocol.IsInphicSplitReceiver(0x1D57, 0xFA60), "FA60 uses split HID collections");
        Check(VendorProtocol.IsInphicSplitFeature(0x0B, 0, 262), "FA60 feature collection");
        Check(VendorProtocol.IsInphicSplitInput(0x0A, 0, 5), "FA60 input collection");
        Check(!VendorProtocol.IsInphicSplitInput(1, 2, 8), "Mouse movement is not a battery input report");
        Check(VendorProtocol.BuildInphicWakeRequest(262).Skip(10).All(b => b == 0), "Official wake padding for FA60");
        const string inputPath = @"\?\hid#vid_1d57&pid_fa60&mi_02&col03#8&1381d3a6&0&0002#{guid}";
        const string featurePath = @"\?\hid#vid_1d57&pid_fa60&mi_02&col04#8&1381d3a6&0&0003#{guid}";
        Check(VendorProtocol.InphicCollectionParent(inputPath) == VendorProtocol.InphicCollectionParent(featurePath),
            "Split collections from the same receiver pair together");
        Check(VendorProtocol.InphicCollectionParent(inputPath) !=
            VendorProtocol.InphicCollectionParent(featurePath.Replace("1381d3a6", "1381d3a7")),
            "Different receiver instances do not pair");
        var receiver = new HidDiscovery.Bucket
        {
            Vid = 0x1D57, Pid = 0xFA60, Product = "Inphic 2.4G Receiver", Kind = DeviceKind.Mouse
        };
        receiver.Counts[DeviceKind.Keyboard] = 2;
        receiver.Counts[DeviceKind.Mouse] = 1;
        var detected = receiver.ToDevice();
        Check(detected.Kind == DeviceKind.Mouse && detected.Transport == "2.4G",
            "FA60 mouse collection wins over the two keyboard collections from the report");

        // 兜底协议（SC525）保持原样，别被新路径带坏。
        var sc525 = new byte[32];
        sc525[0]=0x0E; sc525[1]=0x20; sc525[8]=0x80|66; sc525[9]=0x80;
        Check(VendorProtocol.TryParseInphicSc525(sc525,out level,out charging,out bool sleeping) && level==66 && charging && !sleeping,"SC525 fallback still parses");
        sc525[8]=66; sc525[9]=0;
        Check(VendorProtocol.TryParseInphicSc525(sc525,out level,out charging,out sleeping) && !charging && sleeping,"SC525 sleep flag direction is preserved");

        return checks;
    }
}
