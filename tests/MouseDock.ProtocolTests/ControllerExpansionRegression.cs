using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class ControllerExpansionRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("Controller expansion: " + name);
            checks++;
        }
        var type = typeof(ReferenceBatteryProtocol).Assembly.GetType(
            "MouseDock.Services.Detection.ControllerExpansionProtocol");
        Check(type != null, "native controller decoder is present");
        var decode = type!.GetMethod("Decode", BindingFlags.NonPublic | BindingFlags.Static);
        Check(decode != null, "decoder contract");
        ReferenceBatteryProtocol.Reading Read(string family, byte[] b, bool bt = false, bool adapter = false) =>
            (ReferenceBatteryProtocol.Reading)decode!.Invoke(null, [family, b, bt, adapter])!;
        byte[] Frame(int size, params (int At, byte Value)[] values)
        {
            var b = new byte[size];
            foreach (var (at, value) in values) b[at] = value;
            return b;
        }
        Check(Read("SonyDs4", Frame(64, (0, 1), (30, 0x16))) is
            { Level: -1, Charging: true, Diagnostic: "device-band=6/10" }, "DS4 retains device band");
        Check(Read("SonyDs4", Frame(64, (0, 1), (30, 0x1B))) is
            { Level: -1, Charging: false } full && full.Status.ToString() == "Full", "DS4 full semantics");
        Check(Read("SonyDs4", Frame(64, (0, 1), (30, 4), (31, 4)), adapter: true).Status == ReferenceBatteryProtocol.State.Failed,
            "unpaired adapter rejects byte");
        Check(Read("SonyDs5", Frame(64, (0, 1), (53, 0x22))) is
            { Level: -1 } ds5Full && ds5Full.Status.ToString() == "Full", "DualSense full status");
        Check(Read("SonyDs5", Frame(64, (0, 1), (53, 0x34))).Status == ReferenceBatteryProtocol.State.Failed,
            "DualSense unknown state rejected");
        var btSony = Frame(78, (0, 0x31), (54, 0x15));
        Check(Read("SonyDs5", btSony, true).Status == ReferenceBatteryProtocol.State.Failed, "Bluetooth Sony requires CRC");
        uint crc = Crc([0xA1, .. btSony.Take(74)]);
        BitConverter.GetBytes(crc).CopyTo(btSony, 74);
        Check(Read("SonyDs5", btSony, true) is { Level: -1, Charging: true, Diagnostic: "device-band=5/10" },
            "Bluetooth Sony CRC accepted");
        btSony[3] ^= 1;
        Check(Read("SonyDs5", btSony, true).Status == ReferenceBatteryProtocol.State.Failed, "Bluetooth Sony corruption rejected");
        Check(Read("EightBitDo", Frame(15, (0, 4), (14, 0x80 | 83))) is
            { Level: 83, Charging: true }, "8BitDo actual percentage");
        Check(Read("EightBitDo", Frame(15, (0, 4), (14, 0))).Level < 0, "8BitDo zero is unknown");
        Check(Read("EightBitDo", Frame(15, (0, 1), (14, 80))).Level < 0, "8BitDo transport report");
        Check(Read("Nintendo", Frame(49, (0, 0x30), (2, 0x70)), true) is
            { Level: -1, Charging: true, Status: ReferenceBatteryProtocol.State.Charging },
            "Nintendo reports a band without fabricated percentage");
        Check(Read("Nintendo", Frame(49, (0, 0x21), (2, 0x80), (13, 0x81), (14, 2)), true).Status ==
            ReferenceBatteryProtocol.State.Failed, "Nintendo ACK validated");
        Check(Read("Nintendo", Frame(49, (0, 0x30), (2, 0x80))).Status ==
            ReferenceBatteryProtocol.State.Failed, "Nintendo Bluetooth only");
        Check(Read("Vader5", Frame(31, (0, 0x5A), (1, 0xA5), (2, 1), (5, 130), (11, 0x12))) is
            { Level: -1, Charging: true, Diagnostic: "device-band=3/5" }, "Vader band is not exact percent");
        Check(Read("Vader5", Frame(31, (0, 0x5A), (1, 0xA5), (2, 1), (5, 129))).Status ==
            ReferenceBatteryProtocol.State.Failed, "Vader runtime identity");
        var json = Encoding.UTF8.GetBytes("{\"c\":14,\"e\":0,\"r\":{\"mb\":64,\"sb\":17,\"cs\":1}}");
        var lead = Frame(64, (0, 0xA5), (1, (byte)(json.Length + 4)), (2, 0xCC));
        json.CopyTo(lead, 3);
        lead[json.Length + 3] = unchecked((byte)lead.Take(json.Length + 3).Sum(x => (int)x));
        Check(Read("Leadjoy", lead) is { Level: 64, Charging: true }, "LeadJoy strict JSON response");
        lead[3] ^= 1;
        Check(Read("Leadjoy", lead).Level < 0, "LeadJoy checksum required");
        foreach (string family in new[] { "SonyDs4", "SonyDs5", "EightBitDo", "Nintendo", "Vader5", "Apex5", "Leadjoy" })
        {
            Check(Read(family, []).Status == ReferenceBatteryProtocol.State.Failed, family + " empty report");
            Check(Read(family, [0, 0, 0]).Status == ReferenceBatteryProtocol.State.Failed, family + " short report");
        }
        ControllerExpansionSystem.Sample Sample(string identity, int? remaining, int? full,
            int? design = null, int? rate = null, string status = "Discharging", ushort vid = 0x045E, ushort pid = 0x0B13) =>
            new(identity, "Controller", vid, pid, true, status, remaining, full, design, rate);
        var systemBand = ControllerExpansionSystem.Normalize([Sample("one",400,1000)]).Single();
        Check(systemBand.Level == -1 && systemBand.Note.Contains("2/5"), "WGI fallback ratio is a system band");
        Check(ControllerExpansionSystem.Normalize([Sample("one",400,1000,1000)]).Single().Level == 40,
            "WGI detailed design capacity preserves precise percentage");
        Check(ControllerExpansionSystem.Normalize([Sample("one",400,1000,rate:0)]).Single().Level == 40,
            "WGI explicit zero charge-rate still proves detailed report");
        Check(ControllerExpansionSystem.Normalize([Sample("one",4,10,10)]).Single().Level == -1,
            "WGI small capacity remains a coarse scale");
        Check(ControllerExpansionSystem.Normalize([Sample("one",400,1000,1000,status:"Unknown")]).Single().Level == -1,
            "WGI unknown status cannot publish capacity bytes");
        Check(ControllerExpansionSystem.Normalize([Sample("one",1100,1000,1000)]).Single().Level == -1,
            "WGI remaining over full rejected");
        Check(ControllerExpansionSystem.Normalize([Sample("duplicate",1,100),Sample("duplicate",2,100)]).Count == 0,
            "WGI duplicate identity snapshot rejected");
        Check(ControllerExpansionSystem.Normalize([Sample("lead",50,100,vid:0x4131,pid:0x3519),
            Sample("apex",50,100,vid:0x37D7,pid:0x2501)]).Count == 0,
            "WGI excludes native LeadJoy and Apex duplicates");
        Check(ControllerExpansionSystem.StableId("one") == systemBand.Id &&
            ControllerExpansionSystem.StableId("two") != systemBand.Id, "WGI identity stable and separate");
        const string collection = @"\\?\hid#vid_045e&pid_0b13#one";
        var bindings = new ControllerExpansionSystem.Bindings(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [collection] = "instance-one" },
            new HashSet<(ushort, ushort)> { (0x045E, 0x0B13) });
        Check(bindings.Resolve(collection.ToUpperInvariant(), 0x045E, 0x0B13) == "instance-one",
            "WGI exact collection resolves existing physical identity");
        Check(bindings.Resolve("opaque-one", 0x045E, 0x0B13) == null,
            "WGI opaque same-model identity cannot duplicate or borrow another instance");
        Check(bindings.Resolve("other", 0x045E, 0x0B20) == ControllerExpansionSystem.StableId("other"),
            "WGI unrelated system controller remains available");
        var disabled = new DevicePreferences(new AppSettings { DisabledRecognitionDeviceIds = ["instance-one"] });
        Check(!ControllerExpansionSystem.CanQuery(collection, 0x045E, 0x0B13, bindings, disabled, false),
            "native disabled identity prevents WGI battery query");
        Check(!ControllerExpansionSystem.CanQuery(collection, 0x045E, 0x0B13, bindings, null, true) &&
            !ControllerExpansionSystem.CanQuery("opaque-one", 0x045E, 0x0B13, bindings, null, false),
            "safe mode and unresolved collision prevent WGI query");
        var mapped = ControllerExpansionSystem.Normalize([Sample(collection, 430, 1000, 1000)], bindings).Single();
        Check(mapped.Id == "instance-one", "WGI normalized device reuses HID physical identity");
        var nativeController = new DetectedDevice { Id = "instance-one", Name = "Original", RawName = "Native",
            Kind = DeviceKind.Controller, Level = -1 };
        var mergedControllers = new List<DetectedDevice> { nativeController };
        ExtraDeviceRoutes.MergeSystemControllers(mergedControllers, [mapped]);
        Check(mergedControllers.Count == 1 && nativeController.Level == 43 &&
            nativeController.Name == "Original" && nativeController.RawName == "Native",
            "WGI enriches missing battery without replacing native identity");
        mapped.Level = 70;
        ExtraDeviceRoutes.MergeSystemControllers(mergedControllers, [mapped]);
        Check(nativeController.Level == 43, "WGI cannot replace an existing native reading");
        return checks;
    }

    private static uint Crc(byte[] b)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in b)
        {
            crc ^= value;
            for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0U : 0xEDB88320U);
        }
        return ~crc;
    }
}
