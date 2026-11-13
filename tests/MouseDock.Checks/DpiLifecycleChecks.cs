using System.Collections;
using System.Reflection;
using System.Windows.Threading;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
using MouseDock.Views;

internal static class DpiLifecycleChecks
{
    internal static void MirroredReadings()
    {
        var source = new Source();
        using var service = new BatteryService(new AppSettings(), source, false);
        Complete(service.PollAsync());
        var device = service.Devices.Single();
        Require(device.Dpi == 800 && device.DpiCapturedAt != default, "Initial DPI has no scan timestamp.");
        var dpi = new DpiService(new Backend(), () => false, () => service.Preferences, 2000);
        Require(dpi.Supports(device), "A numeric scan without writable stage settings lost its readonly DPI entry.");
        source.Value = 1600;
        Complete(service.PollAsync());
        Require(ReferenceEquals(device, service.Devices.Single()) && device.Dpi == 1600,
            "A repeated scan did not update the existing device's DPI.");
        source.Present = false;
        for (int i = 0; i < 3; i++) Complete(service.PollAsync());
        Require(service.Devices.Count == 0 && !device.Connected && device.Dpi < 0 &&
            device.DpiCapturedAt == default, "Removed device retained a usable DPI reading.");
    }

    internal static void RemovedEditor()
    {
        using var fixture = new AppearanceFixture();
        var device = new DeviceInfo { Id = "instance-dpi-removal", Detail = "373B:11D8" };
        fixture.Battery.Devices.Add(device);
        var dpi = new DpiService(new Backend(), () => false, () => fixture.Battery.Preferences, 2000);
        var window = new DpiWindow(dpi, device, MouseDock.App.Theme, false);
        var windows = (IDictionary)typeof(MouseDock.App).GetField("_dpiWindows",
            BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        windows.Add(device.Id, window);
        bool closed = false;
        window.Closed += (_, _) => { closed = true; windows.Remove(device.Id); };
        try
        {
            typeof(MouseDock.App).GetMethod("WatchDpiDevices", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            Complete(window.RefreshAsync());
            fixture.Battery.Devices.Remove(device);
            Require(closed && !windows.Contains(device.Id), "Removing a device left its DPI editor open.");
        }
        finally { window.Close(); windows.Remove(device.Id); }
    }

    private static void Complete(Task task)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(5), DispatcherPriority.Background,
            (_, _) => { if (task.IsCompleted || DateTime.UtcNow > deadline) frame.Continue = false; },
            Dispatcher.CurrentDispatcher);
        try { if (!task.IsCompleted) Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
        Require(task.IsCompleted, "DPI lifecycle fixture timed out.");
        task.GetAwaiter().GetResult();
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private sealed class Source : IBatterySource
    {
        internal int Value = 800;
        internal bool Present = true;
        private readonly AutoDetectSource detected = new();
        public string Key => "dpi-fixture";
        public string Label => Key;
        public IEnumerable<DeviceInfo> Read() => detected.UpdateFromDetection(Present
            ? [new DetectedDevice { Id = "instance-dpi-mirror", Name = "MCHOSE A7 V2",
                Kind = DeviceKind.Mouse, Detail = "3837:100B", Dpi = Value }]
            : []);
    }
    private sealed class Backend : IDpiBackend
    {
        public DpiSnapshot Read(string instance, ushort vid, ushort pid, Action guard)
        {
            guard();
            return new DpiSnapshot(DpiProfiles.Find(vid, pid)!, 0,
                new[] { new DpiStage(800, 800) }, "fixture", 10, 42000, 10).Bind(instance, "fixture");
        }
        public DpiSnapshot Apply(DpiSnapshot before, int stage, int? value, Action guard) =>
            throw new InvalidOperationException("Fixture does not write devices.");
    }
}
