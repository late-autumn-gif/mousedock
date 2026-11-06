using MouseDock.Services.Detection;
using MouseDock.Controls;
using MouseDock.Models;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
internal static class InphicStylesRegression
{
    internal static int Run()
    {
        int n=0;
        void Check(bool ok){n++;if(!ok)throw new Exception("IN9 boundary");}
        Check(VendorProtocol.IsScopedInphic(0x248A,0x5C2F,"Wireless Receiver"));
        Check(!VendorProtocol.IsScopedInphic(0x248A,0x5C2F,"AJ159"));
        Check(!VendorProtocol.IsScopedInphic(0x1D57,0xFA65,"DELUX"));
        for(byte pct=0;pct<=100;pct++)for(byte state=1;state<=3;state++)
        {
            Check(VendorProtocol.TryScopedInphicFrame(new byte[]{0,149,0x40,state,pct},true,out var r) && r.Level==(state==2?100:pct) && r.Charging==(state==3));
        }
        Check(!VendorProtocol.TryScopedInphicFrame(new byte[]{0,148,0x40,1,50},true,out _));
        Check(!VendorProtocol.TryScopedInphicFrame(new byte[]{0,149,0x40,2,255},true,out _));
        Check(!VendorProtocol.IsScopedInphicChannel(0x248A,0x5C2F,"AJ159","&mi_02",0xFF00,0,33,33));
        Check(VendorProtocol.IsScopedInphicChannel(0x1D57,0xFA60,"Receiver","x",0xA,0,5,0));
        Check(!VendorProtocol.IsScopedInphicChannel(0x1D57,0xFA60,"Receiver","x",0xB,0,5,0));
        foreach(string model in new[]{"M909","M910","IN90","M900"})
        {
            var raw=new byte[33];raw[1]=0x10;raw[3]=1;System.Text.Encoding.ASCII.GetBytes(model).CopyTo(raw,5);raw[14]=72;raw[15]=1;
            Check(VendorProtocol.TryScopedInphicFrame(raw,false,out var r)==(model!="M900"));
            if(model!="M900")Check(r.Level==72 && r.Model=="英菲克 IN9" && r.Identity==model);
            raw[15]=0;Check(!VendorProtocol.TryScopedInphicFrame(raw,false,out _));
        }
        return n;
    }
    internal static void Preview(string path)
    {
        Exception? error=null;
        var t=new Thread(()=>{try{
            var canvas=new System.Windows.Controls.Canvas { Width=660,Height=280,Background=new SolidColorBrush(Color.FromRgb(229,227,229)) };
            string[] styles={"ring","tile","pill","battery","plain","multi"};
            string[] labels={"环形刻度","柔角方块","横向胶囊","经典电池","轻量数字","多电量迷你"};
            for(int i=0;i<6;i++){
                var c=new ReferenceBatteryIcon {StyleName=styles[i],Width=170,Height=96,Devices=new(){(DeviceKind.Mouse,76,false,false),(DeviceKind.Keyboard,48,true,false),(DeviceKind.Headset,-1,false,false)}};
                canvas.Children.Add(c);System.Windows.Controls.Canvas.SetLeft(c,i%3*220+25);System.Windows.Controls.Canvas.SetTop(c,i/3*135+4);
                var label=new System.Windows.Controls.TextBlock {Text=labels[i],FontSize=16,Foreground=Brushes.Black};canvas.Children.Add(label);System.Windows.Controls.Canvas.SetLeft(label,i%3*220+62);System.Windows.Controls.Canvas.SetTop(label,i/3*135+103);
            }
            canvas.Measure(new Size(660,280));canvas.Arrange(new Rect(0,0,660,280));canvas.UpdateLayout();
            var bmp=new RenderTargetBitmap(660,280,96,96,PixelFormats.Pbgra32);bmp.Render(canvas);
            var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using var file=File.Create(path);enc.Save(file);
        }catch(Exception e){error=e;}});t.SetApartmentState(ApartmentState.STA);t.Start();t.Join();if(error!=null)throw error;
    }
}
