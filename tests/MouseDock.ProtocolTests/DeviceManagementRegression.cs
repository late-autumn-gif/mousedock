using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock;
using MouseDock.Controls;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Views;

internal static class DeviceManagementRegression
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    { NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private sealed class Source : IBatterySource
    {
        public string Key => "auto";
        public string Label => "测试数据";
        public Func<string, bool> CanRead = _ => true;
        public bool HeadsetPresent = true;
        public int HeadsetQueries;
        public int HeadsetLevel = 42;
        public bool FailReading;
        public ManualResetEventSlim? Entered, Release;
        public IEnumerable<DeviceInfo> Read()
        {
            bool read = CanRead("raw-headset");
            int level = FailReading ? -1 : HeadsetLevel;
            Entered?.Set(); Release?.Wait(TimeSpan.FromSeconds(5));
            var devices = new List<DeviceInfo>
            {
                new() { Id="mouse", Name="测试鼠标", Level=77, DetectionIds=new[]{"raw-mouse"} },
                new() { Id="keyboard", Name="测试键盘", Kind=DeviceKind.Keyboard, Level=100, DetectionIds=new[]{"raw-keyboard"} }
            };
            if (HeadsetPresent)
            {
                if (read) Interlocked.Increment(ref HeadsetQueries);
                devices.Add(new DeviceInfo { Id="headset", Name="竹林鸟玄武（测试）", Kind=DeviceKind.Headset,
                    Level=read ? level : -1, DetectionIds=new[]{"raw-headset"} });
            }
            return devices;
        }
    }
    private static void Pump(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval=TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_,__) => { timer.Stop(); frame.Continue=false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static void Await(Task task)
    {
        var clock=Stopwatch.StartNew();
        while (!task.IsCompleted && clock.ElapsedMilliseconds<8000) Pump(10);
        if (!task.IsCompleted) throw new Exception("Device management test timed out");
        task.GetAwaiter().GetResult();
        Pump(20);
    }
    private static T Find<T>(DependencyObject root, Func<T,bool> predicate) where T:DependencyObject
    {
        if (root is T value && predicate(value)) return value;
        for (int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            try { return Find(VisualTreeHelper.GetChild(root,i),predicate); } catch (InvalidOperationException) { }
        throw new InvalidOperationException("Control not found: "+typeof(T).Name);
    }
    private static void Capture(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(path); encoder.Save(stream);
    }
    internal static int Run(string directory)
    {
        int checks=0; Exception? failure=null;
        var thread=new Thread(() =>
        {
            var windows=new List<Window>();
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                Directory.CreateDirectory(directory);
                SettingsStore.TestDirectory=Path.GetFullPath(Path.Combine(directory,"isolated-settings"));
                void Check(bool condition,string message) { checks++; if(!condition) throw new Exception(message); }
                var settings=new AppSettings { SelectedDeviceIds=new() { "mouse","keyboard" }, AutoUpdate=false, HasShownAbout=true };
                var source=new Source();
                using var battery=new BatteryService(settings,source,false);
                source.CanRead=id=>battery.Preferences.CanRead(id);
                Await(battery.PollAsync());
                Check(battery.Devices.Count==3 && battery.VisibleDevices.Count==2,"Late discovery applies the saved two-device selection");
                var headset=battery.Devices.Single(d=>d.Id=="headset");
                Check(!headset.Selected && headset.RecognitionEnabled,"An unchecked headset remains in management but not displays");
                typeof(App).GetProperty(nameof(App.Battery))!.SetValue(null,battery);
                typeof(App).GetProperty(nameof(App.Settings))!.SetValue(null,settings);
                var dock=new DockWindow(); var mini=new MiniWindow(); var icon=new IconWindow(); var manager=new SettingsWindow();
                dock.ApplyTheme(App.Theme,false); mini.ApplyTheme(App.Theme,false); icon.ApplyTheme(App.Theme,false);
                windows.AddRange(new Window[]{dock,mini,icon,manager});
                foreach(var window in windows) { window.Left=-16000; window.Top=0; window.Show(); }
                Pump(120);
                var dockList=(ItemsControl)dock.FindName("DeviceList");
                var miniList=(ItemsControl)mini.FindName("CellList");
                var iconList=(ItemsControl)icon.FindName("DeviceList");
                var deviceChecks=(ItemsControl)manager.FindName("DeviceChecks");
                Check(ReferenceEquals(dockList.ItemsSource,battery.VisibleDevices),"Main card uses the shared filtered collection");
                Check(dockList.Items.Count==2 && miniList.Items.Count==2 && iconList.Items.Count==2,"All three actual view templates honor the checkbox");
                Check(deviceChecks.Items.Count==3,"Unchecked devices stay recoverable in settings");
                var checkbox=Find<CheckBox>(deviceChecks,c=>ReferenceEquals(c.DataContext,headset));
                checkbox.SetCurrentValue(CheckBox.IsCheckedProperty,true); Pump(40);
                Check(headset.Selected && battery.VisibleDevices.Count==3,"The actual settings checkbox immediately shows the device");
                checkbox.SetCurrentValue(CheckBox.IsCheckedProperty,false); Pump(40);
                Check(!headset.Selected && dockList.Items.Count==2 && miniList.Items.Count==2 && iconList.Items.Count==2,"Unchecking immediately removes it from every actual view");
                Await(battery.PollAsync());
                Check(!headset.Selected && battery.VisibleDevices.Count==2,"A subsequent discovery does not reset selection");
                Check(App.PrimaryDevice?.Id!="headset","The primary/tray selection cannot fall back to an unchecked device");

                Capture(dock,Path.Combine(directory,"设备勾选-主面板模拟预览.png"));
                Capture(manager,Path.Combine(directory,"设备管理-设置模拟预览.png"));
                var menu=DeviceActions.CreateMenu(headset,battery);
                var actions=menu.Items.OfType<MenuItem>().Where(m=>m.IsEnabled).ToList();
                Check(actions.Select(a=>a.Header.ToString()).SequenceEqual(new[]{"取消识别","隐藏设备","重新识别"}),"The requested three device actions appear in order");
                var deviceRow=Find<Border>(deviceChecks,b=>ReferenceEquals(b.DataContext,headset) && DeviceActions.GetEnabled(b));
                Check(DeviceActions.GetEnabled(deviceRow),"The actual settings row enables the device context menu");
                var rightClick=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Right)
                    { RoutedEvent=UIElement.PreviewMouseRightButtonUpEvent };
                deviceRow.RaiseEvent(rightClick); Pump(20);
                Check(rightClick.Handled && deviceRow.ContextMenu.IsOpen && deviceRow.ContextMenu.Items.OfType<MenuItem>().Any(m=>Equals(m.Header,"取消识别")),
                    "The device right-click opens its actions and suppresses the generic window menu");
                deviceRow.ContextMenu.IsOpen=false;
                var visibleRow=Find<Border>(dockList,b=>DeviceActions.GetEnabled(b));
                Check(DeviceActions.GetEnabled(visibleRow),"The actual main card row enables the device context menu");
                menu.PlacementTarget=deviceRow; menu.IsOpen=true; Pump(40);
                if (menu.ActualWidth>0 && menu.ActualHeight>0) Capture(menu,Path.Combine(directory,"设备右键菜单-模拟预览.png"));
                menu.IsOpen=false;

                actions[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(20);
                Check(headset.Hidden && !headset.Selected && !battery.VisibleDevices.Contains(headset),"Hide action hides rather than removes management metadata");
                Check(battery.Devices.Contains(headset) && headset.RecognitionEnabled,"Hidden devices remain managed and recognition stays enabled");
                int queries=source.HeadsetQueries;
                Await(battery.PollAsync());
                Check(source.HeadsetQueries>queries && headset.Hidden,"Hidden devices can still refresh without becoming visible");
                checkbox.SetCurrentValue(CheckBox.IsCheckedProperty,true); Pump(20);
                Check(!headset.Hidden && headset.Selected && battery.VisibleDevices.Contains(headset),"Checking a hidden device restores its display");

                var cancel=DeviceActions.CreateMenu(headset,battery).Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"取消识别"));
                cancel.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(80);
                Check(!headset.RecognitionEnabled && !headset.Selected && !battery.VisibleDevices.Contains(headset),"Cancel recognition immediately excludes the device");
                Check(headset.Level==-1 && !headset.Charging,"Cancel recognition clears the old reading and charging state");
                Check(!battery.Preferences.CanRead("raw-headset"),"The transport alias is excluded from future queries");
                queries=source.HeadsetQueries;
                Await(battery.PollAsync());
                Check(source.HeadsetQueries==queries && battery.Devices.Contains(headset),"Cancelled devices are passively listed without another query");
                var serialized=JsonSerializer.Serialize(settings,JsonOptions);
                var restored=JsonSerializer.Deserialize<AppSettings>(serialized,JsonOptions)!;
                using (var restart=new BatteryService(restored,new Source(),false))
                {
                    Await(restart.PollAsync());
                    Check(!restart.Devices.Single(d=>d.Id=="headset").RecognitionEnabled && restart.VisibleDevices.Count==2,"Recognition cancellation survives JSON round-trip and restart");
                }
                var retry=DeviceActions.CreateMenu(headset,battery).Items.OfType<MenuItem>().Single(m=>Equals(m.Header,"重新识别"));
                retry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump(80);
                Await(battery.PollAsync());
                Check(headset.RecognitionEnabled && headset.Selected && !headset.Hidden && headset.Level==42,"Reidentify restores polling and display using a new result");
                Check(source.HeadsetQueries>queries,"Reidentify actually requests a new sample");
                Check(!settings.DisabledRecognitionDeviceIds.Contains("raw-headset") && !settings.HiddenDeviceIds.Contains("headset"),"Reidentify removes persisted exclusion keys");

                source.FailReading=true;
                Await(battery.PollAsync());
                Check(headset.Level==42 && headset.IsStale,"Normal refresh keeps the last real percentage if the receiver is temporarily empty");
                battery.Reidentify(headset);
                Check(headset.Level==42 && headset.IsStale && !headset.IsChargingActive,"Reidentify keeps the known percentage while a new reply is pending");
                Pump(60); Await(battery.PollAsync());
                Check(headset.Level==42 && headset.Note.Contains("待刷新"),"A failed retry retains the old value with an explicit stale note");
                source.FailReading=false; Await(battery.PollAsync());
                Check(headset.Level==42 && !headset.IsStale && !headset.Note.Contains("待刷新"),"The next successful reply clears the stale note");

                foreach(var device in battery.Devices.ToArray()) battery.SetSelected(device,false);
                Pump(40);
                Check(battery.VisibleDevices.Count==0 && App.PrimaryDevice==null,"Explicitly selecting none has no fallback device");
                Check(dockList.Items.Count==0 && miniList.Items.Count==0 && iconList.Items.Count==0,"All actual views remain empty when none is selected");
                Check(((TextBlock)dock.FindName("EmptyDeviceHint")).IsVisible && ((TextBlock)mini.FindName("EmptyDeviceHint")).IsVisible,"Empty displays explain that no device is selected");
                Check(settings.DeviceSelectionInitialized && settings.SelectedDeviceIds.Count==0,"An explicit empty selection is represented persistently");
                var empty=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings,JsonOptions),JsonOptions)!;
                using(var restart=new BatteryService(empty,new Source(),false))
                {
                    Await(restart.PollAsync()); Check(restart.VisibleDevices.Count==0,"Restart does not turn an empty selection into select-all");
                }

                battery.SetSelected(headset,true);
                battery.HideDevice(headset);
                source.HeadsetPresent=false;
                for(int i=0;i<3;i++) Await(battery.PollAsync());
                Check(!battery.Devices.Any(d=>d.Id=="headset"),"Disconnect removes only the live inventory row");
                source.HeadsetPresent=true; Await(battery.PollAsync());
                headset=battery.Devices.Single(d=>d.Id=="headset");
                Check(headset.Hidden && !headset.Selected,"Hide survives disconnect and reconnect");
                battery.SetSelected(headset,true);
                Check(!headset.Hidden && headset.Selected,"A reconnected hidden row can be restored manually");

                source.Entered=new ManualResetEventSlim(false); source.Release=new ManualResetEventSlim(false);
                var pending=battery.PollAsync();
                Check(source.Entered.Wait(2000),"A real background poll is in flight");
                battery.CancelRecognition(headset);
                source.Release.Set(); Await(pending); Pump(100);
                Check(!headset.RecognitionEnabled && headset.Level==-1 && !battery.VisibleDevices.Contains(headset),"An old in-flight result cannot undo cancellation");
                source.Entered=null; source.Release=null;

                battery.Reidentify(headset); Pump(80);
                source.HeadsetLevel=11;
                source.Entered=new ManualResetEventSlim(false); source.Release=new ManualResetEventSlim(false);
                pending=battery.PollAsync();
                Check(source.Entered.Wait(2000),"An old sample is waiting when reidentify is requested");
                var observed=new List<int>();
                EventHandler observer=(_,__)=>observed.Add(headset.Level);
                battery.Refreshed+=observer;
                source.HeadsetLevel=64;
                battery.Reidentify(headset);
                source.Release.Set(); Await(pending); Pump(120);
                battery.Refreshed-=observer;
                Check(!observed.Contains(11) && headset.Level==64,"Reidentify discards the pre-reset sample and takes a new result");
                source.Entered=null; source.Release=null;

                var noneWithNull=new AppSettings { DeviceSelectionInitialized=true, SelectedDeviceIds=null!, HiddenDeviceIds=null!, DisabledRecognitionDeviceIds=null! };
                var nullPreferences=new DevicePreferences(noneWithNull);
                var nullDevice=new DeviceInfo { Id="new" }; nullPreferences.Apply(nullDevice);
                Check(!nullDevice.Selected,"Explicit empty selection remains empty even with null legacy lists");
                var aliasSettings=new AppSettings { DisabledRecognitionDeviceIds=new() { "HID-4C4A-4155" } };
                var aliases=new DevicePreferences(aliasSettings);
                Check(!aliases.CanRead("hid-4c4a-4155"),"Device transport gates match persisted aliases case-insensitively");

                var defaults=new AppSettings();
                using(var fresh=new BatteryService(defaults,new Source(),false))
                {
                    Await(fresh.PollAsync()); Check(fresh.VisibleDevices.Count==3,"A new installation keeps automatic display until the first user choice");
                }
                typeof(App).GetProperty(nameof(App.Dock))!.SetValue(null,dock);
                typeof(App).GetProperty(nameof(App.SettingsView))!.SetValue(null,manager);
                foreach(string mode in new[]{"taskbar","edge"})
                {
                    manager.Hide(); settings.SurfaceMode=mode; App.ShowLaunchSurface(); Pump(30);
                    Check(manager.IsVisible,"Explicit launch exposes settings for "+mode+" even without a visible shell badge");
                }
                foreach(var device in battery.Devices) device.Selected=false;
                settings.SurfaceMode="dock"; manager.Hide(); App.ShowLaunchSurface(); Pump(30);
                Check(manager.IsVisible,"Empty/stale selection exposes device management rather than an invisible launch");
                var choices=(System.Windows.Controls.Primitives.UniformGrid)manager.FindName("IconStyleChoices");
                Check(choices.Children.Count==6,"All six replica styles are discoverable in settings");
                typeof(App).GetProperty(nameof(App.Icon))!.SetValue(null,icon);
                foreach(var d in battery.Devices)d.Selected=false;
                battery.Devices[0].Level=76;battery.Devices[0].Selected=true;
                battery.Devices[1].Level=-1;battery.Devices[1].Selected=true;
                foreach(string style in new[]{"ring","tile","pill","battery","plain","multi"})
                {
                    choices.Children.OfType<Button>().Single(b=>Equals(b.Tag,style)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(20);
                    Check(settings.IconStyle==style&&settings.SurfaceMode=="icon"&&icon.IsVisible,"Style selection activates the actual floating icon: "+style);
                    Check(SettingsStore.Load().IconStyle==style,"Style persists in isolated test settings: "+style);
                    Check(!((FrameworkElement)icon.FindName("LegacyLayers")).IsVisible&&!iconList.IsVisible,"No legacy/photo/bottom list overlaps "+style);
                    var host=(System.Windows.Controls.Primitives.UniformGrid)icon.FindName("ReferenceIcons");
                    var cells=host.Children.OfType<ReferenceBatteryIcon>().ToArray();
                    Check(cells.Length==(style=="multi"?1:2),"Each selected device rendered in "+style);
                    Check(cells[0].Devices[0].Item2==76&&(style=="multi"?cells[0].Devices[1].Item2:cells[1].Devices[0].Item2)==-1,"Known and unknown readings stay with their own device");
                    var iconSize=(Slider)manager.FindName("IconScaleSlider");
                    foreach(double size in new[]{.5,2,1}){
                        iconSize.Value=size;Pump(20);
                        Check(icon.Content is FrameworkElement root&&root.LayoutTransform is ScaleTransform st&&Math.Abs(st.ScaleX-size)<.001,"Actual floating style scales: "+style);
                        Check(SettingsStore.Load().IconScale==size&&settings.Scale==1,"Icon size persists independently from panel scale");
                    }
                    Capture(icon,Path.Combine(directory,"实际悬浮窗-"+style+".png"));
                }
                var taskbar=new TaskbarWindow(); windows.Add(taskbar);
                typeof(App).GetProperty(nameof(App.Taskbar))!.SetValue(null,taskbar);
                App.SetSurface("taskbar"); manager.Show(); Pump(40);
                var stylePanel=(FrameworkElement)manager.FindName("TaskbarStylePanel");
                stylePanel.BringIntoView(); Pump(40);
                Check(stylePanel.IsVisible,"Taskbar style controls are visible in taskbar mode");
                Capture(manager,Path.Combine(directory,"任务栏样式-设置入口.png"));
                foreach(string style in new[]{"panel","ring"})
                {
                    var taskbarChoices=(System.Windows.Controls.Primitives.UniformGrid)manager.FindName("TaskbarStyleChoices");
                    taskbarChoices.Children.OfType<RadioButton>().Single(b=>Equals(b.Tag,style)).IsChecked=true;
                    Pump(40);
                    Check(settings.TaskbarStyle==style && SettingsStore.Load().TaskbarStyle==style,"Taskbar style choice persists: "+style);
                    var strip=(TaskbarBatteryStrip)((Viewbox)taskbar.Content).Child;
                    Check(strip.StyleName==style && strip.DeviceCount==2,"Actual taskbar changes style immediately: "+style);
                    Check(settings.SurfaceMode=="taskbar","Style choice keeps taskbar mode");
                    var numbers=(CheckBox)manager.FindName("BatteryNumberBox");
                    numbers.IsChecked=true; Pump(20);
                    strip=(TaskbarBatteryStrip)((Viewbox)taskbar.Content).Child;
                    Check(Find<TextBlock>(strip,t=>t.Name=="BatteryNumber").Visibility==Visibility.Visible,"Number switch shows values");
                    Capture(strip,Path.Combine(directory,"任务栏-"+style+".png"));
                    numbers.IsChecked=false; Pump(20);
                    strip=(TaskbarBatteryStrip)((Viewbox)taskbar.Content).Child;
                    Check(Find<TextBlock>(strip,t=>t.Name=="BatteryNumber").Visibility==Visibility.Collapsed,"Number switch hides values");
                }
                foreach(double dpi in new[]{1d,1.25,1.5,2})
                {
                    var bar=new Rect(0,1000,1920,32*dpi);
                    Check(bar.Contains(TaskbarPlacement.CalculateBadges(bar,3,2,true,8,dpi)),"Digits stay inside thin taskbar at DPI "+dpi);
                }
                choices.BringIntoView();Pump(80);
                Capture(manager,Path.Combine(directory,"悬浮样式-设置入口.png"));
                foreach(bool download in new[]{false,true})
                {
                    var prompt=new UpdateAvailableWindow(new UpdateService.UpdateInfo{Version="9.9.9"});windows.Add(prompt);prompt.Left=-16000;prompt.Show();Pump(20);
                    Check(Equals(prompt.DownloadNow.Content,"立即下载")&&Equals(prompt.Later.Content,"稍后"),"Update prompt exposes requested choices");
                    (download?prompt.DownloadNow:prompt.Later).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(prompt.DownloadRequested==download&&!prompt.IsVisible,"Update dialog returns explicit choice without starting a download");
                }
                var clone=settings.Clone();
                clone.HiddenDeviceIds.Add("clone-only"); clone.DisabledRecognitionDeviceIds.Add("clone-only");
                Check(!settings.HiddenDeviceIds.Contains("clone-only") && !settings.DisabledRecognitionDeviceIds.Contains("clone-only"),"Cloned preference lists are independent");
            }
            catch(Exception ex) { failure=ex; }
            finally { foreach(var window in windows) window.Close(); SettingsStore.TestDirectory=null; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if(failure!=null) throw new Exception("Device management regression",failure);
        return checks;
    }
}
