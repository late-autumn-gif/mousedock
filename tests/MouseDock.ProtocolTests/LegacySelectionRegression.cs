using MouseDock.Models;
using MouseDock.Services;

internal static class LegacySelectionRegression
{
    private sealed class Source(params DeviceInfo[] devices) : IBatterySource
    {
        public DeviceInfo[] Devices = devices;
        public string Key => "auto";
        public string Label => "Legacy selection fixture";
        public IEnumerable<DeviceInfo> Read() => Devices;
    }

    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message)
        {
            checks++;
            if (!value) throw new Exception(message);
        }
        var settings = new AppSettings
        {
            SelectedDeviceIds = new() { "hid-3837-100b", "hid-258a-01bb" },
            HiddenDeviceIds = new() { "hid-3837-100b" },
            DisabledRecognitionDeviceIds = new() { "hid-3837-100b" },
            DeviceSelectionInitialized = true
        };
        using (var battery = new BatteryService(settings, new Source(
            new DeviceInfo { Id = "instance-mouse", Detail = "3837:100B", Level = 86 },
            new DeviceInfo { Id = "instance-keyboard", Detail = "258A:01BB", Kind = DeviceKind.Keyboard, Level = 90 }), false))
        {
            battery.PollAsync().GetAwaiter().GetResult();
            Check(battery.VisibleDevices.Single().Id == "instance-keyboard", "An existing keyboard selection must survive instance IDs.");
            var mouse = battery.Devices.Single(d => d.Id == "instance-mouse");
            Check(mouse.Hidden && !mouse.RecognitionEnabled && mouse.Level == -1, "Migration must preserve hidden and disabled choices.");
            Check(settings.SelectedDeviceIds.Contains("instance-mouse") && !settings.SelectedDeviceIds.Contains("hid-3837-100b"),
                "The old selection must be replaced by the unique instance.");
            Check(!battery.Preferences.CanRead("instance-mouse"), "Migrated cancellation must reach subsequent queries.");
        }
        var ambiguous = new AppSettings
        {
            SelectedDeviceIds = new() { "hid-3837-100b" }, DeviceSelectionInitialized = true,
            DisabledRecognitionDeviceIds = new() { "hid-3837-100b" }
        };
        using (var battery = new BatteryService(ambiguous, new Source(
            new DeviceInfo { Id = "instance-first", Detail = "3837:100B" },
            new DeviceInfo { Id = "instance-second", Detail = "3837:100B" }), false))
        {
            battery.PollAsync().GetAwaiter().GetResult();
            Check(battery.VisibleDevices.Count == 0, "An old model-wide choice must not select multiple physical instances.");
            Check(ambiguous.SelectedDeviceIds.SequenceEqual(new[] { "hid-3837-100b" }), "An ambiguous choice must remain recoverable.");
            Check(battery.Devices.All(d => !d.RecognitionEnabled), "Both instances must retain an old model-wide cancellation.");
            battery.Preferences.Enable(battery.Devices.Single(d => d.Id == "instance-first"));
            battery.PollAsync().GetAwaiter().GetResult();
            Check(battery.Devices.Single(d => d.Id == "instance-first").RecognitionEnabled &&
                !battery.Devices.Single(d => d.Id == "instance-second").RecognitionEnabled,
                "Explicitly enabling one instance must leave the other cancelled.");
            Check(battery.Preferences.CanRead("hid-3837-100b") &&
                !battery.Preferences.CanRead("instance-second"), "The obsolete global cancellation must not block per-instance recovery.");
        }
        var selection = new AppSettings { SelectedDeviceIds = new() { "hid-3837-100b" }, DeviceSelectionInitialized = true };
        var pair = new Source(
            new DeviceInfo { Id = "instance-first", Detail = "3837:100B" },
            new DeviceInfo { Id = "instance-second", Detail = "3837:100B" });
        using (var battery = new BatteryService(selection, pair, false))
        {
            battery.PollAsync().GetAwaiter().GetResult();
            battery.SetSelected(battery.Devices.Single(d => d.Id == "instance-first"), true);
            pair.Devices = new[] { pair.Devices[1] };
            battery.PollAsync().GetAwaiter().GetResult();
            Check(!battery.Devices.Single(d => d.Id == "instance-second").Selected,
                "An explicit instance selection must not later migrate to the other receiver.");
        }
        return checks;
    }
}
