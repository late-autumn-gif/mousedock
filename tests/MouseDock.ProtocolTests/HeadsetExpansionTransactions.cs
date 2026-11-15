using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MouseDock.Services.Detection;

internal static class HeadsetExpansionTransactions
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string name)
        { if (!condition) throw new InvalidOperationException("Headset transaction: " + name); count++; }
        bool Reject(Action operation)
        {
            try { operation(); return false; }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { return true; }
        }
        byte[] Frame(int size, params (int At, byte Value)[] fields)
        {
            var b = new byte[size]; foreach (var (at, value) in fields) b[at] = value; return b;
        }
        ExpansionWire Wire(Input input, int incoming = 64, int outgoing = 64, int feature = 0) =>
            new(input, incoming, outgoing, feature);
        int bolutekReads = 0;
        var bolutek = new Input(packet =>
        {
            Check(packet.AsSpan().SequenceEqual(HeadsetExpansionBolutek.Request()), "only battery query is emitted");
            return [Frame(64,(0,0x55),(1,0x65),(2,(byte)(60 + ++bolutekReads)),(3,3))];
        });
        Check(HeadsetExpansionBolutek.Read(Wire(bolutek), "Bolutek") is { Level: 62, ChargingKnown: false },
            "two fresh Bolutek reports suppress unverified charging");
        var frozen = new Input(_ => [Frame(64,(0,0x55),(1,0x65),(2,97),(3,0))]);
        Check(HeadsetExpansionBolutek.Read(Wire(frozen), "V9Pro") is { Level: -1, ChargingKnown: false },
            "external power does not produce frozen percentage or inferred charging");
        int notificationReads = 0;
        var notification = new Input(_ => ++notificationReads == 2 ?
            [Frame(64,(0,0x55),(1,0x65),(2,80),(3,2)),Frame(64,(0,0x55),(1,0x0A),(2,1))] :
            [Frame(64,(0,0x55),(1,0x65),(2,79),(3,2))]);
        Check(Reject(() => HeadsetExpansionBolutek.Read(Wire(notification), "AtkN9V2")),
            "N9 V2 disconnect after battery invalidates final reading");
        int links = 0, batteries = 0;
        Input Moondrop(bool disconnect) => new(packet =>
        {
            Check(packet[0] == 0x4B && packet[1] == 128 && packet[2] is 0x31 or 0x27,
                "MOONDROP only link/battery GET");
            if (packet[2] == 0x31)
                return [Frame(64,(0,0x4B),(2,0x31),(4,(byte)(disconnect && ++links == 3 ? 0 : 1)))];
            return [Frame(64,(0,0x4B),(2,0x27),(8,1),(9,(byte)(50 + ++batteries)))];
        });
        Check(HeadsetExpansionMoondrop.Read(Wire(Moondrop(false))).Level == 52, "MOONDROP returns second current sample");
        links = 0; batteries = 0;
        Check(Reject(() => HeadsetExpansionMoondrop.Read(Wire(Moondrop(true)))),
            "MOONDROP final offline refuses earlier percent");
        var paFailure = new Input(_ => []);
        Check(Reject(() => HeadsetExpansionRazerPa.Read(Wire(paFailure), true)) &&
            paFailure.Outputs.Last().AsSpan().SequenceEqual(HeadsetExpansionRazerPa.Request(true, false)),
            "PA failure disables the attempted remote session");
        var paFlood = new Input(_ => []) { Flood = true };
        Check(Reject(() => HeadsetExpansionRazerPa.Read(Wire(paFlood), true)) && paFlood.Outputs.Count == 0,
            "PA failed initial drain does not send enable or disable");
        var feature = new Input(_ => []) { FeatureReply = Frame(64,(0,0x66),(1,0x89),(2,1),(4,63)) };
        Check(HeadsetExpansionHyperX.Read(Wire(feature,outgoing:0,feature:64), "Cloud3").Level == 63 &&
            feature.Features.Count == 2 && feature.Outputs.Count == 0, "Cloud III verified Feature-only transport");
        var failedOutput = new Input(_ => []) { CompleteWrites = false };
        Check(Reject(() => HeadsetExpansionHyperX.Read(Wire(failedOutput,feature:64), "Cloud3")) &&
            failedOutput.Features.Count == 0, "ambiguous output failure never causes a second transport write");
        int cloud3Samples = 0;
        var cloud3Output = new Input(packet =>
        {
            Check(packet.Length == 62 && packet[0] == 0x66 && packet[1] == 0x89 &&
                packet.AsSpan(2).IndexOfAnyExcept((byte)0) < 0, "Cloud III sends only a battery query");
            return [Frame(62, (0, 0x66), (1, 0x0d), (3, 1), (4, (byte)(40 + ++cloud3Samples)))];
        });
        Check(HeadsetExpansionHyperX.Read(Wire(cloud3Output, 62, 62), "Cloud3")
            is { Level: 42, ChargingKnown: false }, "Cloud III event uses the second valid reading");
        Check(Reject(() => HeadsetExpansionHyperX.DecodeCloud3(Frame(62, (0, 0x66), (1, 0x0d), (4, 88)))),
            "Cloud III status bytes both zero reject stale percentage");

        // Keep only the verified protocol fields of the public NGENUITY capture.
        var capturedCloud3S = Frame(64, (0, 0x0c), (1, 2), (2, 3), (3, 1), (5, 6), (6, 47));
        Check(HeadsetExpansionCloud3S.Decode(capturedCloud3S, 6) == 47,
            "Cloud III S battery response matches the NGENUITY sample");
        Check(Reject(() => HeadsetExpansionCloud3S.Decode(HeadsetExpansionCloud3S.Request(6), 6)),
            "Cloud III S request echo is not a zero reading");
        byte[] razerRequest = Frame(91, (2, 0x3f), (6, 2), (7, 7), (8, 0x80), (89, 0x85));
        Check(RazerBatteryFeature.Request(0x3f, RazerBatteryFeature.BatteryLevel).SequenceEqual(razerRequest),
            "Razer battery command and checksum match the standard control report");
        byte[] razerReport = Frame(91, (1, 2), (2, 0x3f), (6, 2), (7, 7), (8, 0x80), (10, 128), (89, 5));
        var razer = new Input(_ => []) { FeatureReply = razerReport };
        Check(HeadsetExpansionRazerFeature.Read2023(Wire(razer, feature: 91)) is
            { Level: -1, ChargingKnown: false, Diagnostic: "device-scale=128/255; exact percentage unavailable" } &&
            razer.Features.Count == 2 && razer.Features.All(frame => frame.SequenceEqual(razerRequest)),
            "Razer headset keeps two Feature reads and its raw scale");
        Check(!RazerBatteryFeature.TryRead(razerReport, 0x9f, 0x80, out _) &&
            !RazerBatteryFeature.TryRead(razerReport, 0x3f, 0x84, out _),
            "Razer ignores unrelated transaction and command");
        razerReport[89] ^= 1;
        Check(!RazerBatteryFeature.TryRead(razerReport, 0x3f, 0x80, out _),
            "Razer rejects corrupted checksum");

        int cloud2Samples = 0;
        var cloud2 = new Input(packet =>
        {
            Check(packet.Length == 52 && packet.AsSpan().StartsWith(new byte[] { 6, 255, 187, 2 }) &&
                packet.AsSpan(4).IndexOfAnyExcept((byte)0) < 0, "Cloud II sends the padded level query");
            return [Frame(20, (0, 6), (1, 255), (2, 187), (3, 2), (5, 1), (7, (byte)(48 + ++cloud2Samples)))];
        });
        Check(HeadsetExpansionHyperX.Read(Wire(cloud2, 20, 52), "Cloud2") is
            { Level: 50, ChargingKnown: false }, "Cloud II keeps the second percentage and unknown charging");
        Check(Reject(() => HeadsetExpansionHyperX.DecodeCloud2(Frame(20, (0, 6), (1, 255), (2, 187), (3, 2)))),
            "Cloud II rejects a zero-filled command echo");

        int alpha2Samples = 0;
        var alpha2 = new Input(packet =>
        {
            Check(packet.Length == 64 && packet[0] == 0x50 && packet[1] == 0x02 &&
                packet.AsSpan(2).IndexOfAnyExcept((byte)0) < 0, "Cloud Alpha 2 sends the captured NGENUITY battery query");
            // Preserve only battery-related fields from the two public captures.
            byte[] reply = ++alpha2Samples == 1 ? Frame(64, (0, 0x51), (1, 2), (2, 51))
                : Frame(64, (0, 0x51), (1, 2), (2, 67), (6, 0x86));
            return [Frame(64, (0, 0x61), (1, 2), (2, 90)), reply];
        });
        Check(HeadsetExpansionHyperX.Read(Wire(alpha2), "Alpha2") is { Level: 67, Charging: true } &&
            alpha2Samples == 2, "Cloud Alpha 2 ignores the unrelated 61 report and uses the second capture");
        int cloudReads = 0;
        var cloud = new Input(packet => [Frame(64,(0,12),(1,2),(2,3),(3,1),(5,packet[5]),
            (6,packet[5] == 6 ? (byte)(70 + ++cloudReads) : (byte)1))]);
        var cloudWire = Wire(cloud);
        Check(HeadsetExpansionCloud3S.Read(cloudWire, [cloudWire]) is { Level:72,Charging:true },
            "Cloud III S commands correlate to fresh percentage and optional charging");
        var aula = Frame(8,(0,0xAB),(1,0x47),(2,0x4D),(3,0x86),(4,3),(5,1),(6,1),(7,57));
        Check(HeadsetExpansionPassive.DecodeAula(aula).Level == 57, "AULA GM86 is three-byte binary magic");
        Check(Reject(() => HeadsetExpansionPassive.DecodeSiren(ExpansionFacts.Packet(13,2,11,1))),
            "short Siren query echo cannot become a battery band");
        int h5Battery = 0, h5Status = 0;
        var h5 = new Input(packet =>
        {
            byte command = packet[9]; ushort transaction = (ushort)(packet[11] | packet[12] << 8);
            byte[] payload = command == 1 ? new byte[] {1} : new byte[] {0,(byte)(60 + ++h5Battery)};
            if (command == 1) h5Status++;
            var hci = Frame(12 + payload.Length,(0,4),(1,255),(2,(byte)(9 + payload.Length)),
                (4,0x96),(5,0xC3),(6,command == 1 ? (byte)0x12 : (byte)0x14),(7,command),(8,16),
                (9,(byte)transaction),(10,(byte)(transaction >> 8)));
            payload.CopyTo(hci,11); hci[^1] = unchecked((byte)hci.Skip(3).Take(hci.Length - 4).Sum(v => (int)v));
            var raw = Frame(64,(0,2),(1,(byte)hci.Length)); hci.CopyTo(raw,2); return [raw];
        });
        Check(HeadsetExpansionSony.ReadH5(Wire(h5)) is { Level:-1,ChargingKnown:false } &&
            h5Battery == 2 && h5Status == 2, "H5 exact HCI transactions with no inferred precise percentage");
        return count;
    }

    private sealed class Input(Func<byte[], IEnumerable<byte[]>> respond) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> queue = new();
        internal readonly List<byte[]> Outputs = new(), Features = new();
        internal bool CompleteWrites = true, Flood;
        internal byte[]? FeatureReply;
        public long Now { get; private set; }
        public byte[]? Read(int timeoutMs)
        {
            Now += queue.Count == 0 ? Math.Max(1, timeoutMs) : 1;
            return queue.Count > 0 ? queue.Dequeue() : Flood ? new byte[64] : null;
        }
        public bool Write(byte[] packet, int timeoutMs)
        {
            Outputs.Add((byte[])packet.Clone());
            if (!CompleteWrites) return false;
            foreach (var b in respond(packet)) queue.Enqueue(b);
            return true;
        }
        public bool SetFeature(byte[] packet)
        { Features.Add((byte[])packet.Clone()); if (FeatureReply != null) queue.Enqueue(FeatureReply); return true; }
        public byte[]? GetFeature(byte id, int size) => FeatureReply;
        public void Delay(int ms) => Now += ms;
    }
}
