using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
using MouseDock.Views;

internal static class XboxWirelessUiChecks
{
    internal static void Run(string? image=null)
    {
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        using var fixture=new AppearanceFixture();
        var source=new Source();
        using var service=new BatteryService(fixture.Settings,source,false);
        fixture.Set("Battery",service);
        MchoseFeedbackUiChecks.Wait(service.PollAsync());
        var device=service.VisibleDevices.Single();
        if(device.Level!=43)throw new Exception("Xbox detailed system report did not reach selection");
        source.Relative=true;MchoseFeedbackUiChecks.Wait(service.PollAsync());
        if(device.Level!=-1||device.BatteryBand!="70/100"||device.Transport!="无线")
            throw new Exception("Xbox relative report was lost or old percentage restored");
        var window=new DockWindow {Left=-2000,ShowActivated=false};fixture.Set("Dock",window);
        window.Show();window.ApplyTheme(MouseDock.App.Theme,false);window.UpdateLayout();
        window.BeginAnimation(UIElement.OpacityProperty,null);window.Opacity=1;
        window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Render);
        var text=Texts(window).Single(t=>t.Name=="Pct"&&t.DataContext==device);
        if(!text.IsVisible||text.Text!="档位"||!device.StatusText.Contains("70/100"))
            throw new Exception("Xbox relative reading not visible in normal WPF card");
        foreach(var style in new[]{"ring","plain","card","pill","battery","panel","multi"})
        {
            var strip=new TaskbarBatteryStrip(new[]{device},style,true,false,Brushes.White,20);
            strip.Measure(new Size(500,100));strip.Arrange(new Rect(0,0,500,100));strip.UpdateLayout();
            if(!Texts(strip).Any(t=>t.Text.Contains("档位")))throw new Exception("Xbox band missing from taskbar style "+style);
        }
        if(image!=null)
        {
            var root=(FrameworkElement)window.Content;
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output=File.Create(image);encoder.Save(output);
        }
        source.Unknown=true;MchoseFeedbackUiChecks.Wait(service.PollAsync());window.UpdateLayout();
        if(device.Level!=-1||device.BatteryBand!=""||device.LevelText!="—"||text.Text!="—")
            throw new Exception("Xbox missing report revived a cached percent or band");
        Console.WriteLine("PASS Xbox synthetic system report -> discovery -> polling -> selection -> WPF card, seven taskbar styles, precise/band/unknown transitions; no hardware I/O.");
    }
    private static IEnumerable<TextBlock> Texts(DependencyObject d)
    {
        if(d is TextBlock t)yield return t;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(d);i++)foreach(var child in Texts(VisualTreeHelper.GetChild(d,i)))yield return child;
    }
    private sealed class Source:IBatterySource
    {
        public bool Relative,Unknown;
        public string Key=>"xbox-offline";public string Label=>"Synthetic Xbox system report";
        public IEnumerable<DeviceInfo> Read()
        {
            const string path=@"\\?\hid#vid_045e&pid_0b00#SYNTHETIC_UI";
            const string id="instance-xbox-ui";
            var native=new List<DetectedDevice>{new(){Id=id,PhysicalKey=id,Name="Controller (Xbox One For Windows)",Kind=DeviceKind.Controller,Connected=true,Transport="有线",Level=-1}};
            var bindings=new ControllerExpansionSystem.Bindings(new Dictionary<string,string>{{path,id}},new HashSet<(ushort,ushort)>{(0x045E,0x0B00)});
            var sample=new ControllerExpansionSystem.Sample(path,native[0].Name,0x045E,0x0B00,true,Unknown?"Unknown":"Discharging",Relative?70:430,Relative?100:1000,Relative?null:1000,null);
            ExtraDeviceRoutes.MergeSystemControllers(native,ControllerExpansionSystem.Normalize(new[]{sample},bindings));
            return new AutoDetectSource().UpdateFromDetection(native);
        }
    }
}
