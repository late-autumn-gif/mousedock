using MouseDock.Services.Detection;

internal static class MchoseA7V2Regression
{
    public static int Run()
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            count++;
            if (!condition) throw new Exception(message);
        }

        // Synthetic fixture from the official mouseDeviceReadParser layout, not a device capture.
        byte[] Response(int level = 42, byte charge = 0, ushort model = 0x4021, byte connection = 9)
        {
            var body = new byte[19];
            body[0] = 0x37; body[1] = 0x38;
            body[2] = (byte)model; body[3] = (byte)(model >> 8);
            body[8] = connection; body[9] = (byte)level; body[10] = charge;
            return new byte[] { 0x11, 0xF9 }.Concat(body.Select(b => (byte)(b ^ 0xFF))).ToArray();
        }
        bool Parse(byte[] response, out int level, out bool charging) =>
            VendorProtocol.TryParseMchoseLegacy(response, 0x3837, 0x100B, out level, out charging);

        Check(Parse(Response(), out int value, out bool charge) && value == 42 && !charge,
            "A7 V2 Ultra+ receiver must accept its official 4021 model battery response");
        foreach (int level in new[] { 0, 1, 85, 99, 100 })
            Check(Parse(Response(level), out value, out charge) && value == level && !charge,
                $"A7 V2 preserves exact battery {level} without inferring charge");
        Check(Parse(Response(charge: 1), out value, out charge) && value == 42 && charge,
            "A7 V2 propagates the official charging flag");
        Check(!Parse(Response(101), out _, out _), "A7 V2 rejects out-of-range battery");
        Check(!Parse(Response(255), out _, out _), "A7 V2 rejects unknown battery sentinel");
        Check(!Parse(Response(charge: 2), out _, out _), "A7 V2 rejects unverified charging states");
        Check(!Parse(Response(model: 0x4033), out _, out _), "A7 V2 rejects A7 V3 identity");
        Check(!Parse(Response(model: 0x4022), out _, out _), "A7 V2 rejects another paired model");
        Check(!Parse(Response(connection: 1), out _, out _), "A7 V2 sleeping response is not live battery");
        foreach (byte connection in new byte[] { 0, 2, 8, 10, 15, 255 })
            Check(!Parse(Response(connection: connection), out _, out _),
                $"A7 V2 receiver rejects unverified or offline connection state {connection}");
        Check(Parse(Response(connection: 0x19), out value, out _) && value == 42,
            "Reserved high bits do not change the verified wireless-online state");
        Check(!Parse(new byte[21], out _, out _), "Empty A7 V2 data is not zero battery");
        Check(!Parse(new byte[] { 0x11, 0xF9 }.Concat(Enumerable.Repeat((byte)0xFF, 19)).ToArray(), out _, out _),
            "A7 V2 query echo is not zero battery");
        var wrongVendor = Response(); wrongVendor[2] ^= 1;
        Check(!Parse(wrongVendor, out _, out _), "A7 V2 rejects cross-vendor responses");
        foreach (int length in new[] { 0, 12, 20 })
            Check(!Parse(Response()[..length], out _, out _), $"A7 V2 rejects truncated response {length}");
        var wrongCommand = Response(); wrongCommand[1] = 0xFB;
        Check(!Parse(wrongCommand, out _, out _), "A7 V2 rejects a different feature command");
        Check(!VendorProtocol.TryParseMchoseLegacy(Response(), 0x3837, 0x1015, out _, out _),
            "K87 must never be dispatched to the A7 V2 mouse reader");
        Check(!VendorProtocol.TryParseMchoseModern(new byte[64], 0x3837, 0x100B, out _, out _),
            "A7 V2 must not use the A7 V3 protocol");
        Check(!Parse(Response().Concat(new byte[44]).ToArray(), out _, out _),
            "Collection maximum length must not replace report 11's actual length");
        Check(VendorProtocol.BuildMchoseA7V2Request().SequenceEqual(
            new byte[] { 0x11, 0xF9 }.Concat(Enumerable.Repeat((byte)0xFF, 19))),
            "A7 V2 query must match the official 21-byte read-only device-info request");
        var node = new HidProbe.Node
        {
            Vid = 0x3837, Pid = 0x100B, Product = "MCHOSE A7 V2 Ultra+",
            Caps = new HidProbe.HIDP_CAPS { UsagePage = 0xFF01, Usage = 1,
                InputReportByteLength = 65, OutputReportByteLength = 65, FeatureReportByteLength = 65 }
        };
        Check(VendorProtocol.IsMchoseA7V2Interface(node), "A7 V2 accepts its verified query collection");
        node.Caps.UsagePage = 0xFF0B;
        Check(!VendorProtocol.IsMchoseA7V2Interface(node), "A7 V2 must not query its other vendor collection");
        node.Caps.UsagePage = 0xFF01; node.Caps.Usage = 2;
        Check(!VendorProtocol.IsMchoseA7V2Interface(node), "A7 V2 rejects a different collection usage");
        node.Caps.Usage = 1; node.Caps.FeatureReportByteLength = 64;
        Check(!VendorProtocol.IsMchoseA7V2Interface(node), "A7 V2 rejects the modern report descriptor");
        node.Caps.FeatureReportByteLength = 65; node.Product = "MCHOSE A7 V2 Pro";
        Check(!VendorProtocol.IsMchoseA7V2Interface(node), "Shared receiver PID must not authorize another model");
        node.Product = "MCHOSE A7 V2 Ultra+"; node.Pid = 0x1015;
        Check(!VendorProtocol.IsMchoseA7V2Interface(node), "A7 V2 reader rejects the K87 receiver");
        return count;
    }
}
