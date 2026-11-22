using System.Text;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class LogitechPairedMouseRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string message)
        {
            count++;
            if (!ok) throw new Exception("C539 paired mouse: " + message);
        }

        var reported = new Link
        {
            Name = "G403 Prodigy Wired/Wireless Gaming Mouse",
            HasBattery = false, HasVoltage = true, Voltage = 4069
        };
        var modeled = OriginalGpwProtocol.Read(reported, 0xC539);
        Check(modeled.Level == 87 && modeled.Model == "Logitech G403" &&
            modeled.Diagnostic.Contains("估算") && modeled.Diagnostic.Contains("4069"),
            "A verified G403 uses its own official discharge model for 4069 mV");
        Check(reported.VoltageReads == 2 && reported.NameReads == 2 && reported.TypeReads == 2,
            "A G403 estimate still requires two voltage samples bracketed by live identity");
        var chargingModel = OriginalGpwProtocol.Read(new Link
        {
            Name = reported.Name, HasBattery = false, HasVoltage = true, Voltage = 4069, VoltageFlags = 0x80
        }, 0xC539);
        Check(chargingModel.Level == 74 && chargingModel.Charging && chargingModel.ChargingKnown,
            "4069 mV uses G403's separate charging curve while charging");
        foreach (int voltageValue in new[] { 4198, 4200, 4300 })
        {
            var plateau = OriginalGpwProtocol.Read(new Link
            {
                Name = reported.Name, HasBattery = false, HasVoltage = true,
                Voltage = voltageValue, VoltageFlags = 0x80
            }, 0xC539);
            Check(plateau.Level == -1 && plateau.Model == "Logitech G403" &&
                !plateau.ChargingKnown && plateau.Diagnostic.Contains("不能唯一换算"),
                "Charging plateau cannot invent a percentage: " + voltageValue);
        }
        var full = OriginalGpwProtocol.Read(new Link
        {
            Name = reported.Name, HasBattery = false, HasVoltage = true, Voltage = 4198, VoltageFlags = 0x81
        }, 0xC539);
        Check(full.Level == 100 && full.Status == ReferenceBatteryProtocol.State.Full && !full.Charging,
            "A device's explicit full state is retained without estimating the charging plateau");
        foreach (byte flags in new byte[] { 0x40, 0x83, 0x87 })
            Check(OriginalGpwProtocol.Read(new Link
            {
                Name = reported.Name, HasBattery = false, HasVoltage = true, Voltage = 4069, VoltageFlags = flags
            }, 0xC539).Level == -1, "Invalid voltage status is rejected: " + flags);
        Check(OriginalGpwProtocol.Read(new Link
        {
            Name = reported.Name, HasBattery = false, HasVoltage = true, Voltage = 4069, DropSecondVoltage = true
        }, 0xC539).Level == -1, "One G403 voltage sample cannot produce an estimate");
        Check(OriginalGpwProtocol.Read(new Link
        {
            Name = reported.Name, HasBattery = false, HasVoltage = true, Voltage = 4069, ChangeIdentity = true
        }, 0xC539).Level == -1, "A swapped peripheral cannot keep the old G403 model estimate");
        foreach (string other in new[] { "G403 HERO Gaming Mouse", reported.Name + " 2", "G703 LIGHTSPEED" })
            Check(OriginalGpwProtocol.Read(new Link
            {
                Name = other, HasBattery = false, HasVoltage = true, Voltage = 4069
            }, 0xC539).Level == -1, "Other live names cannot borrow G403's model: " + other);

        var link = new Link();
        var reading = OriginalGpwProtocol.Read(link, 0xC539);
        Check(reading.Level == 67 && reading.Model == "Logitech G403" &&
            reading.Identity == link.Name && !reading.Charging,
            "A live non-GPW mouse can publish its own percentage");
        Check(link.NameReads == 2 && link.TypeReads == 2 && link.BatteryReads == 2,
            "Live name and mouse type bracket two fresh battery replies");
        Check(link.Requests.All(r => r.Length == 20 && r[0] == 0x11 && r[1] == 1 && (r[3] & 15) != 0),
            "Only correlated long reports on paired slot one are used");
        Check(!link.Requests.Any(r => r[2] == 0 && r[4] == 0x10 && r[5] == 1),
            "A directly reported percentage never requests a GPW voltage curve");

        foreach (int level in new[] { 1, 50, 100 })
            Check(OriginalGpwProtocol.Read(new Link { Level = level }, 0xC539).Level == level,
                "The device percentage is retained: " + level);
        foreach (int level in new[] { 0, 101, 255 })
            Check(OriginalGpwProtocol.Read(new Link { Level = level }, 0xC539).Level == -1,
                "1000 unknown zero and invalid percentages are rejected: " + level);
        Check(OriginalGpwProtocol.Read(new Link { Status = 1 }, 0xC539).Charging,
            "A valid charging status is retained");
        Check(OriginalGpwProtocol.Read(new Link { Status = 8 }, 0xC539).Level == -1,
            "An unsupported battery status cannot produce a percentage");
        Check(OriginalGpwProtocol.Read(new Link { HasBattery = false }, 0xC539).Level == -1,
            "No direct percentage remains unknown");
        var voltage = new Link { Name = "G703 LIGHTSPEED", HasBattery = false, HasVoltage = true };
        reading = OriginalGpwProtocol.Read(voltage, 0xC539);
        Check(reading.Level == -1 && reading.Diagnostic.Contains("3860") && reading.Model == voltage.Name,
            "Voltage-only firmware retains its model and millivolts without inventing a percentage");
        Check(OriginalGpwProtocol.Read(new Link { DropSecondBattery = true }, 0xC539).Level == -1,
            "One battery response is insufficient");
        var keyboard = new Link { DeviceType = 0 };
        Check(OriginalGpwProtocol.Read(keyboard, 0xC539).Level == -1 && keyboard.BatteryReads == 0,
            "A receiver keyboard cannot enter the mouse battery path");
        Check(OriginalGpwProtocol.Read(new Link { ChangeIdentity = true }, 0xC539).Level == -1,
            "Changed live identity discards the battery");
        Check(OriginalGpwProtocol.Read(new Link { ChangeType = true }, 0xC539).Level == -1,
            "Changed peripheral type discards the battery");
        var direct = new Link();
        Check(OriginalGpwProtocol.Read(direct, 0xC088).Level == -1 && direct.BatteryReads == 0,
            "Other models cannot enter the direct GPW USB path");
        var unified = new Link { Unified = true, HasBattery = false, Level = 0, Status = 1 };
        Check(OriginalGpwProtocol.Read(unified, 0xC539).Level == 0,
            "1004 supports a real drained zero when percentage capability is set");
        Check(OriginalGpwProtocol.Read(new Link
            { Unified = true, HasBattery = false, Level = 0 }, 0xC539).Level == -1,
            "An all-zero battery reply indistinguishable from the request is rejected as an echo");
        Check(OriginalGpwProtocol.Read(new Link
            { Unified = true, HasBattery = false, SupportsPercentage = false }, 0xC539).Level == -1,
            "1004 without state-of-charge support cannot turn a battery band into a percentage");
        Check(OriginalGpwProtocol.Read(new Link
            { WrongClient = true }, 0xC539).Level == -1,
            "Uncorrelated replies cannot identify or read a peripheral");

        InstanceScope.BeginScan();
        const string path = "test#vid_046d&pid_c539&mi_02&col02#PAIRED_MOUSE";
        string instance = InstanceScope.Register(path,
            [(@"USB\VID_046D&PID_C539\SYNTHETIC_PAIRED_MOUSE", "usbccgp")]);
        var bucket = new HidDiscovery.Bucket
        {
            Vid = 0x046D, Pid = 0xC539, InstanceId = instance, Product = "USB Receiver",
            Kind = DeviceKind.Keyboard, ChargeFull = true
        };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [instance] = bucket };
        var channels = new List<HidDiscovery.VendorCollectionInfo>
        {
            new() { Path = path, Vid = 0x046D, Pid = 0xC539, UsagePage = 0xFF00,
                Usage = 2, Input = 20, Output = 20 }
        };
        var scan = new BatteryReplay.Scan();
        scan.AddCollection(instance, path, 0x046D, 0xC539, "USB Receiver", 0xFF00, 2, 20, 20, 0, false);
        BatteryReplay.Current = scan;
        try
        {
            int queries = 0;
            HidDiscovery.QueryOriginalGpwCollections(buckets, channels, false, null, (p, _, pid) =>
            {
                queries++;
                Check(InstanceScope.Allows(p), "The query remains in its physical receiver scope");
                return OriginalGpwProtocol.Read(new Link(), pid);
            });
            var device = bucket.ToDevice();
            Check(queries == 1 && device.Level == 67 && device.Name == "Logitech G403" &&
                device.Kind == DeviceKind.Mouse && device.PeripheralConfirmed,
                "Discovery publishes the live paired mouse rather than an unknown receiver");
            Check(!device.ChargeFull, "A prior full flag cannot survive a new non-full reading");
            var capture = scan.Finish(null);
            Check(capture.Queries.Length == 1 && capture.Queries[0].Interface == 1 &&
                capture.Queries[0].ObservedPercent == 67,
                "Reports retain the scoped reader outcome on its actual control interface");
            HidDiscovery.QueryOriginalGpwCollections(buckets, channels, true, null, (_, _, _) =>
            {
                queries++;
                return reading;
            });
            Check(queries == 1 && bucket.ToDevice().Level == -1 &&
                !bucket.ToDevice().PeripheralConfirmed,
                "Safe mode clears prior paired mouse identity and skips I/O");
            var preferences = new DevicePreferences(new AppSettings
                { DisabledRecognitionDeviceIds = [instance] });
            HidDiscovery.QueryOriginalGpwCollections(buckets, channels, false, preferences, (_, _, _) =>
            {
                queries++;
                return reading;
            });
            Check(queries == 1, "Disabled recognition prevents paired mouse queries");
            HidDiscovery.QueryOriginalGpwCollections(buckets, channels, false, null, (_, _, _) =>
                new(67, false, true, ReferenceBatteryProtocol.State.Online, "synthetic", link.Name, link.Name));
            Check(bucket.ToDevice().Level == -1 && !bucket.ToDevice().PeripheralConfirmed,
                "A model label without verified mouse type cannot publish a reading");
            HidDiscovery.QueryOriginalGpwCollections(buckets, channels, false, null, (_, _, _) =>
                ReferenceBatteryProtocol.Reading.Failed(
                    "No matching identity；回显=0 无效=1 LASTREJECT=11ABCDEF1234567890 TX=11ABCDEF1234567890"));
            var failed = scan.Finish(null).Queries.Last();
            Check(failed.Samples.Any(s => s.Stage == "battery-result" && !s.Ok) &&
                failed.Samples.All(s => s.Hex == null && !(s.Result ?? "").Contains("ABCDEF1234567890")),
                "Failure reports retain the reason without legacy device-info or serial bytes");
        }
        finally { BatteryReplay.Current = null; InstanceScope.BeginScan(); }
        return count;
    }

    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        public long Now { get; private set; }
        private readonly Queue<byte[]> input = new();
        internal readonly List<byte[]> Requests = new();
        internal string Name = "G403 Prodigy Wireless Gaming Mouse";
        internal int Level = 67, NameReads, TypeReads, BatteryReads, Voltage = 3860, VoltageReads;
        internal byte Status, DeviceType = 3, VoltageFlags;
        internal bool HasBattery = true, HasVoltage, Unified, SupportsPercentage = true;
        internal bool ChangeIdentity, ChangeType, DropSecondBattery, DropSecondVoltage, WrongClient;

        public byte[]? Read(int milliseconds)
        {
            Now += Math.Max(1, milliseconds);
            return input.Count > 0 ? input.Dequeue() : null;
        }

        public bool Write(byte[] request, int milliseconds)
        {
            Requests.Add(request.ToArray());
            var reply = new byte[20];
            request.AsSpan(0, 4).CopyTo(reply);
            if (WrongClient) reply[3] &= 0xF0;
            int function = request[3] >> 4;
            if (request[2] == 0)
            {
                ushort feature = (ushort)((request[4] << 8) | request[5]);
                reply[4] = feature == 5 ? (byte)3 : feature == 0x1000 && HasBattery ? (byte)6 :
                    feature == 0x1004 && Unified ? (byte)7 : feature == 0x1001 && HasVoltage ? (byte)8 : (byte)0;
            }
            else if (request[2] == 3)
            {
                if (function == 2)
                {
                    TypeReads++;
                    reply[4] = ChangeType && TypeReads > 1 ? (byte)0 : DeviceType;
                }
                else
                {
                    if (function == 0) NameReads++;
                    string current = ChangeIdentity && NameReads > 1 ? "Another Wireless Gaming Mouse" : Name;
                    var name = Encoding.UTF8.GetBytes(current);
                    if (function == 0) reply[4] = (byte)name.Length;
                    else name.AsSpan(request[4], Math.Min(16, name.Length - request[4])).CopyTo(reply.AsSpan(4));
                }
            }
            else if (request[2] == 7 && function == 0)
                reply[5] = SupportsPercentage ? (byte)2 : (byte)0;
            else if (request[2] == 6 || request[2] == 7 && function == 1)
            {
                BatteryReads++;
                if (DropSecondBattery && BatteryReads > 1) return true;
                reply[4] = (byte)Level;
                reply[5] = Level == 0 ? (byte)0 : (byte)50;
                reply[6] = Status;
            }
            else if (request[2] == 8)
            {
                VoltageReads++;
                if (DropSecondVoltage && VoltageReads > 1) return true;
                reply[4] = (byte)(Voltage >> 8); reply[5] = (byte)Voltage;
                reply[6] = VoltageFlags;
            }
            else throw new Exception("Unexpected read-only HID++ query");
            input.Enqueue(reply);
            return true;
        }

        public bool SetFeature(byte[] request) => throw new Exception("No Feature transfer");
        public byte[] GetFeature(byte reportId, int length) => throw new Exception("No Feature transfer");
        public void Delay(int milliseconds) => throw new Exception("No configuration write");
    }
}
