using System.Reflection;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class ConnectionSwitchChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void ReceiverOffline()
    {
        var protocol = typeof(AppSettings).Assembly.GetType("MouseDock.Services.Detection.RkKeyboardProtocol")!;
        var read = protocol.GetMethod("ReadReceiverAsync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Require(read != null, "Receiver connection must be checked before querying a sleeping keyboard.");
        Func<byte[], Task<byte[]>> exchange = request =>
        {
            Require(request[1] == 7, "Offline receiver must not query keyboard identity or battery.");
            return Task.FromResult(Convert.FromHexString("130701000100000000000000000000000000001C"));
        };
        var result = ((Task<DetectedDevice>)read!.Invoke(null, [(ushort)0x258A, (ushort)0x01BB, exchange])!)
            .GetAwaiter().GetResult();
        Require(result != null && !result.Connected && result.Level == -1 && !result.Charging,
            "Confirmed offline receiver must not fall back to an online HID device.");
    }

    public static void ReceiverOnline()
    {
        var protocol = typeof(AppSettings).Assembly.GetType("MouseDock.Services.Detection.RkKeyboardProtocol")!;
        var read = protocol.GetMethod("ReadReceiverAsync", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Require(read != null, "Missing receiver connection-aware reading.");
        foreach (bool recognized in new[] { true, false })
        {
            Func<byte[], Task<byte[]>> exchange = request => Task.FromResult(Convert.FromHexString(request[1] switch
            {
                7 => "130701000101000000000000000000000000001D",
                5 when recognized => "13050100060A000000004600000000000000006F",
                5 => "13050100060A0000000020000000000000000049",
                0x4A when recognized => "134A0100026401000000000000000000000000C5",
                _ => throw new Exception("Unrecognized keyboard must not receive battery queries.")
            }));
            var result = ((Task<DetectedDevice>)read!.Invoke(null, [(ushort)0x258A, (ushort)0x01BB, exchange])!)
                .GetAwaiter().GetResult();
            Require(recognized ? result is { Connected: true, Level: 100, Kind: DeviceKind.Keyboard } : result == null,
                "Connection-first queries lost model verification or battery decoding.");
        }
    }

    public static void OfflineMerge()
    {
        var merge = typeof(AutoDetectSource).GetMethod("MergeGroup", BindingFlags.NonPublic | BindingFlags.Static)!;
        var devices = new List<DetectedDevice>
        {
            new() { Id = "receiver", Connected = false, Level = 100, Charging = true, Transport = "2.4G" },
            new() { Id = "bluetooth", Connected = true, Level = -1, Transport = "蓝牙 LE" }
        };
        var merged = (DetectedDevice)merge.Invoke(null, [devices])!;
        Require(merged.Connected && merged.Level == -1 && !merged.Charging && merged.Id == "bluetooth",
            "Disconnected channel supplied old battery or charging to the active channel.");
    }

    public static void OfflinePropagation()
    {
        var update = typeof(AutoDetectSource).GetMethod("UpdateFromDetection", BindingFlags.NonPublic | BindingFlags.Instance);
        Require(update != null, "Detection must preserve explicit disconnection evidence.");
        var source = new AutoDetectSource();
        var input = new List<DetectedDevice> { new() { Id = "receiver", Connected = false, Level = -1 } };
        var snapshot = ((IEnumerable<DeviceInfo>)update!.Invoke(source, [input])!).ToArray();
        Require(snapshot.Length == 1 && !snapshot[0].Connected,
            "Confirmed offline receiver was discarded before the UI could remove its cached reading.");
    }

    public static void ImmediateRemoval()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        using var battery = new BatteryService(new AppSettings { Provider = "json", JsonPath = "missing-switch-fixture.json" });
        var initialized = new TaskCompletionSource();
        EventHandler onReady = (_, _) => initialized.TrySetResult();
        battery.Refreshed += onReady;
        Wait(initialized.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        battery.Refreshed -= onReady;
        var source = new SwitchSource();
        typeof(BatteryService).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(battery, source);
        Wait(battery.PollAsync());
        Wait(battery.PollAsync());
        Require(battery.Devices.Any(d => d.Id == "receiver" && d.Level == 100), "Missing online receiver fixture.");
        source.Bluetooth = true;
        Wait(battery.PollAsync());
        Require(battery.Devices.Count == 1 && battery.Devices[0].Id == "bluetooth" && battery.Devices[0].Level == 63,
            "Explicit disconnect left the old 2.4G reading visible after the first refresh.");
        source.Bluetooth = false;
        Wait(battery.PollAsync());
        Require(battery.Devices.Count == 1 && battery.Devices[0].Id == "receiver",
            "Switching back retained the old Bluetooth channel.");
    }

    private sealed class SwitchSource : IBatterySource
    {
        public string Key => "switch";
        public string Label => "switch";
        public bool Bluetooth;
        public IEnumerable<DeviceInfo> Read() =>
        [
            new() { Id = "receiver", Connected = !Bluetooth, Level = Bluetooth ? -1 : 100 },
            new() { Id = "bluetooth", Connected = Bluetooth, Level = Bluetooth ? 63 : -1 }
        ];
    }

    private static void Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false),
                TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
}
