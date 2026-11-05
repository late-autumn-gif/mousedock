using MouseDock.Models;
using MouseDock.Services.Detection;

internal static class OfficialIntegrationRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        foreach (string product in new[] { "ATK ZERO", "ATK ZERO 2.4G Receiver" })
        {
            var receiver = new HidDiscovery.Bucket { Vid=0x373B, Pid=0x1155, Product=product, Kind=DeviceKind.Mouse };
            receiver.Counts[DeviceKind.Keyboard]=2; receiver.Counts[DeviceKind.Mouse]=1;
            Check(receiver.ToDevice().Kind == DeviceKind.Mouse, "Identified ZERO remains a mouse after renaming");
        }
        var generic = new HidDiscovery.Bucket { Vid=0x320F, Pid=0x5088, Product="Wireless Gaming Keyboard", Kind=DeviceKind.Keyboard };
        Check(!generic.ToDevice().Name.Contains("Rainy"), "A shared PID cannot establish Rainy identity without a compatible reply");
        var rainy = new HidDiscovery.Bucket { Vid=0x320F, Pid=0x5088, Product="Wireless Gaming Keyboard", Kind=DeviceKind.Keyboard, RainyIdentified=true };
        Check(rainy.ToDevice().Name.Contains("Rainy") && rainy.ToDevice().Transport=="2.4G", "A compatible Rainy reply names the shared 2.4G keyboard");
        foreach (var (vid,pid,product,transport) in new[]{
            ((ushort)0x320F,(ushort)0x5055,"ATK V75X","有线"),
            ((ushort)0x320F,(ushort)0x5088,"ATK V75X 2.4G Dongle","2.4G")})
        {
            var v75 = new HidDiscovery.Bucket { Vid=vid, Pid=pid, Product=product, Kind=DeviceKind.Keyboard };
            Check(v75.ToDevice().Name=="ATK V75X" && v75.ToDevice().Transport==transport, "V75X wired/wireless identity");
        }
        var vgn5055 = new HidDiscovery.Bucket { Vid=0x320F, Pid=0x5055, Product="VGN V98 Pro", Kind=DeviceKind.Keyboard };
        Check(!vgn5055.ToDevice().Name.Contains("Rainy") && !vgn5055.ToDevice().Name.Contains("ATK V75X"), "Shared 0x5055 keeps other brands");
        foreach (string identity in new[] { "M909", "M910", "IN90", "I149" })
        {
            var receiver = new HidDiscovery.Bucket { Vid=0x248A, Pid=0x5C2F, Product="Wireless Receiver", Identity=identity, Kind=DeviceKind.Mouse };
            receiver.Counts[DeviceKind.Keyboard]=2; receiver.Counts[DeviceKind.Mouse]=1;
            var device = receiver.ToDevice();
            Check(device.Kind == DeviceKind.Mouse && device.Name.Contains("IN9"), "Official IN9 identities override composite keyboard counts");
        }
        Check(HidDiscovery.TryDeviceKind(0x291D,0x385D,0xFF90,1,out var kind,"MCHOSE V9 PRO") && kind==DeviceKind.Headset,
            "V9 Pro vendor-only interface is discovered");
        Check(!HidDiscovery.TryDeviceKind(0x291D,0x385D,0xFF90,1,out _,"MCHOSE S9"), "Shared headset PID requires V9 product identity");
        var v9 = new HidDiscovery.Bucket { Vid=0x291D, Pid=0x385D, Product="MCHOSE V9 PRO", Kind=DeviceKind.Headset };
        Check(v9.ToDevice().Kind == DeviceKind.Headset && v9.ToDevice().Name.Contains("V9 Pro"), "V9 keeps its headset identity");
        var request=VendorProtocol.BuildCloud3SRequest(62,VendorProtocol.Cloud3SBatteryCommand);
        var response=(byte[])request.Clone(); response[6]=50;
        Check(VendorProtocol.TryParseCloud3SBattery(response,request,out int pct,out bool charge) && pct==50 && !charge,
            "HyperX battery response cannot invent charging from adjacent data");
        response[5]=0x48;
        Check(!VendorProtocol.TryParseCloud3SBattery(response,request,out _,out _), "HyperX 0x48 game-chat setting is not a battery reply");
        var cloud3=new byte[62];cloud3[0]=0x66;cloud3[1]=0x89;cloud3[2]=1;cloud3[4]=80;
        Check(VendorProtocol.TryParseCloud3Battery(cloud3,out pct) && pct==80, "Cloud III has a separate wire protocol");
        Check(!VendorProtocol.TryParseCloud3SBattery(cloud3,request,out _,out _), "Cloud III cannot be decoded as III S");
        var battery=new byte[20];battery[0]=0x11;battery[4]=77;
        Check(LogitechHidpp.TryParseBattery(0x1000,battery,out pct,out _) && pct==77, "HID++1000 is battery percentage");
        battery[4]=0x10;battery[5]=0x5A;
        Check(!LogitechHidpp.TryParseBattery(0x1000,new byte[3],out _,out _), "HID++ truncated replies rejected");
        Check(LogitechHidpp.TryParseBattery(0x1001,battery,out pct,out _) && pct==100,
            "0x1001 is documented as voltage; a plausible mV reading is usable before 0x0005 name confirmation");
        battery[4]=0x01;battery[5]=0x00;
        Check(!LogitechHidpp.TryParseBattery(0x1001,battery,out _,out _), "0x1001 outside the voltage range cannot borrow the GPW curve");
        battery[4]=0x0E;battery[5]=0xD8;battery[6]=0;
        Check(LogitechHidpp.TryParseBattery(0x1001,battery,out pct,out _,true) && pct==47, "GPW uses big endian 3800mV and official energy model");
        battery[4]=0x10;battery[5]=0x16;battery[6]=0x80;
        Check(LogitechHidpp.TryParseBattery(0x1001,battery,out pct,out charge,true) && pct==50 && charge, "GPW active charging uses official charging curve");
        battery[6]=0x81;
        Check(LogitechHidpp.TryParseBattery(0x1001,battery,out pct,out charge,true) && pct==100 && !charge, "GPW explicit full is not active charging");
        battery[6]=0x84;
        Check(!LogitechHidpp.TryParseBattery(0x1001,battery,out _,out _,true), "GPW reserved charge status cannot become charging");
        Check(LogitechHidpp.IsOriginalGpwName("PRO Wireless Gaming Mouse") && !LogitechHidpp.IsOriginalGpwName("PRO X SUPERLIGHT"), "GPW calibration is model specific");
        return checks;
    }
}
