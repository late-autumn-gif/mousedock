using System.Reflection;
using System.Runtime.InteropServices;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

// Offline protocol regression: these tests never enumerate or communicate with a device.
if(args.Length>2&&args[0]=="--audit-support-catalogue"){SupportAuditRegression.Export(args[1],args[2]);return;}
if(args.Length>1 && args[0]=="--preview-reference-styles"){InphicStylesRegression.Preview(args[1]);return;}
if(args.Length>1&&args[0]=="--verify-published-update")
{
    string json;
    if(args[1].StartsWith("https://",StringComparison.Ordinal))
    {
        using var client=new System.Net.Http.HttpClient{Timeout=TimeSpan.FromSeconds(20)};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MouseDock-Release-Verification/1.1.1");
        json=await client.GetStringAsync(args[1]);
    }
    else json=System.IO.File.ReadAllText(args[1]);
    var update=UpdateService.ParseRelease(json,"1.1.0");
    if(update?.Version!="1.1.1" || update.AssetName!="MouseDock-v1.1.1-win-x64.zip")throw new Exception("Published update metadata mismatch");
    Console.WriteLine("PASS published stable v1.1.1 update and exact Windows x64 program asset selected.");return;
}
// Explicit inspection modes below are opt-in and report their hardware I/O.
if (args.Length > 0 && args[0] == "--inspect-aula-battery")
{
    var devices = HidDiscovery.Enumerate();
    foreach (var d in devices.Where(d => d.Detail == "089D:062F" || VendorProtocol.IsAulaSc630(d.RawName)))
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { d.Name,d.RawName,d.ConfirmedModel,d.Level,d.Charging,d.PeripheralConfirmed,d.IsStale,d.Note }));
    foreach (var line in HidDiscovery.LastDiagnostics.Where(s => s.Contains("狼蛛路线=") || s.Contains("狼蛛边界：")))
        Console.WriteLine(line);
    return;
}
if (args.Length > 1 && args[0] == "--verify-headset-audio")
{
    Console.WriteLine($"PASS {HeadsetAudioRegression.RunUi(args[1])} headset UI/control checks; simulated backend, no system audio writes.");
    return;
}
if (args.Length > 0 && args[0] == "--inspect-audio-endpoints")
{
    var endpoints = new WindowsHeadsetAudioBackend().Enumerate();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(endpoints.Select(e => new {
        e.Name, e.Capture, e.FormFactor, e.IsHeadset, HasContainer=e.Container != Guid.Empty,
        HasVolume=e.Volume.HasValue, HasMute=e.Muted.HasValue, e.Error
    })));
    return;
}
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
if (args.Length > 0 && args[0] == "--inspect-inphic-battery")
{
    ushort vid = args.Length > 2 ? Convert.ToUInt16(args[1], 16) : (ushort)0x1D57;
    ushort pid = args.Length > 2 ? Convert.ToUInt16(args[2], 16) : (ushort)0xFA60;
    for (int i = 0; i < 5; i++)
    {
        int level = VendorProtocol.TryReadInphic(vid, pid, out bool charging);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            time = DateTimeOffset.Now, target = $"{vid:X4}:{pid:X4}", level, charging,
            stale = VendorProtocol.IdentifyStale
        }));
        Thread.Sleep(500);
    }
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
int assertions = ReportedX23SeRegression.Run() + HeadsetAudioRegression.RunPolicy() + ReferenceBatteryRegression.Run() + IdentityBoundaryRegression.Run() + MchoseA7V2Regression.Run() + ReceiverClassificationRegression.Run();
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
// 罗技 G HUB 会挂两条没有硬件的 LGHUBDEVICE 虚拟 HID（C231 鼠标 / C232 键盘），
// 它们带着 0x01 用法混进面板，会白占两台设备配额。真实接收器走 USB 那条链，不能误伤。
Check(HidDiscovery.ExclusionReason(new[] {
        (@"HID\VID_046D&PID_C231", "mouhid"),
        (@"LGHUBDEVICE\VID_046D&PID_C231\1&79F5D87&2&02", "logi_joy_vir_hid"),
        (@"ROOT\SYSTEM\0001", "logi_joy_bus_enum") }) != null,
    "Filter the Logitech G HUB virtual HID");
Check(HidDiscovery.ExclusionReason(new[] {
        (@"HID\VID_046D&PID_C231", "mouhid"),
        (@"LGHUBDEVICE\VID_046D&PID_C231\1&79F5D87&2&02", "") }) != null,
    "LGHUBDEVICE ancestry alone is enough");
Check(HidDiscovery.ExclusionReason(new[] {
        (@"HID\VID_046D&PID_C539&MI_01&COL01", "mouhid"),
        (@"USB\VID_046D&PID_C539&MI_01", "HidUsb") }) == null,
    "Keep the real Logitech receiver on a machine that also runs G HUB");
Check(HidDiscovery.ExclusionReason(new[] { (@"HID\VID_045E&PID_0000", ""), (@"BTHLEDEVICE\ABC", ""), (@"ACPI\PNP0A08", "") }) == null, "Preserve Bluetooth despite upstream ACPI");
assertions += BluetoothRegression.Run();
assertions += HfpRegression.Run();
assertions += InphicRegression.Run();
assertions += OfficialInphicAulaRegression.Run();
assertions += AulaScopedRegression.Run();
assertions += AtkOfficialRegression.Run();
assertions += MchoseRapooRegression.Run();
assertions += OfficialIntegrationRegression.Run();
assertions += LogitechGpwRegression.Run();
assertions += OriginalGpwScopedRegression.Run();
assertions += InphicStylesRegression.Run();
assertions += G7V9Regression.Run();
assertions += ZeroRainyRegression.Run();
assertions += SupportAuditRegression.Run();
assertions += UpdateReleaseRegression.Run();
assertions += UpdateInstallRegression.Run();
assertions += LogitechReceiverRegression.Run();
assertions += FreqRegression.Run();

// ── HyperX（飓风 3）：03F0:06BE 的六个集合原来全被判成 usage 跳过 ──
Check(HidDiscovery.TryDeviceKind(0x03F0, 0x06BE, 0xFF13, 0x0001, out var hxKind1) && hxKind1 == DeviceKind.Headset,
    "HyperX private page resolves to a headset");
// 反馈包里 03F0:06BE 就是这六个集合，逐个过一遍，一个都不许再落成「usage 跳过」。
foreach (var (page, usage) in new (ushort, ushort)[]
{
    (0x01C0, 0x0001), (0x170F, 0x0202), (0x000C, 0x0001),
    (0xFF13, 0x0001), (0x000B, 0x0005), (0xFFC0, 0x0202)
})
{
    Check(HidDiscovery.TryDeviceKind(0x03F0, 0x06BE, page, usage, out DeviceKind hxKind) &&
        hxKind == DeviceKind.Headset,
        $"HyperX 0x{page:X4}/0x{usage:X4} resolves to a headset");
}
Check(HidDiscovery.TryDeviceKind(0x03F0, 0x06BE, 0x01C0, 0x0001, out var hxKind2) && hxKind2 == DeviceKind.Headset,
    "HyperX vendor-defined page resolves to a headset");
Check(HidDiscovery.TryDeviceKind(0x03F0, 0x06BE, 0x000C, 0x0001, out var hxKind3) && hxKind3 == DeviceKind.Headset,
    "HyperX consumer control resolves to a headset");
Check(!HidDiscovery.TryDeviceKind(0x03F0, 0x1234, 0xFF13, 0x0001, out _),
    "An unknown 03F0 product is not claimed as a headset");
Check(VendorProtocol.IsHyperXHeadset(0x03F0, 0x06BE) && !VendorProtocol.IsHyperXHeadset(0x03F0, 0x1234),
    "HyperX headset PID list");
Check(VendorMap.TryUsbHeadset(0x03F0, 0x06BE, out string hxName) && hxName.Contains("Cloud III S"),
    "HyperX Cloud III S keeps its model name");
Check(!VendorMap.TryUsbHeadset(0x03F0, 0x1234, out _), "Unknown 03F0 stays unknown");
Check(VendorProtocol.IsCloud3SControlCollection(0xFF13, 0x0001, 62, 62) &&
    !VendorProtocol.IsCloud3SControlCollection(0xFFC0, 0x0202, 32, 32),
    "Cloud III S only uses the FF13 control collection");
var cloudRequest = VendorProtocol.BuildCloud3SRequest(62, VendorProtocol.Cloud3SBatteryCommand);
Check(cloudRequest.Length == 62 &&
    cloudRequest.Take(6).SequenceEqual(new byte[] { 0x0C, 0x02, 0x03, 0x01, 0x00, 0x1D }),
    "Cloud III S battery command is Airoha GET_BATTERY_INFO 0x1D");
var cloudReply = new byte[62];
cloudReply[0] = 0x0C; cloudReply[1] = 0x02; cloudReply[2] = 0x03; cloudReply[3] = 1; cloudReply[5] = 0x1D;
cloudReply[6] = 64; cloudReply[7] = 1;
Check(!VendorProtocol.TryParseCloud3SBattery(cloudReply, cloudRequest, out int cloudLevel, out bool cloudCharging) &&
    cloudLevel == -1 && !cloudCharging, "Synthetic Cloud III S offsets are not proven battery data");
Check(!VendorProtocol.TryParseCloud3SBattery(cloudRequest, cloudRequest, out _, out _),
    "Cloud III S rejects the request echo");
cloudReply[6] = 101;
Check(!VendorProtocol.TryParseCloud3SBattery(cloudReply, cloudRequest, out _, out _),
    "Cloud III S rejects an out-of-range level");
var cloudInfoRequest = VendorProtocol.BuildCloud3SRequest(62, VendorProtocol.Cloud3SDeviceInfoCommand);
Check(cloudInfoRequest[5] == 0x02, "Cloud III S device-info command");
Check(HidDiscovery.Bucket.NamesOtherSharedBrand("ATK V75X 2.4G Dongle") &&
    !HidDiscovery.Bucket.NamesOtherSharedBrand("Wireless Gaming Keyboard"),
    "Shared 0x5088 keeps the other brands' names");

var atkReceiver = new HidDiscovery.Bucket
{
    Vid = 0x373B, Pid = 0x1155, Product = "Wireless mouse 8k dongle-L1", Kind = DeviceKind.Mouse
};
atkReceiver.Counts[DeviceKind.Mouse] = 1;
atkReceiver.Counts[DeviceKind.Keyboard] = 2;
var detectedAtkReceiver = atkReceiver.ToDevice();
Check(detectedAtkReceiver.Kind == DeviceKind.Mouse && detectedAtkReceiver.Transport == "2.4G",
    "Reported 8K receiver remains a 2.4G mouse despite auxiliary keyboard collections");
atkReceiver.Product = "Wireless Gaming Keyboard";
Check(atkReceiver.ToDevice().Kind == DeviceKind.Keyboard, "Explicit keyboard keeps its type");
Check(VendorProtocol.IsAtkZeroBatteryCollection(0xFF05, 1, 64, 64) &&
    !VendorProtocol.IsAtkZeroBatteryCollection(0xFF00, 1, 0, 0), "ATK uses only the FF05 control collection");
Check(VendorProtocol.BuildAtkZeroRequest(0x07, true).Take(8)
    .SequenceEqual(new byte[] { 0x08, 0x7D, 0x72, 2, 0, 1, 7, 1 }), "ATK wireless battery request");
Check(VendorProtocol.BuildAtkZeroRequest(0x89, true).Take(8)
    .SequenceEqual(new byte[] { 0x08, 0x0A, 0x72, 8, 0, 1, 0x89, 6 }), "ATK shared receiver identity request");
var atkResponse = new byte[64];
atkResponse[0] = 8;
atkResponse[5] = 0x89;
atkResponse[8] = 1;
atkResponse[9] = 1;
Check(VendorProtocol.IsAtkZeroPairedModel(atkResponse), "CID 1/MID 1 identifies ZERO");
atkResponse[9] = 8;
Check(!VendorProtocol.IsAtkZeroPairedModel(atkResponse), "Shared receiver can pair another mouse");
atkResponse[5] = 7;
atkResponse[7] = 0;
Check(VendorProtocol.TryParseAtkZeroBattery(atkResponse, out int atkLevel) && atkLevel == 0, "ATK valid zero percent");
atkResponse[2] = 2;
Check(!VendorProtocol.TryParseAtkZeroBattery(atkResponse, out _), "ATK ignored reply status");
atkResponse[2] = 0;
atkResponse[7] = 101;
Check(!VendorProtocol.TryParseAtkZeroBattery(atkResponse, out _), "ATK out-of-range level");
// 第二布局：命令退到 [4]、电量退到 [6]、身份 [7..11]，整帧前移一格。
var atkShifted = new byte[64];
atkShifted[4] = 0x07;
atkShifted[6] = 66;
Check(!VendorProtocol.TryParseAtkZeroBattery(atkShifted, out _),
    "ATK rejects unsupported shifted payload");
atkShifted[1] = 0xFF;
Check(!VendorProtocol.TryParseAtkZeroBattery(atkShifted, out _), "ATK 0xFF status frame is not a reading");
atkShifted[1] = 0x02;
Check(!VendorProtocol.TryParseAtkZeroBattery(atkShifted, out _), "ATK status 2 is an error frame in either layout");
var atkIdentityShifted = new byte[64];
atkIdentityShifted[4] = 0x89;
atkIdentityShifted[7] = 1;
atkIdentityShifted[8] = 1;
Check(!VendorProtocol.IsAtkZeroPairedModel(atkIdentityShifted),
    "ATK rejects a shifted identity without Windows report ID");
Check(VendorProtocol.IsAulaF99(0x3554, 0xFA09, "AULA F99") &&
    !VendorProtocol.IsAulaF99(0x3554, 0xFA09, "Wireless Gaming Keyboard"),
    "AULA F99 requires a matching name on the shared ID");
var f99 = VendorProtocol.BuildAulaF99BatteryRequest();
Check(f99.Length == 520 && f99.Take(8).SequenceEqual(new byte[] { 9, 12, 9, 1, 1, 0, 1, 0 }),
    "F99 official Feature report");
Check(!VendorProtocol.TryParseAulaF99Battery(f99, out _), "F99 echo is not a reading");
f99[8] = 57;
Check(VendorProtocol.TryParseAulaF99Battery(f99, out int f99Level) && f99Level == 57, "F99 level field");
Check(VendorProtocol.IsRainy75Wired(0x320F, 0x5055) && !VendorProtocol.IsRainy75Wired(0x320F, 0x5088),
    "Rainy wired reader never claims the shared wireless PID");
Check(VendorProtocol.IsRainy75Wireless(0x320F, 0x5088) && VendorProtocol.IsRainy75Candidate(0x320F, 0x5088),
    "Rainy wireless candidate is separate from the wired verdict");
Check(VendorProtocol.IsRainy75Candidate(0x320F, 0x5055),
    "Both Rainy transports are queried");
Check(VendorProtocol.IsRainy75Name("WOB Rainy75") && !VendorProtocol.IsRainy75Name("Wireless Gaming Keyboard"),
    "Shared 0x5088 only takes the Rainy name when the product says so");
Check(VendorProtocol.IsAulaF99Wired(0x3554, 0xFA09, "AULA F99 Wired") &&
    !VendorProtocol.IsAulaF99Wired(0x3554, 0xFA09, "AULA F99"),
    "F99 wired variant is told apart from the 2.4G one");
var rainy = VendorProtocol.BuildRainy75BatteryRequest();
Check(!VendorProtocol.TryParseRainy75Battery(rainy, out _, out _), "Rainy rejects the request echo");
rainy[8] = 48;
rainy[9] = 1;
Check(VendorProtocol.TryParseRainy75Battery(rainy, out int rainyLevel, out bool rainyCharging) &&
    rainyLevel == 48 && rainyCharging, "Rainy wired battery and charge fields");
// 首字节 00 是官方驱动抓到的另一种写法，33 字节集合也走同一套解析。
var rainyReply = new byte[64];
rainyReply[0] = 0x00;
rainyReply[1] = 0x20;
rainyReply[3] = 0x1A;
rainyReply[4] = 0x06;
rainyReply[8] = 72;
Check(!VendorProtocol.TryParseRainy75Battery(rainyReply, out _, out _),
    "Rainy rejects an unverified report ID");
var rainy33 = new byte[33];
rainy33[0] = 0x04;
rainy33[1] = 0x20;
rainy33[3] = 0x1A;
rainy33[4] = 0x06;
rainy33[8] = 33;
Check(!VendorProtocol.TryParseRainy75Battery(rainy33, out _, out _),
    "Rainy rejects unsupported 33-byte layout");
rainyReply[1] = 0x21;
Check(!VendorProtocol.TryParseRainy75Battery(rainyReply, out _, out _), "Rainy rejects a different command echo");
Check(!VendorProtocol.TryParseRainy75Battery(new byte[64], out _, out _), "Rainy rejects an all-zero frame");
foreach (var (pid, product, model) in new (ushort, string, string)[]
{
    (0x880C, "NUT75", "NUT75"), (0x880C, "NUT87", "NUT87"),
    (0x80D3, "NUT65V2", "NUT65 V2"), (0x8802, "VEGA108", "VEGA108"), (0x800E, "NUT21", "NUT21"),
    (0xFEF9, "NUT75 2.4G WIRELESS", "NUT75"), (0xFEF9, "NUT87 2.4G", "NUT87"),
    (0xFEF9, "NUT65V2 2.4G", "NUT65 V2"), (0xFEF9, "VEGA108 2.4G", "VEGA108"),
    (0xFEFA, "NUT21 2.4G", "NUT21")
})
    Check(VendorProtocol.IdentifyWeikav(0x0C45, pid, product) == model, $"WEIKAV {product} identity");
Check(VendorProtocol.IdentifyWeikav(0x0C45, 0xFEF9, "HS USB Dongle") == null, "Shared OEM dongle stays unnamed");
foreach (var (pid, product, model) in new (ushort, string, string)[]
{
    (0xE407, "Lucky65V2", "Lucky65 V2"), (0xE538, "Lucky65 V3", "Lucky65 V3"),
    (0xE401, "Lucky80", "Lucky80"), (0xE401, "Stars80", "Stars80"),
    (0xE554, "NUT108", "NUT108"), (0xE51A, "NUT65", "NUT65"), (0xE545, "NUT65 CU", "NUT65 CU"),
    (0xE537, "MAX102", "MAX102"), (0xE543, "S21", "Stars21-S"), (0xE403, "Lucky21", "Stars21"),
    (0xE409, "Lucky21", "Stars21"), (0xE402, "Stars75", "Stars75"), (0xE406, "WK87", "WK87"),
    (0xE408, "Record Alice V2", "Record Alice")
})
    Check(VendorProtocol.IdentifyWeikavVia(0x342D, pid, product) == model, $"WEIKAV VIA {product} identity");
Check(VendorProtocol.IdentifyWeikavVia(0x342D, 0xE401, "Wireless Gaming Keyboard") == null,
    "Ambiguous WEIKAV VIA IDs need a model name");

foreach (int length in new[] { 33, 65 })
{
    var reply = VendorProtocol.BuildWeikavBatteryRequest(length);
    Check(reply[1] == 0xAA && reply[2] == 0x10 && reply[3] == (length == 33 ? 24 : 48) &&
        reply[7] == (length == 33 ? 0 : 1), "WEIKAV device-info request header");
    Check(!VendorProtocol.TryParseWeikavBattery(reply, out _), "WEIKAV rejects request echo");
    reply[1] = 0x55;
    reply[13] = 0x45;
    reply[26] = 0;
    Check(VendorProtocol.TryParseWeikavBattery(reply, out int weikavLevel) && weikavLevel == 0,
        "WEIKAV parses device info including zero percent");
    reply[26] = 101;
    Check(!VendorProtocol.TryParseWeikavBattery(reply, out _), "WEIKAV rejects invalid percentages");
}


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
Check(headsetDevice.Kind == DeviceKind.Headset && headsetDevice.RawName == "ZhuAudio ZG2" && !headsetDevice.Name.Contains("竹林鸟") && headsetDevice.Transport == "USB", "Display USB headset by confirmed identity");
Check(headsetDevice.Level == -1 && headsetDevice.Note.Contains("候选设备"), "Device discovery must not fabricate headset battery");
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

// 单设备报告：只包含选中的一台，带 device/probe 两份附加文件。
var fakeTarget = new DeviceInfo
{
    Id = "hid-FFFF-FFFF", Name = "测试设备", Detail = "FFFF:FFFF",
    Transport = "2.4G", Kind = DeviceKind.Mouse, Level = -1, Note = "测试用"
};
var fakeOther = new DeviceInfo
{
    Id = "hid-EEEE-EEEE", Name = "另一台设备", Detail = "EEEE:EEEE",
    Transport = "2.4G", Kind = DeviceKind.Keyboard, Level = 50
};
string targetZip = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
    "mousedock-target-" + Guid.NewGuid().ToString("N") + ".zip");
try
{
    var targetReport = FeedbackService.TryExportReport(new AppSettings(),
        new[] { fakeTarget, fakeOther }, targetZip, fakeTarget);
    Check(targetReport.Succeeded, "Single-device report writes: " + targetReport.Detail);
    using (var archive = System.IO.Compression.ZipFile.OpenRead(targetZip))
    {
        var names = archive.Entries.Select(e => e.FullName).ToArray();
        Check(names.Contains("summary.txt") && names.Contains("hid-devices.txt") &&
              names.Contains("device.txt") && names.Contains("probe.txt") &&
              names.Contains("probe-events.jsonl"), "Single-device report includes device/probe files");
        using var reader = new System.IO.StreamReader(
            archive.GetEntry("hid-devices.txt").Open(), System.Text.Encoding.UTF8);
        string text = reader.ReadToEnd();
        Check(text.Contains("测试设备") && !text.Contains("另一台设备"),
            "Single-device report excludes other detected devices");
        using var deviceReader = new System.IO.StreamReader(
            archive.GetEntry("device.txt").Open(), System.Text.Encoding.UTF8);
        Check(deviceReader.ReadToEnd().Contains("FFFF:FFFF"), "device.txt keeps the selected VID:PID");
    }
}
finally { try { System.IO.File.Delete(targetZip); } catch { } }

Console.WriteLine($"PASS {assertions} offline protocol/integration assertions; no hardware I/O.");
