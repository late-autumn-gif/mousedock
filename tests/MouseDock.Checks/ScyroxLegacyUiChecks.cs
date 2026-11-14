using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
using MouseDock.Views;

internal static class ScyroxLegacyUiChecks
{
    internal static void Run(string? image = null)
    {
        using var fixture = new AppearanceFixture();
        using var service = new BatteryService(fixture.Settings,new Source(),false);
        fixture.Set("Battery",service);
        MchoseFeedbackUiChecks.Wait(service.PollAsync());
        var device=service.VisibleDevices.Single();
        if(device.Kind!=DeviceKind.Mouse || device.Level!=63 || !device.IsDisplayEligible)
            throw new Exception("Scyrox recognition/polling/selection pipeline lost the reading");
        var window=new DockWindow {Left=-2000,ShowActivated=false};fixture.Set("Dock",window);
        window.Show();window.ApplyTheme(MouseDock.App.Theme,false);window.UpdateLayout();
        window.BeginAnimation(UIElement.OpacityProperty,null);window.Opacity=1;
        window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Render);
        IEnumerable<TextBlock> Texts(DependencyObject d)
        {
            if(d is TextBlock t)yield return t;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++)foreach(var child in Texts(VisualTreeHelper.GetChild(d,i)))yield return child;
        }
        var number=Texts(window).Single(t=>t.Name=="Pct" && t.DataContext==device);
        if(!number.IsVisible || number.Text!="63%")throw new Exception("Scyrox live value not visible in normal WPF card");
        if(image!=null)
        {
            var root=(FrameworkElement)window.Content;
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create(image);encoder.Save(output);
        }
        Console.WriteLine("PASS simulated Scyrox GETs -> discovery -> BatteryService -> selection -> visible 63% card; no hardware I/O.");
    }
    private sealed class Source : IBatterySource
    {
        public string Key=>"scyrox-offline"; public string Label=>"Scyrox synthetic protocol fixture";
        public IEnumerable<DeviceInfo> Read()
        {
            InstanceScope.BeginScan();const string path=@"test&mi_01&col05#scyrox-ui";
            string id=InstanceScope.Register(path,new[]{(@"USB\VID_3554&PID_F5F7\SYNTHETIC_UI","usbccgp")});
            var bucket=new HidDiscovery.Bucket {Vid=0x3554,Pid=0xF5F7,Product="SCYROX 8K Dongle",InstanceId=id,Kind=DeviceKind.Other};
            var buckets=new Dictionary<string,HidDiscovery.Bucket>{[id]=bucket};
            var channels=new List<HidDiscovery.VendorCollectionInfo>{new(){Vid=0x3554,Pid=0xF5F7,Product=bucket.Product,Path=path,UsagePage=0xFF02,Usage=2,Input=17,Output=17}};
            HidDiscovery.QueryExtraDevices(buckets,channels,false,null,(_,_,_,_,_,t)=>ScyroxLegacyProtocol.Read(new(new Link(),17,17,trace:t)));
            return new AutoDetectSource().UpdateFromDetection(new(){bucket.ToDevice()});
        }
    }
    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        private byte[]? pending;
        public long Now{get;private set;}
        public byte[]? Read(int ms){Now+=Math.Max(1,ms);var b=pending;pending=null;return b;}
        public bool Write(byte[] b,int ms)
        {
            pending=new byte[17];pending[0]=8;pending[1]=b[1];pending[5]=4;pending[6]=b[1]==3?(byte)1:(byte)63;
            pending[16]=unchecked((byte)(0x55-pending.Take(16).Sum(v=>(int)v)));return true;
        }
        public bool SetFeature(byte[] b)=>throw new Exception("Unexpected Feature query");
        public byte[] GetFeature(byte id,int size)=>throw new Exception("Unexpected Feature query");
        public void Delay(int ms)=>Now+=ms;
    }
}
