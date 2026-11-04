param([string]$Repo = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$hid = Get-Content -LiteralPath (Join-Path $Repo 'src\MouseDock\Services\Detection\HidDiscovery.cs') -Raw
$feedback = Get-Content -LiteralPath (Join-Path $Repo 'src\MouseDock\Services\FeedbackService.cs') -Raw

function Get-Method([string]$Source, [string]$Signature) {
    $start = $Source.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($start -lt 0) { throw "Missing method: $Signature" }
    $body = $Source.Substring($start)
    $end = [regex]::Match($body, '(?m)^        \}\r?$')
    if (-not $end.Success) { throw "Missing method end: $Signature" }
    return $body.Substring(0, $end.Index + $end.Length)
}

$classifier = Get-Method $hid 'internal static string ExclusionReason('
$report = Get-Method $feedback 'private static string BuildHidReport('
$summary = Get-Method $feedback 'private static string BuildSummary('
$caps = [regex]::Match($hid, '(?s)\[StructLayout\(LayoutKind.Sequential\)\]\s+private struct HIDP_CAPS\s*\{.*?\r?\n        \}').Value
if (-not $caps) { throw 'Missing HIDP_CAPS structure' }

$source = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DeviceInfo = Regression.DeviceInfo;
using AppSettings = Regression.AppSettings;
namespace Regression {
    public sealed class DeviceInfo {
        public string Id = "", Name = "", Detail = "", Transport = "", Note = "", Kind = "Mouse";
        public int Level = -1;
        public bool Connected = true, IsStale;
    }
    public sealed class AppSettings {
        public int RefreshSeconds = 10, LowBatteryWarn = 20;
        public bool AutoUpdate, SafeMode;
    }
    public static class UpdateService { public const string CurrentVersionText = "test"; }
    public static class Detection {
        public static class HidDiscovery { public static List<string> LastDiagnostics = new List<string>(); }
        public static class BluetoothDiscovery { public static List<string> LastErrors = new List<string>(); }
        public static class HidProbe {
            public sealed class CapsInfo { public int UsagePage, Usage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength; }
            public sealed class Node { public int Vid, Pid; public string Product, Path; public CapsInfo Caps = new CapsInfo(); }
            public static List<Node> Nodes = new List<Node>();
            public static List<Node> Enumerate(int? vid, int? pid) { return Nodes; }
        }
    }
    public static class Checks {
        __CAPS__
        __CLASSIFIER__
        __REPORT__
        __SUMMARY__
        private static int checks;
        private static void Assert(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        public static int Run() {
            Assert(Marshal.SizeOf<HIDP_CAPS>() == 64, "Native HIDP_CAPS must contain all 64 bytes");
            Assert(ExclusionReason(new[] { (@"HID\VID_093A&PID_3807", "hidi2c") }) != null, "I2C service must be excluded");
            Assert(ExclusionReason(new[] { (@"HID\VID_093A&PID_3807", ""), (@"ACPI\PNP0C50", "") }) != null, "ACPI input ancestry must be excluded");
            Assert(ExclusionReason(new[] { (@"HID\VID_045E&PID_0000", "") }) == null, "Unknown 045E:0000 must be retained");
            Assert(ExclusionReason(new[] { (@"HID\VID_045E&PID_0000", ""), (@"ROOT\VHF\0000", "vhf") }) != null, "Virtual node must be excluded by actual metadata");
            Assert(ExclusionReason(new[] { (@"HID\VID_045E&PID_0000", "HidUsb"), (@"USB\VID_045E&PID_0000", ""), (@"ACPI\PNP0A08", "") }) == null, "USB must be retained despite upstream ACPI");
            Assert(ExclusionReason(new[] { (@"HID\VID_0000&PID_0000", ""), (@"USB\VID_0000&PID_0000", "") }) == null, "Unknown USB IDs must be retained");
            Assert(ExclusionReason(new[] { (@"HID\VID_06CB&PID_1234", "HidUsb"), (@"USB\VID_06CB&PID_1234", "") }) == null, "No vendor-wide USB blacklist");
            Assert(ExclusionReason(new[] { (@"HID\VID_048D&PID_1234", "HidUsb"), (@"USB\VID_048D&PID_1234", "") }) == null, "ITE USB input must not be excluded by vendor");
            Assert(ExclusionReason(new[] { (@"HID\VID_045E&PID_0000", ""), (@"BTHLEDEVICE\ABC", ""), (@"ACPI\PNP0A08", "") }) == null, "Bluetooth must be retained despite upstream ACPI");
            var devices = new List<DeviceInfo> {
                new DeviceInfo { Id = "hid-3837-1018", Name = "MCHOSE A7 V3", Detail = "3837:1018", Transport = "2.4G", Note = "GET_FEATURE Win32=5" },
                new DeviceInfo { Id = "bt-headset", Name = "ROSE Ceramics U", Detail = "849D4B3669C7", Transport = "蓝牙", Level = 78, IsStale = true }
            };
            Detection.HidProbe.Nodes.Add(new Detection.HidProbe.Node { Vid = 0x045E, Pid = 0, Product = "", Path = "interface-keyboard-1", Caps = new Detection.HidProbe.CapsInfo { UsagePage = 1, Usage = 6 } });
            Detection.HidProbe.Nodes.Add(new Detection.HidProbe.Node { Vid = 0x045E, Pid = 0, Product = "", Path = "interface-keyboard-2", Caps = new Detection.HidProbe.CapsInfo { UsagePage = 1, Usage = 6 } });
            Detection.HidDiscovery.LastDiagnostics.Add("3837:1018 GET_FEATURE failed Win32=5");
            string text = BuildHidReport(devices);
            Assert(!text.Contains("已识别（能读到电量）"), "Discovery header must not imply readable battery");
            Assert(text.Contains("电量已读到: 1；电量未读到: 1"), "Battery successes and failures counted independently");
            Assert(text.Contains("GET_FEATURE Win32=5"), "Per-device read failure explanation included");
            Assert(text.Contains("上一次成功读取的缓存值"), "Cached battery identified");
            Assert(text.Contains("interface-keyboard-1") && text.Contains("interface-keyboard-2"), "All interface paths retained despite identical ID/usage");
            Assert(text.Contains("02. 045E:0000"), "Distinct HID paths not collapsed");
            Assert(text.Contains("集合数量不等于物理设备数量"), "HID collection counting explained");
            Assert(text.Contains("蓝牙地址，不一定是 VID:PID"), "Bluetooth identifier not labeled VID/PID");
            Assert(text.Contains("3837:1018 GET_FEATURE failed Win32=5"), "Latest scan diagnostics included");
            string sum = BuildSummary(new AppSettings { SafeMode = true }, devices);
            Assert(sum.Contains("安全模式 = 开"), "Summary states safety mode");
            Assert(sum.Contains("协议或实际响应数据") && !sum.Contains("发我就能补"), "Summary does not promise unsupported battery protocol");
            return checks;
        }
    }
}
'@
$source = $source.Replace('__CAPS__', $caps).Replace('__CLASSIFIER__', $classifier).Replace('__REPORT__', $report).Replace('__SUMMARY__', $summary)
Add-Type -TypeDefinition $source -Language CSharp
$checks = [Regression.Checks]::Run()
Write-Output "PASS: $checks hardware-free discovery/feedback regression checks"
