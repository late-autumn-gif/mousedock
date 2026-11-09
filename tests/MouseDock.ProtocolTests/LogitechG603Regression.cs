using System.Text;
using MouseDock.Models;
using MouseDock.Services;
using MouseDock.Services.Detection;

internal static class LogitechG603Regression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string reason)
        {
            count++;
            if (!ok) throw new Exception("G603: " + reason);
        }

        var link = new Link();
        var reading = OriginalGpwProtocol.Read(link, 0xC539);
        Check(reading.Level == 90 && reading.Model == "Logitech G603" &&
            reading.Identity == "G603 Wireless Gaming Mouse" &&
            reading.Status == ReferenceBatteryProtocol.State.Online,
            "C539 must read the confirmed G603 percentage instead of rejecting it as non-GPW");
        Check(!reading.Charging && reading.ChargingKnown, "Replaceable AA batteries never show charging");
        Check(link.NameReads == 2 && link.BatteryReads == 2, "Two live identities and battery replies are required");
        Check(link.Requests.All(r => r[0] == 0x11 && r[1] == 1 && (r[3] & 15) != 0),
            "G603 keeps the correlated long-report receiver slot");
        Check(!link.Requests.Any(r => r[2] == 0 && r[4] == 0x10 && r[5] == 1),
            "G603 never requests the GPW voltage model");

        foreach (int level in new[] { 1, 50, 100 })
            Check(OriginalGpwProtocol.Read(new Link { Level = level }, 0xC539).Level == level,
                "The reported percentage is retained: " + level);
        foreach (int level in new[] { 0, 101, 255 })
            Check(OriginalGpwProtocol.Read(new Link { Level = level }, 0xC539).Level == -1,
                "Unknown or invalid percentage is rejected: " + level);
        Check(OriginalGpwProtocol.Read(new Link { Status = 8 }, 0xC539).Level == -1,
            "Unknown battery status is rejected");
        Check(!OriginalGpwProtocol.Read(new Link { Status = 1 }, 0xC539).Charging,
            "A rechargeable status cannot turn the G603 into a charging device");
        Check(OriginalGpwProtocol.Read(new Link { DropSecondBattery = true }, 0xC539).Level == -1,
            "A single reply cannot become a fresh reading");
        Check(OriginalGpwProtocol.Read(new Link { HasBattery = false }, 0xC539).Level == -1,
            "No percentage feature means unknown, without a voltage fallback");
        var changed = new Link { ChangeIdentity = true };
        Check(OriginalGpwProtocol.Read(changed, 0xC539).Level == -1 && changed.BatteryReads == 0,
            "An identity change aborts before battery queries");
        var direct = new Link();
        Check(OriginalGpwProtocol.Read(direct, 0xC088).Level == -1 && direct.BatteryReads == 0,
            "G603 identity is admitted only on its shared receiver, not GPW direct USB");
        foreach (string name in new[] { "G603", "G603 Wireless Gaming Mouse 2", "USB Receiver", "G604 Wireless Gaming Mouse" })
        {
            var other = new Link { Name = name };
            Check(OriginalGpwProtocol.Read(other, 0xC539).Level == -1 && other.BatteryReads == 0,
                "Other identities cannot borrow G603 support: " + name);
        }

        InstanceScope.BeginScan();
        const string path = "test#vid_046d&pid_c539&mi_02&col02#G603";
        string instance = InstanceScope.Register(path, [(@"USB\VID_046D&PID_C539\SYNTHETIC_G603", "usbccgp")]);
        var bucket = new HidDiscovery.Bucket
        {
            Vid = 0x046D, Pid = 0xC539, InstanceId = instance, Product = "USB Receiver", Kind = DeviceKind.Keyboard
        };
        var buckets = new Dictionary<string, HidDiscovery.Bucket> { [instance] = bucket };
        var channels = new List<HidDiscovery.VendorCollectionInfo>
        {
            new() { Path = path, Vid = 0x046D, Pid = 0xC539, UsagePage = 0xFF00, Usage = 2, Input = 20, Output = 20 }
        };
        int queries = 0;
        ReferenceBatteryProtocol.Reading Query(string p, ushort vid, ushort pid)
        {
            queries++;
            Check(InstanceScope.Allows(p), "Discovery keeps the physical instance scope");
            return OriginalGpwProtocol.Read(new Link(), pid);
        }
        HidDiscovery.QueryOriginalGpwCollections(buckets, channels, false, null, Query);
        var device = bucket.ToDevice();
        Check(queries == 1 && device.Level == 90 && device.Name == "Logitech G603" &&
            device.ConfirmedModel == "Logitech G603" && device.PeripheralConfirmed &&
            device.Kind == DeviceKind.Mouse && device.Transport == "2.4G" &&
            device.RawName == "USB Receiver" && !device.Charging,
            "Normal discovery publishes the G603 identity and percentage");
        HidDiscovery.QueryOriginalGpwCollections(buckets, channels, true, null, Query);
        Check(queries == 1 && bucket.ToDevice().Level == -1 && !bucket.ToDevice().PeripheralConfirmed,
            "Safe mode clears the old reading and sends no G603 query");
        var preferences = new DevicePreferences(new AppSettings { DisabledRecognitionDeviceIds = [instance] });
        HidDiscovery.QueryOriginalGpwCollections(buckets, channels, false, preferences, Query);
        Check(queries == 1, "Disabled recognition prevents G603 queries");
        HidDiscovery.QueryOriginalGpwCollections(buckets, channels, false, null,
            (_, _, _) => new(90, false, true, ReferenceBatteryProtocol.State.Online, "synthetic",
                "Logitech G603", "G604 Wireless Gaming Mouse"));
        Check(bucket.ToDevice().Level == -1 && !bucket.ToDevice().PeripheralConfirmed,
            "A G603 model label without matching live identity cannot publish a reading");
        return count;
    }

    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        public long Now { get; private set; }
        private readonly Queue<byte[]> input = new();
        public readonly List<byte[]> Requests = new();
        public string Name = "G603 Wireless Gaming Mouse";
        public int Level = 90, NameReads, BatteryReads;
        public byte Status;
        public bool HasBattery = true, ChangeIdentity, DropSecondBattery;

        public byte[]? Read(int milliseconds)
        {
            Now += Math.Max(1, milliseconds);
            return input.Count > 0 ? input.Dequeue() : null;
        }

        public bool Write(byte[] request, int milliseconds)
        {
            Requests.Add(request.ToArray());
            var reply = new byte[20];
            Array.Copy(request, reply, 4);
            if (request[2] == 0)
            {
                ushort feature = (ushort)((request[4] << 8) | request[5]);
                reply[4] = feature == 5 ? (byte)3 : feature == 0x1000 && HasBattery ? (byte)6 : (byte)0;
            }
            else if (request[2] == 3)
            {
                if (request[3] >> 4 == 0) NameReads++;
                string current = ChangeIdentity && NameReads > 1 ? "G604 Wireless Gaming Mouse" : Name;
                var bytes = Encoding.UTF8.GetBytes(current);
                if (request[3] >> 4 == 0) reply[4] = (byte)bytes.Length;
                else bytes.AsSpan(request[4], Math.Min(16, bytes.Length - request[4])).CopyTo(reply.AsSpan(4));
            }
            else if (request[2] == 6)
            {
                BatteryReads++;
                if (DropSecondBattery && BatteryReads > 1) return true;
                reply[4] = (byte)Level;
                reply[5] = 50;
                reply[6] = Status;
            }
            else throw new Exception("Unexpected G603 feature query");
            input.Enqueue(reply);
            return true;
        }

        public bool SetFeature(byte[] request) => throw new Exception("No G603 feature transfer");
        public byte[] GetFeature(byte reportId, int length) => throw new Exception("No G603 feature transfer");
        public void Delay(int milliseconds) => throw new Exception("No G603 configuration changes");
    }
}
