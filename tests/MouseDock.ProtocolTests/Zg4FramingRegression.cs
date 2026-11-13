using MouseDock.Services.Detection;

internal static class Zg4FramingRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string reason)
        {
            count++;
            if (!ok) throw new Exception(reason);
        }

        // Literal vectors follow the official driver's JW / cz3 / cz2 framing.
        byte[] request = VendorProtocol.BuildZg4BatteryRequest(0xCD, 64, 0x31);
        Check(request.Take(24).SequenceEqual(Convert.FromHexString(
            "CDEE80001200FF310EFE05070406241B222C1C2D26271FAA")) &&
            request.Skip(24).All(b => b == 0),
            "ZG4 requests the official read-only property batch, including battery 07");
        byte[] direct = Convert.FromHexString("CEFF310407C30000AA");
        byte[] property = Convert.FromHexString("CEFF3105FE07C30000AA");
        foreach (byte[] frame in new[] { direct, property })
        {
            Check(VendorProtocol.ParseZg4Battery(frame, 0xCE, 0x31, out bool charging) == 67 && charging,
                "ZG4 accepts direct and FE-wrapped battery responses");
            Check(VendorProtocol.ParseZg4Battery(frame, 0xCD, 0x31, out _) == -1 &&
                VendorProtocol.ParseZg4Battery(frame, 0xCE, 0x32, out _) == -1,
                "ZG4 validates the input report ID and query sequence");
        }
        byte[] joined = Convert.FromHexString("CEEA80000E00FF3101FCAAFF3105FE07C30000AA");
        Check(VendorProtocol.ParseZg4Battery(joined, 0xCE, 0x31, out _) == 67,
            "ZG4 scans all inner frames after an ACK in one packet");

        foreach (string mode in new[] { "direct", "property", "coalesced", "fragmented", "ack-then-battery" })
        {
            var link = new FramedLink(mode);
            var trace = new BatteryReplay.Trace();
            Check(VendorProtocol.ReadZg4Exchange(link, 0xCE, 0xCD, 64, trace) is
                { Level: 67, Charging: true, Model: "ZhuAudio ZG4" },
                "ZG4 reads the official response form: " + mode);
            var kept = trace.Snapshot().Where(s => s.Stage == "zg4-battery" && s.Ok).ToArray();
            Check(kept.Length == 1 && kept[0].Hex?.Length is 18 or 20,
                "ZG4 diagnostics retain only the validated battery frame");
        }
        foreach (string mode in new[] { "ack-only", "out-of-order", "truncated", "expired",
            "wrong-sequence", "wrong-report", "invalid-level", "oversized" })
        {
            var link = new FramedLink(mode);
            Check(VendorProtocol.ReadZg4Exchange(link, 0xCE, 0xCD, 64, null).Level == -1,
                "ZG4 leaves battery unknown for " + mode);
            Check(link.Now <= 2400, "ZG4 read budget remains bounded for " + mode);
        }
        var allowedInputs = new HashSet<byte> { 0xCE, 0xC0 };
        Check(VendorProtocol.ReadZg4Exchange(new FramedLink("wrong-report"),
            0xCE, 0xCD, 64, null, allowedInputs).Level == 67,
            "ZG4 can receive a battery on a separately validated auxiliary input");
        Check(VendorProtocol.ReadZg4Exchange(new FramedLink("cross-input-fragments"),
            0xCE, 0xCD, 64, null, allowedInputs).Level == -1,
            "ZG4 never assembles fragments from different input report IDs");
        Check(VendorProtocol.ReadZg4Exchange(new FramedLink("interleaved"),
            0xCE, 0xCD, 64, null, allowedInputs).Level == 67,
            "An auxiliary ACK does not destroy the primary input's incomplete batch");
        foreach (string hex in new[] { "CEEA80000500FF4D01FCAA00", "CE09AACC0002065009000000" })
            Check(VendorProtocol.ParseZg4Battery(Convert.FromHexString(hex), 0xCE, 0x4D, out _) == -1,
                "The user's captured ACK and dongle 09 state are not battery percentages");
        return count;
    }

    private sealed class FramedLink(string mode) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<(byte[] Bytes, int Delay)> replies = new();
        public long Now { get; private set; }
        public bool Write(byte[] request, int timeoutMs)
        {
            if (request[1] == 7)
            {
                replies.Enqueue((Convert.FromHexString(request[8] == 2
                    ? "CE09AACC0002C0A00201" : "CE0CAACC000541320688360510"), 1));
                return true;
            }
            byte sequence = request[7];
            byte[] battery = { 0xFF, sequence, 5, 0xFE, 7, 0xC3, 0, 0, 0xAA };
            byte[] ack = { 0xFF, sequence, 1, 0xFC, 0xAA };
            byte[] Report(byte[] bytes) => new byte[] { 0xCE }.Concat(bytes).ToArray();
            byte[] Packet(byte index, int total, byte[] bytes) =>
                Report(new byte[] { 0xEA, index, 0, (byte)total, (byte)(total >> 8) }.Concat(bytes).ToArray());
            if (mode == "direct")
                replies.Enqueue((Report(new byte[] { 0xFF, sequence, 4, 7, 0xC3, 0, 0, 0xAA }), 1));
            else if (mode == "property") replies.Enqueue((Packet(0x80, battery.Length, battery), 1));
            else if (mode == "coalesced")
                replies.Enqueue((Packet(0x80, ack.Length + battery.Length, ack.Concat(battery).ToArray()), 1));
            else if (mode is "fragmented" or "out-of-order" or "truncated" or "expired" or
                "cross-input-fragments" or "interleaved")
            {
                // A non-battery property precedes a battery split at the 58-byte boundary.
                byte[] prefix = new byte[54];
                prefix[0] = 0xFF; prefix[1] = sequence; prefix[2] = 50;
                prefix[3] = 0xFE; prefix[4] = 5; prefix[^1] = 0xAA;
                byte[] all = prefix.Concat(battery).ToArray();
                replies.Enqueue((Packet(0, all.Length, all.Take(58).ToArray()), 1));
                if (mode == "interleaved")
                {
                    byte[] other = Packet(0x80, ack.Length, ack);
                    other[0] = 0xC0;
                    replies.Enqueue((other, 1));
                }
                if (mode != "truncated")
                {
                    byte[] last = Packet(mode == "out-of-order" ? (byte)0x82 : (byte)0x81, 0, all.Skip(58).ToArray());
                    if (mode == "cross-input-fragments") last[0] = 0xC0;
                    replies.Enqueue((last, mode == "expired" ? 2100 : 1));
                }
            }
            else if (mode is "ack-only" or "ack-then-battery")
            {
                replies.Enqueue((Packet(0x80, ack.Length, ack), 1));
                if (mode == "ack-then-battery") replies.Enqueue((Packet(0x80, battery.Length, battery), 800));
            }
            else
            {
                if (mode == "wrong-sequence") battery[1]++;
                if (mode == "invalid-level") battery[5] = 101;
                byte[] report = Packet(0x80, mode == "oversized" ? 1025 : battery.Length, battery);
                if (mode == "wrong-report") report[0] = 0xC0;
                replies.Enqueue((report, 1));
            }
            return true;
        }
        public byte[] Read(int timeoutMs)
        {
            if (replies.Count == 0) { Now += timeoutMs; return null!; }
            var (bytes, delay) = replies.Dequeue();
            Now += Math.Min(delay, timeoutMs);
            return delay <= timeoutMs ? bytes : null!;
        }
        public bool SetFeature(byte[] request) => throw new NotSupportedException();
        public byte[] GetFeature(byte reportId, int length) => throw new NotSupportedException();
        public void Delay(int milliseconds) => Now += milliseconds;
    }
}
