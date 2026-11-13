using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
using MouseDock.Views;

internal static class DpiWindowChecks
{
    internal static void Layout()
    {
        var backend=new Backend();var prefs=new DevicePreferences(new AppSettings());
        var service=new DpiService(backend,()=>false,()=>prefs,2000);
        var device=Device();
        var theme=ReferenceThemes.Resolve("reference-mint","dark");
        var window=new DpiWindow(service,device,theme,false);
        try
        {
            window.RenderSnapshot(backend.State(device.Id));
            foreach(var size in new[]{new Size(410,430),new Size(540,535),new Size(720,650)})
            {
                var root=(FrameworkElement)window.Content;
                root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
                Require(window.StageSelector.Items.Count==8,"DPI did not render every hardware stage.");
                Require(window.SaveButton.ActualWidth>140&&window.SwitchButton.ActualWidth>140,"DPI actions do not fit the compact window.");
                Require(window.ValueEditor.ActualWidth>=120&&window.ValueEditor.ActualHeight>=30,"DPI numeric input lost a stable editing size.");
                foreach(var row in window.StageSelector.Items)
                {
                    var item=(ListBoxItem)window.StageSelector.ItemContainerGenerator.ContainerFromItem(row)!;
                    Require(item!=null&&item.ActualHeight==64&&item.ActualWidth>75,"DPI stage geometry moved with its value.");
                }
                var bitmap=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);
                bitmap.Render(root);
                var pixels=new byte[(int)size.Width*(int)size.Height*4];bitmap.CopyPixels(pixels,(int)size.Width*4,0);
                Require(pixels.Where((_,index)=>index%4==3).Any(value=>value>0),"DPI surface rendered blank.");
            }
            var dark=((SolidColorBrush)window.Background).Color;
            window.ApplyTheme(ReferenceThemes.Resolve("reference-mint","light"));
            Require(((SolidColorBrush)window.Background).Color!=dark,"DPI theme update did not recolor the open window.");
            window.StageSelector.SelectedIndex=7;
            Require(window.ValueEditor.Text=="42000"&&backend.Applies==0,"Inspecting an inactive stage issued a hardware write.");
        }
        finally{window.Close();}
    }

    internal static void Workflow()
    {
        var backend=new Backend();var prefs=new DevicePreferences(new AppSettings());
        var service=new DpiService(backend,()=>false,()=>prefs,2000);var device=Device();
        var window=new DpiWindow(service,device,ReferenceThemes.Resolve("reference-mint","dark"),false);
        try
        {
            Require(!window.SaveButton.IsEnabled&&window.CurrentText.Text.Contains("未确认"),"DPI enabled writes before a successful read.");
            Complete(window.RefreshAsync());
            Require(window.SaveButton.IsEnabled&&window.StageSelector.SelectedIndex==0,"Fresh DPI snapshot did not enable controls.");
            window.StageSelector.SelectedIndex=1;window.ValueEditor.Text="1210";Complete(window.SubmitAsync(false));
            Require(backend.Applies==1&&backend.LastStage==1&&backend.LastValue==1210&&window.StageSelector.SelectedIndex==1&&
                window.CurrentText.Text=="800 DPI","Saving an inactive stage also switched it or lost the selection.");
            Complete(window.SubmitAsync(true));
            Require(backend.LastValue==null&&window.CurrentText.Text=="1210 DPI","Switching was treated as an edit to the stage's numeric value.");
            window.ValueEditor.Text="invalid";Complete(window.SubmitAsync(false));
            Require(backend.Applies==2&&window.StatusText.Text.Contains("整数"),"Invalid text reached hardware instead of remaining editable.");
            backend.Fail=true;Complete(window.RefreshAsync());
            Require(!window.SaveButton.IsEnabled&&!window.SwitchButton.IsEnabled&&window.StageSelector.Items.Count==0&&
                window.CurrentText.Text.Contains("未确认"),"Read error left an old writable DPI snapshot on screen.");
        }
        finally{window.Close();}
    }

    internal static void ReadOnlyAndBusy()
    {
        var backend=new Backend();var prefs=new DevicePreferences(new AppSettings());var device=Device();
        var service=new DpiService(backend,()=>false,()=>prefs,2000);
        var window=new DpiWindow(service,device,ReferenceThemes.Resolve("reference-mint","light"),false);
        try
        {
            var readOnly=new DpiSnapshot(DpiProfiles.Find(0x3554,0xF58E)!,0,new[]{new DpiStage(800,800)},
                "2,32|0055|",200,18000,50,note:"接收器只读");
            window.RenderSnapshot(readOnly);
            Require(!window.SaveButton.IsEnabled&&!window.SwitchButton.IsEnabled&&!window.ValueEditor.IsEnabled&&window.StageSelector.IsEnabled,
                "Read-only DPI states did not keep browsing separate from mutations.");
            window.RenderSnapshot(new DpiSnapshot(new DpiProfile(0x3837,0x100B,0,0,"A7",DpiFamily.MchoseReadOnly,WriteApproved:false),
                0,new[]{new DpiStage(1200,1200)},"current-only",50,32000,1,false,"最近扫描只读").Bind(device.Id,"scan-only"));
            Require(window.CurrentText.Text=="1200 DPI"&&window.StageSelector.Items.Count==0&&!window.SaveButton.IsEnabled,
                "MCHOSE current-only display invented hardware stages or enabled editing.");
            backend.Block=new ManualResetEventSlim(false);
            var read=window.RefreshAsync();
            Require(backend.Entered.Wait(1000),"Busy fixture did not begin.");
            Require(!window.SaveButton.IsEnabled&&!window.StageSelector.IsEnabled,"A pending DPI request allowed another action.");
            window.Close();backend.Block.Set();Complete(read);
            Require(!window.StatusText.Text.Contains("已从设备读取"),"A closed window accepted a late result.");
        }
        finally{window.Close();backend.Block?.Set();}
    }
    private static DeviceInfo Device()=>new(){Id="instance-dpi-window",Name="ATK Zero",Detail="373B:11D8"};
    private static void Complete(Task task)
    {
        var frame=new DispatcherFrame();var deadline=DateTime.UtcNow.AddSeconds(4);
        var timer=new DispatcherTimer(TimeSpan.FromMilliseconds(5),DispatcherPriority.Background,(_,__)=>
        {if(task.IsCompleted||DateTime.UtcNow>deadline)frame.Continue=false;},Dispatcher.CurrentDispatcher);
        try{if(!task.IsCompleted)Dispatcher.PushFrame(frame);}
        finally{timer.Stop();}
        Require(task.IsCompleted,"DPI UI task did not complete.");task.GetAwaiter().GetResult();
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private sealed class Backend : IDpiBackend
    {
        private DpiSnapshot? current;
        internal int Applies,LastStage;internal int? LastValue;internal bool Fail;
        internal ManualResetEventSlim? Block;internal readonly ManualResetEventSlim Entered=new(false);
        internal DpiSnapshot State(string instance)=>new DpiSnapshot(DpiProfiles.Find(0x373B,0x11D8)!,0,
            new[]{800,1600,3200,6400,10000,20000,30000,42000}.Select(v=>new DpiStage(v,v)),"1,26",10,42000,10).Bind(instance,"window-fake");
        public DpiSnapshot Read(string instance,ushort vid,ushort pid,Action guard)
        {Entered.Set();Block?.Wait();guard();if(Fail)throw new InvalidOperationException("模拟读取失败");return current??=State(instance);}
        public DpiSnapshot Apply(DpiSnapshot before,int stage,int? value,Action guard)
        {guard();Applies++;LastStage=stage;LastValue=value;return current=before.Target(stage,value).Bind(before.InstanceId,"window-fake");}
    }
}
