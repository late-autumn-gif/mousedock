using System.IO;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class M9ProRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string message) { count++; if (!condition) throw new Exception(message); }
        var reading = M9ProProtocol.Read(new Link());
        Check(reading.Level == 61 && reading.Charging && reading.ChargingKnown,
            "M9 Pro official Input9 must deliver the low seven battery bits and charging bit.");
        // Synthetic vectors follow the independently inspected official driver's bit operations.
        foreach (var vector in new (byte Raw, int Level, bool Charging)[]
        {
            (0x00, 0, false), (0x01, 1, false), (0x63, 99, false), (0x64, 100, false),
            (0x80, 0, true), (0x81, 1, true), (0xE3, 99, true), (0xE4, 100, true)
        })
        {
            reading = M9ProProtocol.Read(new Link { Fresh = Frame(vector.Raw) });
            Check(reading.Level == vector.Level && reading.Charging == vector.Charging &&
                reading.ChargingKnown && reading.Model == "凌豹 M9 Pro",
                "Battery boundaries and the charging bit must preserve their official meanings.");
        }
        foreach (var frame in new[]
        {
            Frame(0x65), Frame(0x7F), Frame(0xE5), Frame(0xFF), new byte[33],
            Frame(0xBD)[..32], new byte[34], Frame(0xBD, 8)
        })
            Check(M9ProProtocol.Read(new Link { Fresh = frame }).Level == -1,
                "Wrong report, length or out-of-range low seven bits cannot become battery.");
        var initial = new Link { Buffered = Frame(0xE4), Fresh = null };
        reading = M9ProProtocol.Read(initial);
        Check(reading.Level == 100 && reading.Charging && initial.Reads == 1,
            "The first Input9 arriving on a newly opened handle must be consumed, including when no second report follows.");
        var noClock = new Link { Fresh = new byte[33], FreezeClock = true };
        Check(M9ProProtocol.Read(noClock).Level == -1 && noClock.Reads <= 65,
            "Invalid immediate reports must terminate even when the transport clock does not advance.");
        var timeout = new Link { Fresh = null };
        Check(M9ProProtocol.Read(timeout).Level == -1 && timeout.Now <= 3500,
            "Missing live reports must return unknown within the observation deadline.");
        Check(M9ProProtocol.Read(new Link { ThrowOnRead = true }).Level == -1,
            "Native transport exceptions must downgrade to unknown.");
        // 本仓库比 PR #9 多保留一条 RealTek 0x11 0x06 通用兜底，所以 093A:522C 的其它产品串
        // 仍会放行；专用 M9 Pro 读取器只认精确产品串，这里只锁这一层。
        Check(M9ProProtocol.IsCandidate(0x093A, 0x522C, "LB-M9PRO") &&
            !M9ProProtocol.IsCandidate(0x093A, 0x522C, "LB-M9") &&
            !M9ProProtocol.IsCandidate(0x093A, 0x522C, "LB-M9PRO MAX"),
            "Only the report's exact product may enter the dedicated route.");
        Check(DeviceRoutes.IsKnown(0x093A, 0x522C, "LB-M9PRO"),
            "The dedicated M9 Pro product must stay reachable.");
        Check(HidDiscovery.TryDeviceKind(0x093A, 0x522C, 0xFF05, 1, out var kind, "LB-M9PRO") &&
            kind == DeviceKind.Mouse, "The dedicated collection must belong to the mouse bucket.");

        InstanceScope.BeginScan();
        string a = InstanceScope.Register("m9-A", new[] { (@"USB\VID_093A&PID_522C\A", "usbccgp") });
        string b = InstanceScope.Register("m9-B", new[] { (@"USB\VID_093A&PID_522C\B", "usbccgp") });
        HidDiscovery.Bucket Bucket(string id) => new()
        {
            Vid = 0x093A, Pid = 0x522C, Product = "LB-M9PRO",
            InstanceId = id, Kind = DeviceKind.Mouse, Path = id
        };
        HidDiscovery.VendorCollectionInfo Channel(string path) => new()
        {
            Path = path, Vid = 0x093A, Pid = 0x522C, Product = "LB-M9PRO",
            UsagePage = 0xFF05, Usage = 1, Input = 33, Output = 33, Feature = 9
        };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [a] = Bucket(a), [b] = Bucket(b) };
        var channels = new List<HidDiscovery.VendorCollectionInfo> { Channel("m9-A"), Channel("m9-B") };
        int calls = 0;
        ReferenceBatteryProtocol.Reading DeviceQuery(string path)
        {
            calls++;
            Check(InstanceScope.Current == (path == "m9-A" ? a : b), "Reader must enter only its physical instance.");
            return M9ProProtocol.Read(new Link { Fresh = Frame(path == "m9-A" ? (byte)0 : (byte)0x97) });
        }
        HidDiscovery.QueryM9Pro(buckets, channels, false, null!, DeviceQuery);
        Check(calls == 2 && buckets[a].ToDevice().Level == 0 && buckets[b].ToDevice().Level == 23,
            "Identical receiver instances must not borrow each other's percentage.");
        Check(buckets[a].ToDevice().ConfirmedModel == "凌豹 M9 Pro" &&
            buckets[a].ToDevice().Kind == DeviceKind.Mouse && buckets[a].ToDevice().ReceiverDiscovered &&
            buckets[a].ToDevice().PeripheralConfirmed,
            "A fresh Input9 report must reach the detected device with the confirmed model.");
        var displayed = new AutoDetectSource().UpdateFromDetection(new() { buckets[a].ToDevice() }).Single();
        Check(displayed.Level == 0 && displayed.Name == "凌豹 M9 Pro" && displayed.Connected,
            "A valid zero percentage must reach the production UI data source.");
        calls = 0;
        HidDiscovery.QueryM9Pro(buckets, channels, true, null!, DeviceQuery);
        Check(calls == 0 && buckets[a].ToDevice().Level == -1 && buckets[a].ToDevice().ConfirmedModel == "",
            "Safe mode must clear earlier battery and skip all passive reads.");
        var settings = new AppSettings();
        settings.DisabledRecognitionDeviceIds.Add(a);
        HidDiscovery.QueryM9Pro(buckets, channels, false, new DevicePreferences(settings), DeviceQuery);
        Check(calls == 1 && buckets[a].ToDevice().Level == -1,
            "A disabled instance must remain unknown while another instance can update.");
        settings.DisabledRecognitionDeviceIds.Add("hid-093A-522C");
        calls = 0;
        HidDiscovery.QueryM9Pro(buckets, channels, false, new DevicePreferences(settings), DeviceQuery);
        Check(calls == 0, "Legacy disabled IDs must prevent passive reads.");
        HidDiscovery.QueryM9Pro(new() { [a] = buckets[a] }, new() { Channel("m9-B") },
            false, null!, DeviceQuery);
        Check(calls == 0, "A missing collection cannot be substituted from another instance.");
        InstanceScope.Register("m9-A-duplicate", new[] { (@"USB\VID_093A&PID_522C\A", "usbccgp") });
        HidDiscovery.QueryM9Pro(new() { [a] = buckets[a] },
            new() { Channel("m9-A"), Channel("m9-A-duplicate") }, false, null!, DeviceQuery);
        Check(calls == 0, "Ambiguous collections must remain unread.");
        foreach (int mismatch in Enumerable.Range(0, 9))
        {
            var channel = Channel("m9-A");
            switch (mismatch)
            {
                case 0: channel.Vid = 0x093B; break;
                case 1: channel.Pid = 0x522D; break;
                case 2: channel.Product = "LB-M9"; break;
                case 3: channel.UsagePage = 0xFF00; break;
                case 4: channel.Usage = 2; break;
                case 5: channel.Input = 32; break;
                case 6: channel.Output = 32; break;
                case 7: channel.Feature = 0; break;
                case 8: channel.Feature = 64; break;
            }
            HidDiscovery.QueryM9Pro(new() { [a] = buckets[a] }, new() { channel },
                false, null!, DeviceQuery);
            Check(calls == 0, "Every channel attribute must match; feature0 and feature64 are distinct protocols.");
        }
        HidDiscovery.QueryM9Pro(new() { [a] = buckets[a] }, channels, false, null!,
            _ => ReferenceBatteryProtocol.Reading.Failed("No current report."));
        Check(buckets[a].ToDevice().Level == -1 && buckets[a].ToDevice().ConfirmedModel == "",
            "Failure must erase any earlier battery and current peripheral confirmation.");
        Check(VendorProtocol.ReadM9Pro("m9-A").Level == -1, "An unscoped native reader cannot open a device.");
        using (InstanceScope.Enter(b))
            Check(VendorProtocol.ReadM9Pro("m9-A").Level == -1,
                "Native reader must reject another receiver before opening it.");
        Check(VendorProtocol.TryReadBattery(0x093A, 0x522C) == -1,
            "The legacy 33-byte query route must never touch an M9 Pro receiver.");
        var probe = VendorProtocol.ProbeDevice(0x093A, 0x522C, "LB-M9PRO", false);
        Check(probe.Exchanges == 0 && probe.Responses == 0,
            "Unscoped report probes must never open M9 Pro collections.");
        return count;
    }

    private static byte[] Frame(byte battery, byte reportId = 9)
    {
        var bytes = new byte[33];
        bytes[0] = reportId; bytes[1] = battery;
        return bytes;
    }

    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        public byte[]? Buffered, Fresh = Frame(0xBD);
        public bool EndlessBuffered, FreezeClock, ThrowOnRead;
        public int Reads;
        private bool drained;
        public long Now { get; private set; }
        public byte[] Read(int timeoutMs)
        {
            Reads++;
            if (ThrowOnRead) throw new IOException("Synthetic transport failure.");
            if (!FreezeClock) Now += timeoutMs;
            if (EndlessBuffered) return Frame(0xE4);
            if (!drained)
            {
                if (Buffered != null) { var result = Buffered; Buffered = null; return result; }
                drained = true;
                return null!;
            }
            return Fresh!;
        }
        public bool Write(byte[] request, int timeoutMs) => throw new Exception("M9 Pro passive read cannot write.");
        public bool SetFeature(byte[] request) => throw new Exception("M9 Pro passive read cannot send Feature.");
        public byte[] GetFeature(byte reportId, int length) => throw new Exception("M9 Pro passive read cannot read Feature.");
        public void Delay(int milliseconds) => throw new Exception("M9 Pro passive read cannot delay.");
    }
}

