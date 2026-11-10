using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class FeedbackReportRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            checks++;
        }

        var caps = typeof(HidDiscovery).GetNestedType("HIDP_CAPS", BindingFlags.NonPublic)!;
        Check(Marshal.SizeOf(caps) == 64, "Native HIDP_CAPS layout");
        string? Exclusion(params (string, string)[] ancestry) => HidDiscovery.ExclusionReason(ancestry);
        Check(Exclusion((@"HID\VID_093A&PID_3807", "hidi2c")) != null, "Internal I2C excluded");
        Check(Exclusion((@"HID\VID_093A&PID_3807", ""), (@"ACPI\PNP0C50", "")) != null, "ACPI input excluded");
        Check(Exclusion((@"HID\VID_045E&PID_0000", ""), (@"ROOT\VHF\0000", "vhf")) != null, "Virtual HID excluded");
        Check(Exclusion((@"HID\VID_045E&PID_0000", "HidUsb"), (@"USB\VID_045E&PID_0000", ""), (@"ACPI\PNP0A08", "")) == null,
            "A real USB parent survives ACPI upstream");
        foreach (string id in new[] { "045E&PID_0000", "0000&PID_0000", "06CB&PID_1234", "048D&PID_1234" })
            Check(Exclusion(($"HID\\VID_{id}", ""), ($"USB\\VID_{id}", "")) == null, "Unknown USB IDs survive");
        Check(Exclusion((@"BTHLEDEVICE\ABC", ""), (@"ACPI\PNP0A08", "")) == null, "Bluetooth survives ACPI upstream");

        const string targetId = "instance-selected";
        const string otherId = "instance-other";
        const string path = @"\\?\hid#vid_391d&pid_2a05&mi_01&col02#SERIAL-PRIVATE";
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scan = new BatteryReplay.Scan(aliases);
        scan.AddCollection(otherId, path + "-second", 0x391D, 0x2A05, "Another receiver", 0xFFFF, 2, 0, 0, 65, false);
        scan.AddCollection(targetId, path, 0x391D, 0x2A05, "VGN Neon75 Air", 0xFFFF, 2, 0, 0, 65, false, 0x0102);
        scan.AddCollection(targetId, path + "-denied", 0x391D, 0x2A05, null, 0, 0, 0, 0, 0, false,
            openError: 5, status: "open-failed");
        var trace = new BatteryReplay.Trace();
        var request = VendorProtocol.BuildNeon75StatusRequest();
        var response = new byte[65];
        response[2] = 87;
        response[10] = 1;
        for (int i = 0; i < 2; i++)
        {
            trace.Add("set-feature", true, raw: request, keep: true);
            trace.Add("get-feature", true, raw: response, keep: true);
        }
        scan.AddQuery(targetId, path, "Neon75Feature", "Charging", "VGN Neon75 Air", 87, trace);
        scan.Detected.Add(new DetectedDevice
        {
            Id = targetId, Name = "VGN Neon75 Air", Transport = "2.4G", Level = 100, Charging = true
        });
        var single = scan.Finish(new[] { targetId });
        var all = scan.Finish(null);
        Check(single.Collections.Length == 2 && all.Collections.Length == 3, "Single mode excludes same-VID/PID sibling");
        var query = single.Queries.Single();
        Check(BatteryReplay.Replay(query, single.Collections) is { Percent: 87, Charging: true }, "Replay real parser");
        Check(BatteryReplay.Replay(query with { Samples = trace.Snapshot().Skip(2).ToArray() }, single.Collections).Percent == null,
            "One good reply is insufficient");
        Check(BatteryReplay.Replay(query with { Receiver = "other" }, single.Collections).Percent == null,
            "Replay rejects cross-instance evidence");
        Check(query.Samples.All(sample => sample.At.HasValue), "I/O events have timestamps");

        var later = new BatteryReplay.Scan(aliases);
        later.AddCollection(targetId, path, 0x391D, 0x2A05, "VGN Neon75 Air", 0xFFFF, 2, 0, 0, 65, false);
        Check(later.Finish(null).Collections[0].Receiver == query.Receiver, "Alias remains stable across rounds");

        var devices = new[]
        {
            new DeviceInfo { Id = targetId, Name = "VGN Neon75 Air", Detail = "391D:2A05", Transport = "2.4G" },
            new DeviceInfo { Id = "00:11:22:33:44:55", Name = "Bluetooth 00:11:22:33:44:55", Detail = "001122334455",
                Level = 42, IsStale = true, Transport = "蓝牙" }
        };
        foreach (bool isSingle in new[] { true, false })
        {
            string file = Path.Combine(Path.GetTempPath(), $"mousedock-report-v2-{Guid.NewGuid():N}.zip");
            try
            {
                var capture = isSingle ? single : all;
                var result = FeedbackService.TryExportReport(new AppSettings { SafeMode = true }, devices, file,
                    isSingle ? devices[0] : null, new[] { capture, capture }, cableConnected: true);
                Check(result.Succeeded, "Report export succeeds");
                using var zip = ZipFile.OpenRead(file);
                Check(zip.Entries.Select(entry => entry.FullName).SequenceEqual(new[]
                    { "summary.txt", "report.json", "battery-replay.json", "samples/02.json" }), "ZIP has only v2 evidence");
                string Read(string name) { using var reader = new StreamReader(zip.GetEntry(name)!.Open()); return reader.ReadToEnd(); }
                string manifest = Read("report.json");
                using var json = JsonDocument.Parse(manifest);
                Check(json.RootElement.GetProperty("Schema").GetInt32() == 2 &&
                    json.RootElement.GetProperty("Scope").GetString() == (isSingle ? "single" : "all"), "Schema and scope");
                Check(json.RootElement.GetProperty("Series")[0].GetProperty("Assessment").GetString() ==
                    "repeated-100-unverified" && json.RootElement.GetProperty("UserContext").GetString() == "cable-connected",
                    "Repeated 100% is flagged independently of the cable annotation and charging flag");
                using var replay = JsonDocument.Parse(BatteryReplay.ReplayFile(file));
                Check(replay.RootElement.GetProperty("Rounds").GetArrayLength() == 2, "Archive replay processes all rounds");
                Check(!manifest.Contains("00:11:22:33:44:55") && !manifest.Contains("001122334455"), "Bluetooth addresses are redacted");
                Check(manifest.Contains("Bluetooth") != isSingle && manifest.Contains("cached") != isSingle,
                    "Only full report includes the stale Bluetooth snapshot");
                string evidence = Read("battery-replay.json");
                Check(!evidence.Contains("SERIAL-PRIVATE") && !evidence.Contains(targetId), "Physical path and identifier are omitted");
                Check(evidence.Contains("open-failed") && evidence.Contains("MI_01/COL_02"), "Failed interfaces and interface numbers are retained");
            }
            finally { File.Delete(file); }
        }
        return checks;
    }
}
