using System.Runtime.InteropServices;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;
using Channel = MouseDock.Services.Detection.HidDiscovery.StandardBatteryChannel;
using ValueCaps = MouseDock.Services.Detection.HidDiscovery.HIDP_VALUE_CAPS;

internal static class StandardBatteryRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string description)
        {
            if (!ok) throw new Exception(description);
            checks++;
        }

        var caps = new ValueCaps
        {
            UsagePage = 6, UsageMin = 0x20, ReportId = 7, LinkCollection = 2,
            BitField = 2, BitSize = 8, ReportCount = 1, LogicalMin = 0, LogicalMax = 200
        };
        Check(Marshal.SizeOf<ValueCaps>() == 72 && Marshal.OffsetOf<ValueCaps>(nameof(ValueCaps.UsageMin)).ToInt32() == 56,
            "HID parser receives the Windows native value capability layout");
        var field = HidDiscovery.FindStandardBatteryField(new[] { caps }, 5);
        Check(field is { ReportId: 7, LinkCollection: 2, ReportLength: 5 },
            "A numbered Feature report exposes Battery Strength in a nested link collection");
        Check(field?.CreateRequest().SequenceEqual(new byte[] { 7, 0, 0, 0, 0 }) == true,
            "GET_FEATURE uses the declared report ID and native report length");
        Check(field?.Decode(new byte[] { 7, 250, 0, 0, 0 }, 101) == 51,
            "HID parser's field value is normalized over its declared range");
        Check(field?.Decode(new byte[] { 7, 250, 0, 0, 0 }, 0) == 0 &&
              field?.Decode(new byte[] { 7, 250, 0, 0, 0 }, 200) == 100,
            "Descriptor minimum and maximum report empty and full");
        Check(field?.Decode(new byte[] { 8, 0, 0, 0, 0 }, 70) == -1 &&
              field?.Decode(new byte[] { 7, 0, 0, 0 }, 70) == -1 &&
              field?.Decode(new byte[] { 7, 0, 0, 0, 0 }, 201) == -1,
            "Wrong report ID, incorrect native length and out-of-range values stay unknown");
        Check(HidDiscovery.FindStandardBatteryField(new[] { caps, caps }, 5) == null,
            "Multiple declared Battery Strength fields are ambiguous");
        var privateCaps = caps;
        privateCaps.UsagePage = 0xFF00;
        Check(HidDiscovery.FindStandardBatteryField(new[] { privateCaps }, 5) == null,
            "A private field using the same usage number cannot trigger a read");
        var arrayCaps = caps;
        arrayCaps.ReportCount = 2;
        Check(HidDiscovery.FindStandardBatteryField(new[] { arrayCaps }, 5) == null,
            "An array cannot stand in for a scalar battery field");
        var malformed = caps;
        malformed.LogicalMax = 300;
        Check(HidDiscovery.FindStandardBatteryField(new[] { malformed }, 5) == null,
            "Reject logical ranges that cannot fit the declared field");
        var relative = caps;
        relative.BitField = 6;
        Check(HidDiscovery.FindStandardBatteryField(new[] { relative }, 5) == null,
            "A relative value does not establish charge level");
        var rangeCaps = caps;
        rangeCaps.IsRange = 1;
        rangeCaps.UsageMin = 0x1F;
        rangeCaps.UsageMax = 0x21;
        Check(HidDiscovery.FindStandardBatteryField(new[] { rangeCaps }, 5) == null,
            "A range encompassing multiple usages cannot stand in for a scalar battery field");
        var signedCaps = caps;
        signedCaps.LogicalMin = -100;
        signedCaps.LogicalMax = 100;
        var signed = HidDiscovery.FindStandardBatteryField(new[] { signedCaps }, 5);
        Check(signed?.Decode(new byte[] { 7, 0, 0, 0, 0 }, 0xCE) == 25,
            "Signed descriptor values are sign-extended before normalization");
        Check(signed?.Decode(new byte[] { 7, 0, 0, 0, 0 }, 0x80) == -1,
            "A signed out-of-range or null value stays unknown");

        const string path = "hid-mouse-a";
        const string batteryPath = "hid-aux-a";
        const string otherPath = "hid-aux-b";
        const ushort vid = 0x6ABC, pid = 0x1234, revision = 0x0102;
        InstanceScope.BeginScan();
        string owner = InstanceScope.Register(path, new[] { (@"USB\VID_6ABC&PID_1234\A", "usb") });
        InstanceScope.Register(batteryPath, new[] { (@"USB\VID_6ABC&PID_1234\A", "usb") });
        string other = InstanceScope.Register(otherPath, new[] { (@"USB\VID_6ABC&PID_1234\B", "usb") });
        string unscoped = InstanceScope.Register("no-physical-parent", Array.Empty<(string, string)>());
        string bluetooth = InstanceScope.Register("ble-on-usb-adapter", new[]
        {
            (@"BTHLEDEVICE\{00001812}", "hidbthle"),
            (@"USB\VID_6ABC&PID_1234\C", "usb")
        });
        var bucket = new HidDiscovery.Bucket
        {
            InstanceId = owner, Vid = vid, Pid = pid, Revision = revision, Product = "Example Mouse",
            Kind = DeviceKind.Mouse
        };
        bucket.Counts.Add(DeviceKind.Mouse, 1);
        var direct = new Channel(path, vid, pid, revision, 1, 2, field.GetValueOrDefault());
        var auxiliary = new Channel(batteryPath, vid, pid, revision, 6, 1, field.GetValueOrDefault());
        var differentInstance = auxiliary with { Path = otherPath };
        Check(InstanceScope.MatchesUsbInstance(owner, path, vid, pid) &&
              !InstanceScope.MatchesUsbInstance(unscoped, "no-physical-parent", vid, pid),
            "Require a registered physical USB parent for generic HID battery reads");
        Check(!InstanceScope.MatchesUsbInstance(owner, path, vid, 0x2345),
            "Native HID attributes must agree with the physical USB VID/PID");
        Check(!InstanceScope.MatchesUsbInstance(bluetooth, "ble-on-usb-adapter", vid, pid),
            "A Bluetooth adapter's USB ancestor cannot identify its BLE peripherals");
        int calls = 0;
        Channel? observed = null;
        int Read(Channel channel)
        {
            calls++;
            observed = channel;
            return 46;
        }
        void Query(bool safeMode, DevicePreferences? preferences, params Channel[] channels)
        {
            calls = 0;
            observed = null;
            HidDiscovery.QueryStandardBattery(bucket, channels, safeMode, preferences, Read);
        }
        Query(false, null, differentInstance);
        Check(calls == 0 && bucket.Battery == -1 && other != owner, "Same VID/PID on another USB instance is never queried");
        Query(false, null, direct with { Pid = 0x2345 });
        Check(calls == 0 && bucket.Battery == -1, "A sibling with different hardware ID is not queried");
        Query(false, null, direct with { Revision = 0x0103 });
        Check(calls == 0 && bucket.Battery == -1, "A sibling with different revision is not queried");
        Query(true, null, direct);
        Check(calls == 0 && bucket.Battery == -1, "Safe mode skips the generic HID transaction");
        Query(false, new DevicePreferences(new AppSettings { DisabledRecognitionDeviceIds = new() { owner } }), direct);
        Check(calls == 0 && bucket.Battery == -1, "Disabled recognition skips the generic HID transaction");
        Query(false, null, direct, auxiliary);
        Check(calls == 0 && bucket.Battery == -1, "Two possible reports on one physical device are not guessed");
        Query(false, null, auxiliary with { UsagePage = 0xFF00 });
        Check(calls == 0 && bucket.Battery == -1, "A separate vendor top-level collection is not a standard auxiliary path");
        bucket.Counts.Add(DeviceKind.Keyboard, 1);
        Query(false, null, auxiliary);
        Check(calls == 0 && bucket.Battery == -1, "An auxiliary report cannot identify a device behind multiple input functions");
        bucket.Counts.Remove(DeviceKind.Keyboard);
        Query(false, null, auxiliary, differentInstance);
        var result = bucket.ToDevice();
        Check(calls == 1 && observed == auxiliary && result.Id == owner && result.Level == 46 &&
              result.Kind == DeviceKind.Mouse && !result.Charging && !result.PeripheralConfirmed &&
              result.Note.Contains("Battery Strength", StringComparison.Ordinal),
            "Normal discovery publishes the same-instance auxiliary battery without asserting pairing or charging");
        Query(false, null, direct);
        Check(calls == 0 && bucket.Battery == 46, "A valid existing battery reading is preserved");
        bucket.Battery = -1;
        const string vendorPath = "hid-known-mouse";
        bucket.Vid = 0x046D;
        bucket.InstanceId = InstanceScope.Register(vendorPath, new[] { (@"USB\VID_046D&PID_1234\A", "usb") });
        Query(false, null, direct with { Path = vendorPath, Vid = 0x046D });
        Check(calls == 0 && bucket.Battery == -1, "Known vendor routes retain ownership");
        Check(BluetoothDiscovery.DecodeBatteryLevel(new byte[] { 0 }) == 0 &&
              BluetoothDiscovery.DecodeBatteryLevel(new byte[] { 100 }) == 100,
            "BLE Battery Level supports both byte boundaries");
        Check(BluetoothDiscovery.DecodeBatteryLevel(new byte[] { 101 }) == -1 &&
              BluetoothDiscovery.DecodeBatteryLevel(new byte[] { 50, 90 }) == -1 &&
              BluetoothDiscovery.DecodeBatteryLevel(Array.Empty<byte>()) == -1,
            "BLE Battery Level rejects out-of-range bytes and malformed characteristic lengths");
        return checks;
    }
}
