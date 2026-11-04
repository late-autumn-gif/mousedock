using System.Reflection;
using System.Runtime.InteropServices;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

// Offline protocol regression: these tests never enumerate or communicate with a device.
if (args.Length > 0 && args[0] == "--verify-neon75")
{
    Console.WriteLine($"PASS {Neon75Regression.Run()} Neon75 protocol checks; no hardware I/O.");
    return;
}
if (args.Length > 0 && args[0] == "--inspect-neon75-battery")
{
    for (int i=0;i<3;i++)
    {
        int level=VendorProtocol.TryReadNeon75(0x391D,0x2A05,true,out bool charging,out bool stale);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { time=DateTimeOffset.Now,level,charging,stale }));
        Thread.Sleep(300);
    }
    return;
}
if (args.Length > 1 && args[0] == "--verify-device-management")
{
    try { Console.WriteLine($"PASS {DeviceManagementRegression.Run(args[1])} device selection/context-menu/persistence checks; no hardware I/O."); }
    catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
    return;
}
if (args.Length > 0 && args[0] == "--verify-native-io")
{
    Console.WriteLine($"PASS {NativeIoRegression.Run()} real Windows overlapped pipe checks; no hardware I/O.");
    return;
}
if (args.Length > 0 && args[0] == "--inspect-sc580-battery")
{
    for (int i = 0; i < 5; i++)
    {
        int level = VendorProtocol.TryReadBattery(0x089D, 0x062F, out bool charging);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            time = DateTimeOffset.Now, model = VendorProtocol.IdentityFor(0x089D, 0x062F),
            level, charging, stale = VendorProtocol.IdentifyStale
        }));
        Thread.Sleep(500);
    }
    return;
}
if (args.Length > 1 && args[0] == "--verify-charging-ui")
{
    Console.WriteLine($"PASS {ChargingVisualRegression.Run(args[1])} charging animation/resource checks; no hardware I/O.");
    return;
}
if (args.Length > 0 && args[0] == "--inspect-usb-headset")
{
    foreach (var node in HidProbe.Enumerate(0x4C4A, 0x4155))
    {
        bool accepted = HidDiscovery.TryDeviceKind(node.Vid, node.Pid, node.Caps.UsagePage, node.Caps.Usage, out var kind);
        Console.WriteLine($"{node.Vid:X4}:{node.Pid:X4} {node.Product} usage={node.Caps.UsagePage:X4}/{node.Caps.Usage:X4} in/out/feature={node.Caps.InputReportByteLength}/{node.Caps.OutputReportByteLength}/{node.Caps.FeatureReportByteLength} accepted={accepted} kind={kind}");
    }
    return;
}
if (args.Length > 0 && args[0] == "--inspect-device-filter")
{
    foreach (string instanceId in args.Skip(1))
        Console.WriteLine(instanceId + " => " + (HidDiscovery.InspectExclusion(instanceId) ?? "保留"));
    return;
}
int assertions = MchoseA7V2Regression.Run();
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new Exception(message);
}

byte[] Modern(int level = 42, byte charge = 1, ushort model = 0x4030, byte flags = 1)
{
    var packet = new byte[64];
    packet[0] = 0x4D; packet[1] = 1; packet[2] = flags; packet[3] = 13; packet[5] = 9;
    packet[8] = 0x37; packet[9] = 0x38;
    packet[10] = (byte)model; packet[11] = (byte)(model >> 8);
    packet[19] = charge; packet[20] = (byte)level;
    FixChecksum(packet);
    return packet;
}
void FixChecksum(byte[] packet)
{
    byte sum = 0;
    for (int i = 2; i < 8 + packet[3]; i++) sum ^= packet[i];
    packet[8 + packet[3]] = sum;
}
bool Parse(byte[] packet) => VendorProtocol.TryParseMchoseModern(packet, 0x3837, 0x1018, out _, out _);

var request = VendorProtocol.BuildMchoseModernRequest();
Check(request.Length == 64, "Modern request must have the descriptor's exact report length");
Check(request.Take(9).SequenceEqual(new byte[] { 0x4D, 1, 1, 0, 0, 9, 0, 0, 8 }), "Official 0900 request bytes");
Check(request.Skip(9).All(b => b == 0), "Query has no configuration payload");
Check(!Parse(request), "Do not treat an echoed request as a battery response");
Check(!Parse(new byte[64]), "Do not treat empty data as zero battery");
foreach (ushort model in new ushort[] { 0x1014, 0x1018, 0x4030, 0x4031, 0x4032, 0x4033, 0x403D, 0x403A, 0x403F })
{
    Check(VendorProtocol.TryParseMchoseModern(Modern(model: model), 0x3837, 0x1018, out int level, out bool charge)
        && level == 42 && charge, $"Receiver PID must accept official A7 V3 model {model:X4}");
}
foreach (int percent in new[] { 0, 1, 99, 100 })
{
    Check(VendorProtocol.TryParseMchoseModern(Modern(percent, 0), 0x3837, 0x1014, out int level, out bool charge)
        && level == percent && !charge, $"Valid endpoint percentage {percent}");
}
Check(!Parse(Modern(101)), "Reject battery above 100");
Check(!Parse(Modern(255)), "Reject battery sentinel");
Check(!Parse(Modern(charge: 2)), "Reject unknown charging state");
Check(!Parse(Modern(model: 0x7777)), "Reject another model's payload");
Check(!VendorProtocol.TryParseMchoseModern(Modern(), 0x3837, 0x101C, out _, out _), "No unverified receiver commands");
Check(!VendorProtocol.TryParseMchoseModern(Modern(), 0x1234, 0x1018, out _, out _), "No cross-vendor replies");
foreach (int size in new[] { 0, 8, 20, 21, 61, 63, 65 })
    Check(!Parse(new byte[size]), $"Reject truncated/wrong HID report length {size}");
foreach (int index in new[] { 0, 1, 4, 5, 8, 9, 20, 21 })
{
    var bad = Modern(); bad[index] ^= 0x80;
    Check(!Parse(bad), $"Reject corrupted report byte {index}");
}
foreach (byte length in new byte[] { 0, 12, 56, 255 })
{
    var bad = Modern(); bad[3] = length;
    Check(!Parse(bad), $"Reject invalid payload length {length}");
}
var noChecksum = Modern(flags: 0); noChecksum[21] ^= 0xFF;
Check(Parse(noChecksum), "Official checksum is optional when flags bit zero is clear");
var extended = Modern(); extended[3] = 55; FixChecksum(extended);
Check(Parse(extended), "Accept a complete maximum-size device-info payload");

var body = new byte[19]; body[0] = 0x53; body[1] = 0x52; body[8] = 9; body[9] = 40; body[10] = 1;
byte[] Legacy() => new byte[] { 0x11, 0xF9 }.Concat(body.Select(b => (byte)(b ^ 0xFF))).ToArray();
Check(VendorProtocol.TryParseMchoseLegacy(Legacy(), 0x5253, 0x1021, out int legacyLevel, out bool legacyCharge)
    && legacyLevel == 40 && legacyCharge, "Preserve legacy A7 parser");
body[8] = 1;
Check(!VendorProtocol.TryParseMchoseLegacy(Legacy(), 0x5253, 0x1021, out _, out _), "Sleeping legacy mouse must remain unknown");
body[8] = 9; body[9] = 0;
Check(VendorProtocol.TryParseMchoseLegacy(Legacy(), 0x5253, 0x1021, out legacyLevel, out _) && legacyLevel == 0, "Accept valid legacy 0 percent");
Check(!VendorProtocol.TryParseMchoseLegacy(Legacy(), 0x3837, 0x1018, out _, out _), "Never dispatch modern A7 V3 to legacy protocol");

var merged = AutoDetectSource.MergeGroup(new List<DetectedDevice>
{
    new() { Id = "hid-test", Level = 42, Charging = true, Transport = "2.4G" },
    new() { Id = "bt-test", Level = -1, Transport = "蓝牙" }
});
Check(merged.Level == 42 && merged.Charging, "Merging must preserve the valid reading's charging state");
var source = new AutoDetectSource();
List<DetectedDevice> Detection(int level, bool charging) => new()
{
    new() { Id = "hid-3837-1018", Name = "MCHOSE A7 V3", Kind = DeviceKind.Mouse,
        Transport = "2.4G", Level = level, Charging = charging }
};
var first = source.UpdateFromDetection(Detection(42, true)).Single();
var missed = source.UpdateFromDetection(Detection(-1, false)).Single();
Check(!ReferenceEquals(first, missed), "Polling must return independent device snapshots");
Check(first.Level == 42 && first.Charging && missed.Level == -1, "Timeout must not erase the UI's last valid reading before fallback");
var recovered = source.UpdateFromDetection(Detection(43, false)).Single();
Check(recovered.Level == 43 && !recovered.Charging && first.Level == 42, "Recovery updates a new snapshot without mutating prior results");
var bucket = new HidDiscovery.Bucket { Vid = 0x3837, Pid = 0x1018, Product = "MCHOSE A7 V3", Kind = DeviceKind.Mouse,
    Battery = 0, Charging = true, BatterySource = "MHUB v2" };
var device = bucket.ToDevice();
Check(device.Level == 0 && device.Charging && device.Note == "MHUB v2", "Discovery must preserve zero battery, charging and protocol source");

foreach (var type in new[] { typeof(HidDiscovery), typeof(HidBatterySource), typeof(VendorProtocol), typeof(HidCapture), typeof(HidProbe) })
{
    var caps = type.GetNestedType("HIDP_CAPS", BindingFlags.NonPublic | BindingFlags.Public)!;
    Check(Marshal.SizeOf(caps) == 64, $"{type.Name}: native HIDP_CAPS must be 64 bytes");
}

Check(BtSystemBattery.TryParseAddress("84:9D:4B:36:69:C7", out ulong address) && address == 0x849D4B3669C7,
    "Report Bluetooth identity matches system device address");
Check(!BtSystemBattery.TryParseAddress("ROSE Ceramics U", out _), "Names are not device addresses");

Check(HidDiscovery.ExclusionReason(new[] { (@"HID\VID_093A&PID_3807", "hidi2c") }) != null, "Exclude actual I2C service");
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\VID_093A&PID_3807", ""), (@"ACPI\PNP0C50", "") }) != null, "Exclude ACPI input ancestry");
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\VID_045E&PID_0000", "") }) == null, "Keep unknown 045E:0000 without virtual evidence");
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\VID_045E&PID_0000", ""), (@"ROOT\VHF\0000", "vhf") }) != null, "Filter virtual HID using actual PnP evidence");
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\VID_045E&PID_0000", "HidUsb"), (@"USB\VID_045E&PID_0000", ""), (@"ACPI\PNP0A08", "") }) == null, "Do not filter USB due to upstream ACPI controller");
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\VID_0000&PID_0000", ""), (@"USB\VID_0000&PID_0000", "") }) == null, "Preserve unknown USB IDs");
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\VID_06CB&PID_1234", "HidUsb"), (@"USB\VID_06CB&PID_1234", "") }) == null, "No vendor-wide blacklist");
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\VID_048D&PID_1234", "HidUsb"), (@"USB\VID_048D&PID_1234", "") }) == null, "Preserve ITE external input");
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\VID_045E&PID_0000", ""), (@"BTHLEDEVICE\ABC", ""), (@"ACPI\PNP0A08", "") }) == null, "Preserve Bluetooth despite upstream ACPI");
assertions += BluetoothRegression.Run();
assertions += HfpRegression.Run();
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\TencentMyAppsHidBus&Col01", "mouhid") }) != null, "Exclude Tencent virtual mouse by actual bus identity");
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\TencentMyAppsHidBus&Col03", "kbdhid") }) != null, "Exclude Tencent virtual keyboard");
Check(HidDiscovery.ExclusionReason(new[] { (@"ROOT\MyAppsVirtualBus\0000", "MyAppsHidBus") }) != null, "Exclude virtual bus ancestor");
Check(HidDiscovery.ExclusionReason(new[] { (@"USB\VID_048D&PID_C992", "HidUsb") }, true) != null, "Exclude ITE with confirmed machine-container property");
Check(HidDiscovery.ExclusionReason(new[] { (@"USB\VID_048D&PID_C992", "HidUsb") }, false) == null, "ITE identity alone must not blacklist external devices");
Check(HidDiscovery.ExclusionReason(new[] { (@"USB\VID_089D&PID_062F", "HidUsb") }) == null, "Preserve external receiver");
Check(HidDiscovery.TryDeviceKind(0x4C4A, 0x4155, 0x0C, 1, out var headsetKind) && headsetKind == DeviceKind.Headset, "Recognize ZG2 Consumer Control as known USB headset");
Check(!HidDiscovery.TryDeviceKind(0x4C4A, 0x4156, 0x0C, 1, out _), "Do not infer another product is the same headset");
Check(!HidDiscovery.TryDeviceKind(0x1234, 0x4155, 0x0C, 1, out _), "Do not accept arbitrary consumer controls as headsets");
Check(!HidDiscovery.TryDeviceKind(0x4C4A, 0x4155, 0xFF00, 1, out _), "Do not classify arbitrary vendor interfaces as headsets");
var usbHeadset = new HidDiscovery.Bucket { Vid = 0x4C4A, Pid = 0x4155, Product = "ZhuAudio ZG2", Kind = DeviceKind.Headset };
usbHeadset.BatteryFailures.Add("电量协议尚未适配");
var headsetDevice = usbHeadset.ToDevice();
Check(headsetDevice.Kind == DeviceKind.Headset && headsetDevice.Name.Contains("竹林鸟玄武") && headsetDevice.Transport == "USB", "Display USB headset by confirmed identity");
Check(headsetDevice.Level == -1 && headsetDevice.Note.Contains("尚未适配"), "Device discovery must not fabricate headset battery");
var chargeDevice = new DeviceInfo { Connected = true, Level = 42 };
var changedProperties = new List<string>();
chargeDevice.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);
chargeDevice.Charging = true;
Check(chargeDevice.IsChargingActive && chargeDevice.StatusText.Contains("充电中"), "A valid charging flag enables the indicator");
Check(changedProperties.Contains(nameof(DeviceInfo.IsChargingActive)) && changedProperties.Contains(nameof(DeviceInfo.StatusText)), "Charge updates notify derived UI bindings");
chargeDevice.IsStale = true;
Check(!chargeDevice.IsChargingActive && chargeDevice.StatusText.Contains("待刷新"), "Stale charging data must stop the animation");
chargeDevice.IsStale = false; chargeDevice.Connected = false;
Check(!chargeDevice.IsChargingActive, "Disconnected devices must not animate");
chargeDevice.Connected = true; chargeDevice.Charging = false;
Check(!chargeDevice.IsChargingActive, "Unplugged charging state stops the animation");
chargeDevice.Level = 100;
Check(!chargeDevice.IsChargingActive, "100 percent is not proof of charging");
chargeDevice.Level = 99;
Check(!chargeDevice.IsChargingActive, "A percentage change is not proof of charging");
chargeDevice.Level = -1; chargeDevice.Charging = true;
Check(chargeDevice.IsChargingActive && chargeDevice.LevelText == "—", "Known charging can be shown without inventing a battery percentage");

var identify = new byte[] { 0x30, 0, 1, 0x0B, 0x4D, 0x31, 0x33, 0x37, 1, 0x16, 0x11, 0xAA, 0, 65, 1, 0 };
Check(VendorProtocol.TryParseIdentifyBattery(identify, out int identifyLevel, out bool identifyCharging) && identifyLevel == 65 && !identifyCharging, "Captured M137 Identify shape gives level and noncharging state");
identify[12] = 1;
Check(VendorProtocol.TryParseIdentifyBattery(identify, out identifyLevel, out identifyCharging) && identifyLevel == 65 && identifyCharging, "Identify charging byte is propagated");
Check(VendorProtocol.TryParseIdentifyBattery(new byte[] { 0 }.Concat(identify).ToArray(), out identifyLevel, out identifyCharging) && identifyCharging, "Windows report-ID alignment preserves charging");
identify[13] = 0;
Check(VendorProtocol.TryParseIdentifyBattery(identify, out identifyLevel, out _) && identifyLevel == 0, "A valid Identify can report 0 percent");
identify[1] = 2;
Check(!VendorProtocol.TryParseIdentifyBattery(identify, out _, out _), "Sleep status must not be interpreted as new charge state");
Check(!VendorProtocol.TryParseIdentifyBattery(new byte[33], out _, out _), "Empty input is not a real charge response");
identify[1] = 0; identify[13] = 101;
Check(!VendorProtocol.TryParseIdentifyBattery(identify, out _, out _), "Reject invalid Identify percentage");
Check(!VendorProtocol.TryParseIdentifyBattery(identify[..10], out _, out _), "Reject truncated Identify before reading charge byte");
identify[13] = 65; identify[14] = 2;
Check(!VendorProtocol.TryParseIdentifyBattery(identify, out _, out _), "Invalid connection flags are not a fresh charging reading");
var chargeMerge = AutoDetectSource.MergeGroup(new List<DetectedDevice>
{
    new() { Id = "fresh", Name = "MCHOSE A7", Level = 42, Charging = true, Transport = "2.4G" },
    new() { Id = "cache", Name = "MCHOSE A7", Level = 42, Charging = false, IsStale = true, Transport = "蓝牙" }
});
Check(chargeMerge.Charging && !chargeMerge.IsStale, "A stale alternate transport cannot override a fresh charge state");
var replaceable = source.UpdateFromDetection(new List<DetectedDevice> { new() { Id = "logi-test", Name = "Logitech G304", Level = 90, Charging = true } }).Single();
Check(!replaceable.Charging, "A replaceable-battery mouse must never show a charge animation");
Check(!VendorMap.IsReplaceableBattery("Logitech G502 Hero Wireless") && !VendorMap.IsReplaceableBattery("Logitech G903 Hero"), "Short wired-model names must not suppress another model's charging state");
Check(!VendorMap.IsReplaceableBattery("Logitech G700") && !VendorMap.IsReplaceableBattery("Logitech G700s"), "USB-rechargeable models must not be excluded merely because their rechargeable battery is removable");
Console.WriteLine($"PASS {assertions} offline protocol/integration assertions; no hardware I/O.");
