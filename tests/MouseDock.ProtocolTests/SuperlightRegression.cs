using MouseDock.Services.Detection;
using MouseDock.Models;
internal static class SuperlightRegression
{
    internal static int Run(){int n=0;void Check(bool v){n++;if(!v)throw new Exception("GPW2 boundary");}
        Check(OriginalGpwProtocol.IsCandidate(0x046D,0xC547));
        foreach(string name in new[]{"PRO X SUPERLIGHT","PRO X Wireless"})Check(OriginalGpwProtocol.IsSuperlightName(name));
        foreach(string name in new[]{"USB Receiver","PRO X SUPERLIGHT 2","G PRO WIRELESS"})Check(!OriginalGpwProtocol.IsSuperlightName(name));
        for(int i=0;i<=100;i++)for(byte state=0;state<4;state++){var f=new byte[20];f[0]=0x11;f[4]=(byte)i;f[6]=state;Check(OriginalGpwProtocol.TrySuperlightPercentage(f,out int level,out bool ch)&&level==i&&ch==(state==1||state==2));}
        var bad=new byte[20];bad[0]=0x11;bad[4]=101;Check(!OriginalGpwProtocol.TrySuperlightPercentage(bad,out _,out _));bad[4]=60;bad[6]=4;Check(!OriginalGpwProtocol.TrySuperlightPercentage(bad,out _,out _));
        var b=new HidDiscovery.Bucket{Vid=0x3837,Pid=0x3033,Product="MCHOSE G87 V2 2.4G",Kind=DeviceKind.Mouse};b.Counts[DeviceKind.Mouse]=2;b.Counts[DeviceKind.Keyboard]=1;Check(b.ToDevice().Kind==DeviceKind.Keyboard);
        b.Product="MCHOSE G87 V2 Upgrader";Check(b.ToDevice().Kind==DeviceKind.Mouse);
        return n;
    }
}
