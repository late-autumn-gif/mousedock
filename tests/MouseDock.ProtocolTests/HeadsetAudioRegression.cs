using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
using MouseDock.Views;

internal static class HeadsetAudioRegression
{
    internal static int RunPolicy()
    {
        int n=0;
        void Check(bool value,string reason) { n++; if (!value) throw new Exception(reason); }
        const string zg4 = "ZhuAudio ZG4";
        Check(HidDiscovery.TryDeviceKind(0x3654, 0x5055, 0xFF00, 1, out var zg4Kind, zg4) &&
              zg4Kind == DeviceKind.Headset, "ZG4 HID product and WebHID filter classify together");
        Check(!VendorMap.TryUsbHeadset(0x3654, 0x5055, out _, "ZhuAudio ZG3") &&
              !VendorMap.TryUsbHeadset(0x3654, 0x5055, out _, null) &&
              !DeviceRoutes.IsKnown(0x3654, 0x5055, zg4), "Shared USB ID must not enable a private query");
        var zg4Usb = new HidDiscovery.Bucket { Vid = 0x3654, Pid = 0x5055, Product = zg4, Kind = zg4Kind }.ToDevice();
        Check(zg4Usb is { Kind: DeviceKind.Headset, Transport: "USB", Level: -1 },
              "ZG4 USB is listed with unknown battery and no inferred wireless channel");
        const byte reportId = 0xCD;
        const byte sequence = 0x31;
        Check(VendorProtocol.BuildZg4DongleRequest(reportId, 64, 2).Take(9)
            .SequenceEqual(new byte[] { reportId, 7, 0xAA, 0x5A, 0xCC, 0x80, 0x51, 1, 2 }),
            "ZG4 online query matches the driver framing");
        Check(VendorProtocol.BuildZg4DongleRequest(reportId, 64, 6).Take(9)
            .SequenceEqual(new byte[] { reportId, 7, 0xAA, 0x5A, 0xCC, 0x81, 0x92, 1, 6 }),
            "ZG4 identity query matches the driver framing");
        byte[] online = { reportId, 0xAA, 0xCC, 0, 2, 0xC0, 0xA0, 2, 1 };
        byte[] identity = { reportId, 0xAA, 0xCC, 0, 5, 0x8A, 0x9C, 6, 0, 0, 5, 16 };
        Check(VendorProtocol.IsZg4Online(online, reportId) && VendorProtocol.IsZg4Identity(identity, reportId),
            "ZG4 validates online status and little-endian MID");
        Check(!VendorProtocol.IsZg4Online(null, reportId) && !VendorProtocol.IsZg4Identity(identity, 0),
            "ZG4 rejects timeout and wrong report ID");
        identity[5] ^= 1;
        Check(!VendorProtocol.IsZg4Identity(identity, reportId), "ZG4 rejects bad CRC");
        identity[5] ^= 1;
        // Correct CRC for a valid reply containing the neighboring ZG3 MID (0x1009).
        byte[] zg3Identity = { reportId, 0xAA, 0xCC, 0, 5, 0x8F, 0x9C, 6, 0, 0, 9, 16 };
        Check(!VendorProtocol.IsZg4Identity(zg3Identity, reportId), "ZG4 rejects a different model");
        Check(VendorProtocol.BuildZg4BatteryRequest(reportId, 64, sequence).Take(12)
            .SequenceEqual(new byte[] { reportId, 0xEE, 0x80, 0, 6, 0, 0xFF, sequence, 2, 0xFE, 7, 0xAA }),
            "ZG4 uses the read-only property command");
        byte[] batteryReply = { reportId, 0xEA, 0x80, 0, 9, 0, 0xFF, sequence, 5, 0xFE, 7, 0xC3, 0, 0, 0xAA };
        int usbLevel = VendorProtocol.ParseZg4Battery(batteryReply, reportId, sequence, out bool usbCharging);
        var liveZg4 = new HidDiscovery.Bucket { Vid = 0x3654, Pid = 0x5055, Product = zg4, Identity = zg4,
            Kind = DeviceKind.Headset, Battery = usbLevel, Charging = usbCharging }.ToDevice();
        var displayedZg4 = new AutoDetectSource().UpdateFromDetection(new() { liveZg4 }).Single();
        Check(displayedZg4 is { Name: zg4, Kind: DeviceKind.Headset, Transport: "2.4G", Level: 67, Charging: true },
            "ZG4 wireless battery and charging flow to the display model");
        Check(VendorProtocol.ParseZg4Battery(batteryReply, reportId, 0, out _) < 0 &&
              VendorProtocol.ParseZg4Battery(batteryReply.AsSpan(0, 12), reportId, sequence, out _) < 0,
            "ZG4 rejects an old sequence and a truncated frame");
        foreach (byte level in new byte[] { 0, 100, 101 })
        {
            batteryReply[11] = level;
            Check(VendorProtocol.ParseZg4Battery(batteryReply, reportId, sequence, out bool charging) ==
                  (level <= 100 ? level : -1) && !charging, "ZG4 validates level " + level);
        }
        Check(VendorMap.KindFromName(zg4) == DeviceKind.Headset &&
              !VendorMap.IsZhuAudioZg4("ZhuAudio ZG2") && !VendorMap.IsZhuAudioZg4("ZhuAudio ZG40"),
              "ZG4 name classifies as a headset without matching neighboring names");
        var battery = new BtSystemBattery.Entry();
        BtSystemBattery.ApplyProperties(battery, new Dictionary<string, object>
        {
            [BtSystemBattery.BluetoothBatteryKey] = (byte)67
        }, "synthetic Windows property");
        var detected = new DetectedDevice { Id = "bt-test-zg4", Name = zg4, Kind = DeviceKind.Headset,
            Transport = "蓝牙", Connected = true, Level = battery.Level };
        var source = new AutoDetectSource();
        Check(source.UpdateFromDetection(new() { detected }).Single() is { Kind: DeviceKind.Headset, Level: 67 },
              "ZG4 synthetic system battery reaches the display model");
        detected.Level = -1;
        Check(source.UpdateFromDetection(new() { detected }).Single().Level == -1,
              "ZG4 without a battery reading stays unknown");
        Guid a=Guid.NewGuid(), b=Guid.NewGuid();
        var ear=new AudioEndpointInfo("out","Windows 耳机原名",false,a,5,42,false);
        var mic=new AudioEndpointInfo("mic","Windows 麦克风原名",true,a,4,60,false);
        var other=new AudioEndpointInfo("other","Windows 麦克风原名",true,b,4,60,false);
        var inputs=new[] { ear,mic,other };
        Check(ear.IsHeadset && !mic.IsHeadset,"Only render endpoints can be headphones");
        Check(HeadsetAudioPolicy.MicrophoneFor(ear,inputs,new Dictionary<string,string>())=="mic","Unique physical headset capture pairing");
        Check(HeadsetAudioPolicy.MicrophoneFor(ear,new[] { mic,mic with { Id="mic2" } },null)==null,"Ambiguous captures are not guessed");
        Check(HeadsetAudioPolicy.MicrophoneFor(ear with { FormFactor=3 },inputs,null)==null,"Shared sound-card headphone jack must not auto-mute a laptop microphone");
        Check(HeadsetAudioPolicy.MicrophoneFor(ear with { FormFactor=3 },new[] { mic with { FormFactor=5 } },null)=="mic","Explicit headset capture may pair with a headphones playback endpoint");
        Check(HeadsetAudioPolicy.MicrophoneFor(ear with { Container=Guid.Empty },inputs,null)==null,"No global fallback microphone without container evidence");
        Check(HeadsetAudioPolicy.MicrophoneFor(ear,inputs,new Dictionary<string,string> { ["out"]="gone" })==null,"Disconnected manual target never falls back to another microphone");
        Check(HeadsetAudioPolicy.MicrophoneFor(ear,inputs,new Dictionary<string,string> { ["out"]="" })==null,"Explicit no-microphone choice is retained");
        Check(HeadsetAudioPolicy.MicrophoneFor(ear,inputs,new Dictionary<string,string> { ["out"]="other" })=="other","Explicit user pairing uses exact endpoint ID");
        Check(HeadsetAudioPolicy.MicrophoneFor(ear,inputs,new Dictionary<string,string> { ["out"]="out" })==null,"Render endpoint cannot be selected as microphone");
        Check(!(ear with { Name="USB Composite Device",FormFactor=1 }).IsHeadset,"Generic USB speaker name is not renamed or inferred as a headset");
        Check((ear with { Name="Headphones (USB Audio)",FormFactor=1 }).IsHeadset,"Windows headphone name is discoverable");
        Check(VendorMap.KindFromName("ATK A9 Air")==null,"Air in a mouse model does not become headset");
        var settings=new AppSettings { HeadsetOutputId="out",HeadsetMicrophoneBindings=new() { ["out"]="mic" } };
        var options=new JsonSerializerOptions { NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        var roundtrip=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings,options),options)!;
        Check(roundtrip.HeadsetOutputId=="out" && roundtrip.HeadsetMicrophoneBindings["out"]=="mic","Only endpoint selections persist; volume and mute are read from Windows");
        return n;
    }

    internal static int RunUi(string directory)
    {
        Exception? failure=null;
        int n=0;
        var thread=new Thread(()=>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                Directory.CreateDirectory(directory);
                void Check(bool value,string reason) { n++; if (!value) throw new Exception(reason); }
                var backend=new FakeBackend();
                var settings=new AppSettings();
                int saves=0;
                var window=new HeadsetAudioWindow(backend,settings,ThemePreset.Presets[2],()=>saves++,false);
                window.DisplayEndpoints(backend.Endpoints);
                Check(window.OutputSelector.SelectedItem is AudioEndpointInfo endpoint && endpoint.Name=="耳机（USB Audio Device）","UI preserves Windows endpoint name");
                Check(window.VolumeText.Text=="42%" && window.MuteButton.IsEnabled,"Volume and uniquely paired microphone read from snapshot");
                Check(backend.Writes.Count==0 && saves==0,"Opening and populating controls never writes system audio settings");
                Capture(window,"headset-dark.png",directory);
                Await(window.ApplyVolumeAsync("out",37));
                Check(backend.Writes.SequenceEqual(new[] { "volume:out:37" }) && window.VolumeText.Text=="37%","Volume affects selected render endpoint and uses readback");
                Await(window.ApplyVolumeAsync("mic",80));
                Check(backend.Writes.Count==1,"A capture endpoint cannot receive output-volume writes");
                Await(window.ToggleMicrophoneAsync());
                Check(backend.Writes.Last()=="mute:mic:True" && window.MuteButton.Content.ToString()=="恢复麦克风","Mic mute targets paired capture and confirms actual state");
                Capture(window,"headset-muted.png",directory);
                Await(window.ToggleMicrophoneAsync());
                Check(backend.Writes.Last()=="mute:mic:False" && window.MuteButton.Content.ToString()=="麦克风静音","Mic can be restored");
                backend.FailWrites=true;
                Await(window.ToggleMicrophoneAsync());
                Check(window.StatusText.Text=="测试：Windows 拒绝操作" && window.MuteButton.Content.ToString()=="麦克风静音","Rejected mute never displays success");
                backend.FailWrites=false;
                using var volumeEntered=new ManualResetEventSlim();
                using var volumeRelease=new ManualResetEventSlim();
                backend.VolumeEntered=volumeEntered; backend.VolumeRelease=volumeRelease;
                var firstVolume=window.ApplyVolumeAsync("out",10);
                Check(volumeEntered.Wait(2000),"Delayed volume operation started");
                Await(window.ApplyVolumeAsync("out",90));
                volumeRelease.Set(); Await(firstVolume);
                PumpUntil(()=>backend.Writes.LastOrDefault()=="volume:out:90");
                Check(backend.Writes.Last()=="volume:out:90","Rapid volume edits retain the final requested value");
                backend.VolumeEntered=null; backend.VolumeRelease=null;
                using var enumerateEntered=new ManualResetEventSlim();
                using var enumerateRelease=new ManualResetEventSlim();
                backend.EnumerateEntered=enumerateEntered; backend.EnumerateRelease=enumerateRelease;
                var oldRefresh=window.RefreshAsync();
                Check(enumerateEntered.Wait(2000),"Delayed read-only refresh started");
                Await(window.ToggleMicrophoneAsync());
                enumerateRelease.Set(); Await(oldRefresh);
                Check(window.MuteButton.Content.ToString()=="恢复麦克风","An older poll cannot overwrite the confirmed microphone change");
                backend.EnumerateEntered=null; backend.EnumerateRelease=null;
                backend.Endpoints=backend.Endpoints.Where(e=>!e.Capture).ToArray();
                window.DisplayEndpoints(backend.Endpoints);
                int writes=backend.Writes.Count;
                Await(window.ToggleMicrophoneAsync());
                Check(!window.MuteButton.IsEnabled && backend.Writes.Count==writes,"Unplugged microphone is disabled and never rerouted to another device");
                window.DisplayEndpoints(Array.Empty<AudioEndpointInfo>());
                Check(!window.VolumeSlider.IsEnabled && !window.MuteButton.IsEnabled,"Disconnected controls cannot write");
                window.DisplayEndpoints(new[] { new AudioEndpointInfo("different","另一副耳机",false,Guid.NewGuid(),5,30,false) });
                Check(window.OutputSelector.SelectedItem==null && !window.VolumeSlider.IsEnabled,"Unplugging the selected headset never silently selects another one");
                Capture(window,"headset-empty.png",directory);
                window.Close();
                var lightBackend=new FakeBackend();
                var light=new HeadsetAudioWindow(lightBackend,new AppSettings(),ThemePreset.Presets[1],()=>{},false);
                light.DisplayEndpoints(lightBackend.Endpoints);
                Capture(light,"headset-light.png",directory);
                lightBackend.FailEnumeration=true;
                Await(light.RefreshAsync());
                Check(!light.OutputSelector.IsEnabled && !light.VolumeSlider.IsEnabled && !light.MuteButton.IsEnabled,"An audio-service error disables stale controls");
                lightBackend.FailEnumeration=false;
                Await(light.RefreshAsync());
                Check(light.OutputSelector.IsEnabled && light.VolumeSlider.IsEnabled,"A successful refresh restores available controls");
                light.Close();
            }
            catch (Exception ex) { failure=ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure!=null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return n;
    }

    private static void Await(Task task)
    {
        var clock=Stopwatch.StartNew();
        while (!task.IsCompleted && clock.ElapsedMilliseconds<5000)
        {
            var frame=new DispatcherFrame();
            var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(10) };
            timer.Tick+=(_,__)=> { timer.Stop(); frame.Continue=false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        if (!task.IsCompleted) throw new Exception("Audio UI test timed out");
        task.GetAwaiter().GetResult();
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var clock=Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds<3000)
        {
            var frame=new DispatcherFrame();
            var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(10) };
            timer.Tick+=(_,__)=> { timer.Stop(); frame.Continue=false; };
            timer.Start(); Dispatcher.PushFrame(frame);
        }
        if (!condition()) throw new Exception("Audio action was not applied");
    }

    private static void Capture(HeadsetAudioWindow window,string name,string directory)
    {
        var element=(FrameworkElement)window.Content;
        element.Measure(new Size(430,double.PositiveInfinity));
        var size=new Size(430,element.DesiredSize.Height);
        element.Arrange(new Rect(size)); element.UpdateLayout();
        var bitmap=new RenderTargetBitmap(430,(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(directory,name));encoder.Save(file);
    }

    private sealed class FakeBackend : IHeadsetAudioBackend
    {
        private static readonly Guid Container=new("793AF0BE-FE46-40B3-B102-D7D5D88B53CA");
        public IReadOnlyList<AudioEndpointInfo> Endpoints=new[] {
            new AudioEndpointInfo("out","耳机（USB Audio Device）",false,Container,5,42,false),
            new AudioEndpointInfo("mic","麦克风（USB Audio Device）",true,Container,4,75,false),
            new AudioEndpointInfo("laptop-mic","麦克风阵列（电脑内置）",true,Guid.NewGuid(),4,60,false) };
        public List<string> Writes=new();
        public bool FailWrites,FailEnumeration;
        public ManualResetEventSlim? VolumeEntered,VolumeRelease,EnumerateEntered,EnumerateRelease;
        public IReadOnlyList<AudioEndpointInfo> Enumerate()
        {
            if (FailEnumeration) throw new InvalidOperationException("Test audio service unavailable");
            var snapshot=Endpoints;
            EnumerateEntered?.Set(); EnumerateRelease?.Wait(TimeSpan.FromSeconds(4));
            return snapshot;
        }
        public AudioActionResult SetVolume(string id,int percent)
        {
            if (FailWrites) return new(false,"测试：Windows 拒绝操作");
            if (!Endpoints.Any(e=>e.Id==id && !e.Capture) || percent is <0 or >100) return new(false,"测试：设备不可用");
            VolumeEntered?.Set(); VolumeRelease?.Wait(TimeSpan.FromSeconds(4));
            Writes.Add($"volume:{id}:{percent}");
            Endpoints=Endpoints.Select(e=>e.Id==id?e with { Volume=percent }:e).ToArray();
            return new(true,$"音量已设为 {percent}%",percent);
        }
        public AudioActionResult SetMicrophoneMute(string id,bool mute)
        {
            if (FailWrites) return new(false,"测试：Windows 拒绝操作");
            if (!Endpoints.Any(e=>e.Id==id && e.Capture)) return new(false,"测试：设备不可用");
            Writes.Add($"mute:{id}:{mute}");
            Endpoints=Endpoints.Select(e=>e.Id==id?e with { Muted=mute }:e).ToArray();
            return new(true,mute?"麦克风已静音":"麦克风已恢复",Muted:mute);
        }
    }
}
