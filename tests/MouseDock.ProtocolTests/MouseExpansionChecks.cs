using System;
using System.Collections.Generic;
using MouseDock.Services.Detection;

internal static class MouseExpansionChecks
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool result, string name)
        {
            count++;
            if (!result) throw new InvalidOperationException("Mouse expansion: " + name);
        }
        Check(VendorProtocol.ExtraMouseCandidate(0x373B, 0x1196, ""), "HECHI admitted");
        const string sonix = "sonix";
        ReferenceBatteryProtocol.Reading ReadSonix(ushort pid, ReplayLink link) =>
            VendorProtocol.MouseExpansionTransaction(sonix, 0x3710, pid, "", link, page: 0xFFFF);
        byte[] SonixReply(byte[] request, byte level = 47)
        {
            var reply = (byte[])request.Clone();
            reply[1] = 1;
            reply[7] = request[3] == 0x81 ? level : (byte)1;
            return reply;
        }
        foreach (var (pid, model) in new (ushort, string)[]
        {
            (0x1401, "Pulsar Xlite V3"), (0x1402, "Pulsar X2 V2"),
            (0x1403, "Pulsar X2H"), (0x1404, "Pulsar X2A")
        })
        {
            Check(ExtraDeviceRoutes.Primary(0x3710, pid, "") is { Family: sonix, Transport: "USB" } &&
                DeviceRoutes.IsKnown(0x3710, pid, ""), "Pulsar wired identity reaches discovery");
            var channel = new HidDiscovery.VendorCollectionInfo { Vid = 0x3710, Pid = pid,
                Path = "pulsar&mi_03", UsagePage = 0xFFFF, Usage = 1, Feature = 65 };
            var layout = new VendorProtocol.MouseExpansionLayoutInfo(channel, new(), new(), new() { 0 }, 0);
            Check(VendorProtocol.MouseExpansionChannel(sonix, channel) && VendorProtocol.MouseExpansionLayout(sonix, layout) &&
                !VendorProtocol.MouseExpansionLayout(sonix, layout with { Features = new() { 0, 1 } }),
                "Pulsar requires an exact unnumbered Feature channel");
            channel.Path = "pulsar&mi_01";
            Check(!VendorProtocol.MouseExpansionChannel(sonix, channel), "Pulsar rejects unrelated interfaces");
            var link = new ReplayLink(request => SonixReply(request));
            var reading = ReadSonix(pid, link);
            Check(reading is { Level: 47, ChargingKnown: false } && reading.Model == model &&
                link.Writes.Select(report => report[3]).SequenceEqual(new byte[] { 0x81, 0x83, 0x81, 0x83 }) &&
                link.Writes.All(report => report[63] == 9 + report[3] && report[64] == 0),
                "Pulsar publishes two confirmed reads with the exact wired model and no guessed charging");
        }
        Check(ReadSonix(0x5403, new ReplayLink(request => SonixReply(request, 0))) is { Level: 0, Model: null },
            "Fusion receiver retains real zero and unresolved paired model");
        var unrelatedSonix = new ReplayLink(request => SonixReply(request));
        Check(ReadSonix(0x1405, unrelatedSonix).Level < 0 && unrelatedSonix.Writes.Count == 0,
            "Adjacent PID is not granted Sonix access");
        int sonixReads = 0;
        Check(ReadSonix(0x1401, new ReplayLink(request => ++sonixReads > 2 ? null! : SonixReply(request))).Level < 0 &&
            ReadSonix(0x1401, new ReplayLink(request => SonixReply(request, 101))).Level < 0 &&
            ReadSonix(0x1401, new ReplayLink(request => SonixReply(request)[..64])).Level < 0 &&
            ReadSonix(0x1401, new ReplayLink(request => request)).Level < 0,
            "Pulsar rejects timeout, invalid percentage, short replies and echoes");
        Check(!VendorProtocol.ExtraMouseCandidate(0x373B, 0x1197, ""), "adjacent HECHI rejected");
        Check(!VendorProtocol.ExtraMouseCandidate(0x3837, 0x1012, ""), "MagDock rejected");
        Check(!VendorProtocol.ExtraMouseCandidate(0x1B1C, 0x1B7F, ""), "pending Dark Core rejected");
        Check(!VendorProtocol.ExtraMouseCandidate(0x1532, 0x0565, ""), "headset retained");
        Check(!VendorProtocol.ExtraMouseCandidate(0x3554, 0xF517, ""), "existing reference retained");
        Check(!VendorProtocol.ExtraMouseCandidate(0x089D, 0x062F, ""), "existing SC580 retained");
        Check(!VendorProtocol.ExtraMouseCandidate(0x1038, 0x1838, ""), "coarse Aerox route suppressed");
        Check(!VendorProtocol.ExtraMouseCandidate(0x3057, 0x1001, ""), "coarse VAXEE route suppressed");
        Check(!VendorProtocol.ExtraMouseCandidate(0x3057, 0xFFFF, ""), "VAXEE VID-wide permission rejected");
        Check(!VendorProtocol.ExtraMouseCandidate(0x260D, 0x1166, ""), "estimated Dareu route suppressed");
        Check(!VendorProtocol.ExtraMouseCandidate(0x391D, 0x1A21, ""), "ambiguous KC2 per-report length closed");
        Check(!VendorProtocol.ExtraMouseCandidate(0x362D, 0xFF09, ""), "ambiguous RIDLAG per-report length closed");
        Check(VendorProtocol.ExtraMouseCandidate(0x0B05, 0x1AD0, "") &&
            ExtraDeviceRoutes.Primary(0x0B05, 0x1AD0, "") is { Family: "RogOmni", Transport: "2.4G" },
            "SPEEDNOVA mouse is admitted alongside the existing keyboard route");
        byte[] Rog8kReply(byte level)
        {
            var report = new byte[64];
            report[0] = 3; report[1] = 0x12; report[2] = 7; report[5] = level;
            return report;
        }
        ReferenceBatteryProtocol.Reading ReadRog8k(ReplayLink link, ushort pid = 0x1AD0) =>
            VendorProtocol.MouseExpansionTransaction("rog8k", 0x0B05, pid, "", link,
                inputId: 3, outputId: 3, outputLength: 64, inputLength: 64);
        var rog8k = new ReplayLink(_ => Rog8kReply(54));
        Check(ReadRog8k(rog8k) is { Level: 54, ChargingKnown: false, Identity: "report-03-mouse" } &&
            rog8k.Writes.Count == 2 && rog8k.Writes.All(r => r.SequenceEqual(Rog8kReply(0))),
            "SPEEDNOVA requests battery twice, with mouse role and charging unknown");
        var shortRog8k = new ReplayLink(_ => Rog8kReply(54)[..32]);
        Check(ReadRog8k(shortRog8k).Level < 0, "SPEEDNOVA rejects short replies inside its native 64-byte report");
        var echoedRog8k = new ReplayLink(request => request);
        Check(ReadRog8k(echoedRog8k).Level < 0, "SPEEDNOVA rejects an echoed request");
        int rogPass = 0;
        var interruptedRog8k = new ReplayLink(_ => ++rogPass == 1 ? Rog8kReply(54) : null!);
        Check(ReadRog8k(interruptedRog8k).Level < 0, "SPEEDNOVA cannot publish the first reply after a timeout");
        var wrongRog8k = new ReplayLink(_ => Rog8kReply(54));
        Check(ReadRog8k(wrongRog8k, 0x1ACE).Level < 0 && wrongRog8k.Writes.Count == 0,
            "SPEEDNOVA cannot send Report 3 to the ordinary Omni route");
        foreach (ushort pid in new ushort[] { 0x1A1A, 0x1A2F, 0x1A30, 0x1A31, 0x1A32, 0x1A33 })
        {
            Check(ExtraDeviceRoutes.Primary(0x391D, pid, "") is { Family: "vgn-master" } &&
                DeviceRoutes.IsKnown(0x391D, pid, ""), "VGN Dragonfly identity reaches the existing battery reader");
            Check(!VendorProtocol.ExtraMouseCandidate(0x391E, pid, ""), "VGN Dragonfly matching is scoped to the vendor");
        }
        foreach (ushort pid in new ushort[] { 0x1B20, 0x1B21 })
        {
            Check(ExtraDeviceRoutes.Primary(0x24AE, pid, "") is { Family: "rapoo", Transport: "2.4G" },
                "VT2 RT V2 receiver uses the passive path");
            var channel = new HidDiscovery.VendorCollectionInfo { Vid = 0x24AE, Pid = pid,
                Path = "fixture&mi_01", UsagePage = 0xFF00, Usage = 2, Input = 19 };
            var layout = new VendorProtocol.MouseExpansionLayoutInfo(channel, new() { 7 }, new(), new(), 0);
            Check(VendorProtocol.MouseExpansionChannel("rapoo", channel) && VendorProtocol.MouseExpansionLayout("rapoo", layout),
                "VT2 RT V2 accepts the Report 7 battery channel without a mouse input collection");
            Check(!VendorProtocol.MouseExpansionLayout("rapoo", layout with { Inputs = new() { 8 } }),
                "VT2 RT V2 rejects an unrelated report ID");
            channel.Input = 7;
            Check(!VendorProtocol.MouseExpansionLayout("rapoo", layout), "VT2 RT V2 rejects the legacy short layout");
        }
        Check(VendorProtocol.ExtraMouseCandidate(0x5253, 0x1020, "MCHOSE L7 Pro"),
            "L7 notification route requires its product identity");
        Check(!VendorProtocol.ExtraMouseCandidate(0x5253, 0x1020, "MCHOSE A7 Pro") &&
            !VendorProtocol.ExtraMouseCandidate(0x5253, 0x1020, "Wireless Receiver"),
            "shared MCHOSE ID does not assign the L7 notification protocol to another mouse");
        Check(!VendorProtocol.ExtraMouseCandidate(0x248A, 0x5C2F, "AJ159 Receiver") &&
            !VendorProtocol.ExtraMouseCandidate(0x249A, 0x5C2F, "DELUX Receiver"),
            "GET10 retains explicit foreign product exclusions on shared IDs");
        var vt7 = new HidDiscovery.VendorCollectionInfo
        {
            Vid = 0x24AE, Pid = 0x1413, Path = "fixture&mi_01&col09",
            Product = "Rapoo Gaming Device", UsagePage = 0xFF00, Usage = 2, Input = 19, Output = 19, Feature = 0
        };
        Check(VendorProtocol.ExtraMouseFamily(vt7.Vid, vt7.Pid, vt7.Product) == "rapoo-vt7", "VT7 must avoid the 13-byte route");
        Check(VendorProtocol.ExtraMouseFamily(0x24AE, 0x1423, vt7.Product) == "rapoo-short", "VT2 route stays separate");
        Check(VendorProtocol.MouseExpansionChannel("rapoo-vt7", vt7), "Reported VT7 collection");
        vt7.Input = 13;
        Check(!VendorProtocol.MouseExpansionChannel("rapoo-vt7", vt7), "Legacy length rejected");
        var vt7Frame = new byte[19];
        vt7Frame[0] = 7;
        vt7Frame[7] = 2;
        vt7Frame[8] = 42;
        Check(VendorProtocol.MouseExpansionDecode("rapoo-vt7", vt7Frame) is { Level: 42, Charging: true }, "VT7 charging report");
        vt7Frame[7] = 0;
        Check(VendorProtocol.MouseExpansionDecode("rapoo-vt7", vt7Frame) == null, "VT7 offline percent rejected");

        const string g3a = "mchose-g3a";
        const string g3aName = "MCHOSE G3 A";
        Check(ExtraDeviceRoutes.Primary(0xA8A5, 0x2255, g3aName) is { Family: g3a, Transport: "2.4G" },
            "G3 A runs through the production extra device route");
        Check(DeviceRoutes.IsKnown(0xA8A5, 0x2255, g3aName), "G3 A is admitted by the recognition gate");
        Check(VendorProtocol.ExtraMouseFamily(0xA8A5, 0x2255, "MCHOSE G3") != g3a &&
            VendorProtocol.ExtraMouseFamily(0xA8A4, 0x2255, g3aName) != g3a,
            "G3 A rejects the shared-ID product and undocumented wired identity");
        var g3aChannel = new HidDiscovery.VendorCollectionInfo
        {
            Vid = 0xA8A5, Pid = 0x2255, Product = g3aName, Path = "fixture&mi_02",
            UsagePage = 0xFF01, Usage = 0x10, Input = 65, Output = 65, Feature = 0
        };
        Check(VendorProtocol.MouseExpansionChannel(g3a, g3aChannel), "G3 A exact collection");
        g3aChannel.Feature = 1;
        Check(!VendorProtocol.MouseExpansionChannel(g3a, g3aChannel), "G3 A rejects altered descriptor");
        var g3aFrame = new byte[64];
        g3aFrame[0] = 0xAA;
        g3aFrame[1] = 0x30;
        g3aFrame[8] = 0;
        g3aFrame[9] = 1;
        var expectedRequest = new byte[65];
        new byte[] { 0, 0x55, 0x30, 0xA5, 0x0B, 0x2E, 1, 1, 1 }.CopyTo(expectedRequest, 0);
        var g3aLink = new ReplayLink(_ => g3aFrame);
        Check(VendorProtocol.MouseExpansionTransaction(g3a, 0xA8A5, 0x2255, g3aName, g3aLink)
            is { Level: 0, Charging: true }, "G3 A accepts a fresh real zero and charging");
        Check(g3aLink.Writes.Count == 2 && g3aLink.Writes.TrueForAll(packet =>
            packet.AsSpan().SequenceEqual(expectedRequest)), "G3 A sends only the packaged fixed battery request twice");
        int replies = 0;
        var incomplete = new ReplayLink(_ => replies++ == 0 ? g3aFrame : Array.Empty<byte>());
        Check(VendorProtocol.MouseExpansionTransaction(g3a, 0xA8A5, 0x2255, g3aName, incomplete).Level < 0,
            "G3 A cannot publish a single response");
        Check(!VendorProtocol.TryMchoseG3ABattery(g3aFrame.AsSpan(0, 63), out _, out _), "G3 A rejects short frames");
        g3aFrame[8] = 101;
        Check(!VendorProtocol.TryMchoseG3ABattery(g3aFrame, out _, out _), "G3 A percent range");
        g3aFrame[8] = 100;
        g3aFrame[9] = 2;
        Check(!VendorProtocol.TryMchoseG3ABattery(g3aFrame, out _, out _), "G3 A charging field range");
        g3aFrame[9] = 1;

        Check(ExtraDeviceRoutes.Primary(0x24AE, 0x4613, "Rapoo Gaming Device") is
            { Family: "rapoo-vt7", Transport: "USB" }, "VT7 wired route uses the new passive reader");
        Check(ExtraDeviceRoutes.Primary(0x24AE, 0x1413, "Rapoo Gaming Device") is
            { Family: "rapoo-vt7", Transport: "2.4G" }, "VT7 receiver keeps its passive route");
        vt7Frame[7] = 2;
        var vt7Link = new ReplayLink(_ => Array.Empty<byte>());
        vt7Link.Enqueue(vt7Frame);
        Check(VendorProtocol.ReadRapooVt7Status(vt7Link) is { Level: 42, Charging: true } &&
            vt7Link.Writes.Count == 0, "VT7 publishes one validated current report without writes");
        vt7Frame[7] = 0;
        vt7Link.Enqueue(vt7Frame);
        Check(VendorProtocol.ReadRapooVt7Status(vt7Link) is { Level: -1, Status: ReferenceBatteryProtocol.State.Sleep } &&
            vt7Link.Writes.Count == 0, "VT7 offline status ignores the stored percent");
        Check(VendorProtocol.ReadRapooVt7Status(vt7Link).Level < 0, "VT7 silent channel yields unknown");
        vt7Frame[7] = 2;

        BatteryReplay.Sample Capture(string stage, byte[] bytes) =>
            new(stage, true, Length: bytes.Length, Hex: Convert.ToHexString(bytes));
        var requestSample = Capture("g3a-request", expectedRequest);
        var responseSample = Capture("g3a-response", g3aFrame);
        var g3aCapture = new BatteryReplay.Query("g3a", 1, "ExtraMouse:mchose-g3a", "Charging", g3aName, 100,
            [new("g3a-open", true), requestSample, responseSample, requestSample, responseSample]);
        var g3aDescriptor = new BatteryReplay.Collection("g3a", 1, 0xA8A5, 0x2255, g3aName, 0xFF01,
            0x10, 65, 65, 0, false, true, InterfaceLabel: "MI_02", ReportIds: "IN[00] OUT[00] FEATURE[]");
        Check(BatteryReplay.Replay(g3aCapture, [g3aDescriptor]) is { Percent: 100, Charging: true }, "G3 A report import");
        Check(BatteryReplay.Replay(g3aCapture, [g3aDescriptor with { Receiver = "other" }]).Percent == null &&
            BatteryReplay.Replay(g3aCapture with { Samples = g3aCapture.Samples[..3] }, [g3aDescriptor]).Percent == null,
            "G3 A replay rejects foreign instances and incomplete transactions");
        var vt7Capture = new BatteryReplay.Query("vt7", null, "ExtraMouse:rapoo-vt7", "Charging", "Rapoo VT7", 42,
            [new("vt7-open", true), Capture("vt7-status", vt7Frame)]);
        var vt7Descriptor = new BatteryReplay.Collection("vt7", 1, 0x24AE, 0x4613, "Rapoo Gaming Device",
            0xFF00, 2, 19, 19, 0, false, false, InterfaceLabel: "MI_01/COL_09", ReportIds: "IN[07] OUT[07] FEATURE[]");
        Check(BatteryReplay.Replay(vt7Capture, [vt7Descriptor]) is { Percent: 42, Charging: true }, "VT7 wired report import");
        Check(BatteryReplay.Replay(vt7Capture, [vt7Descriptor with { ReportIds = "IN[07] OUT[] FEATURE[]" }]).Percent == null,
            "VT7 replay rejects changed report layout");

        var hechi = new byte[64];
        hechi[0] = 11; hechi[1] = 19; hechi[18] = 57;
        Check(VendorProtocol.MouseExpansionDecode("hechi", hechi)?.Level == 57, "HECHI percent18");
        hechi[0] = 10; hechi[17] = 1;
        Check(VendorProtocol.MouseExpansionDecode("hechi", hechi) is {Level:57, Charging:true},
            "Alternate HECHI input and charging field");
        Check(VendorProtocol.ExtraMouseFamily(0x373B,0x1147,"") == "hechi" &&
            VendorProtocol.ExtraMouseTransport(0x373B,0x1148,"") == "2.4G", "Wired and receiver HECHI routes");
        hechi[18] = 101;
        Check(VendorProtocol.MouseExpansionDecode("hechi", hechi) == null, "HECHI invalid percent");
        var scyrox = new byte[64];
        scyrox[0] = 8; scyrox[1] = 0x2E; scyrox[4] = 41; scyrox[6] = 1;
        Check(VendorProtocol.MouseExpansionDecode("scyrox", scyrox)?.Level == 41, "Scyrox status62 zero");
        scyrox[62] = 1;
        Check(VendorProtocol.MouseExpansionDecode("scyrox", scyrox) == null, "Scyrox status62 one rejected");
        var steel = new byte[64];
        steel[0] = 0xD2; steel[1] = 12;
        var coarse = VendorProtocol.MouseExpansionDecode("steel", steel);
        Check(coarse?.Level == 55 && coarse.Diagnostic.Contains("5%"), "Aerox coarse provenance");
        var ipi = new byte[64];
        ipi[0] = 3; ipi[4] = 2; ipi[6] = 34;
        Check(VendorProtocol.MouseExpansionDecode("ipi", ipi) is { Level: 34, ChargingKnown: false }, "IPI charging unknown");
        var nzxt = new byte[64];
        nzxt[0] = 0x4E; nzxt[1] = 2; nzxt[2] = 0x97; nzxt[17] = 72;
        Check(VendorProtocol.MouseExpansionDecode("nzxt", nzxt)?.Level == 72, "NZXT LE percent");
        nzxt[18] = 1;
        Check(VendorProtocol.MouseExpansionDecode("nzxt", nzxt) == null, "NZXT bounded LE percent");
        var f83 = new byte[65];
        f83[1] = 0xA1; f83[4] = 2; f83[6] = 0x83; f83[8] = 46;
        Check(VendorProtocol.MouseExpansionDecode("feature83", f83)?.Level == 46, "Feature83 framing");
        f83[7] = 2;
        Check(VendorProtocol.MouseExpansionDecode("feature83", f83) == null, "Feature83 flags");
        foreach (string family in new[] { "hechi", "scyrox", "steel", "ipi", "nzxt", "feature83" })
            for (int length = 0; length < 64; length++)
                Check(VendorProtocol.MouseExpansionDecode(family, new byte[length]) == null,
                    family + " short " + length);
        var s9Link = new ReplayLink(packet =>
        {
            var reply = new byte[64];
            reply[0] = 3; reply[1] = 80; reply[3] = 8;
            reply[4] = packet[4]; reply[5] = packet[6];
            if (packet[5] == 0x47) reply[6] = 1;
            if (packet[6] == 0x80) { reply[8] = 9; reply[13] = 14; }
            if (packet[6] == 0x81) reply[6] = 63;
            return reply;
        });
        var s9 = VendorProtocol.MouseExpansionTransaction("s9", 0x372E, 0x1029, "EWEADN S9 Pro", s9Link);
        Check(s9 is { Level: 63, ChargingKnown: false }, "S9 UUID confirmed battery transaction");
        Check(s9Link.Writes.Count == 5, "S9 two identities/batteries and final identity");
        var wrongS9 = new ReplayLink(packet =>
        {
            var reply = new byte[64];
            reply[0] = 3; reply[1] = 80; reply[3] = 8;
            reply[4] = packet[4]; reply[5] = packet[6];
            reply[8] = 9; reply[13] = 13;
            return reply;
        });
        Check(VendorProtocol.MouseExpansionTransaction("s9", 0x372E, 0x1029, "EWEADN S9 Pro", wrongS9).Level < 0,
            "S9 foreign UUID rejected before battery");
        foreach (var (pid, product, transport) in new (ushort, string, string)[]
        {
            (0x1029, "EWEADN S9 Pro", "USB"), (0x1027, "EWEADN S9 Ultra", "USB"),
            (0x105E, "EWEADN S9 Max", "USB"), (0x108D, "EWEADN S9Ultra+", "USB"),
            (0x107B, "EWEADN S7Pro", "USB"), (0x107C, "EWEADN S7Max", "USB"),
            (0x108A, "EWEADN S7Ultra", "USB"), (0x1026, "EWEADN S9", "2.4G"),
            (0x103D, "Gaming 2.4G", "2.4G"), (0x106E, "EWEADN S7Max", "2.4G"),
            (0x106E, "EWEADN S7Ultra", "2.4G")
        })
            Check(DeviceRoutes.Find(0x372E, pid, product) is { Reader: "ExtraMouse:s9" } route && route.Transport == transport,
                $"S7/S9 route {pid:X4} {product}");
        Check(!VendorProtocol.ExtraMouseCandidate(0x372E, 0x106E, "EWEADN S7Pro"),
            "S7 Pro rejects unsupported shared receiver PID");

        (int Level, int Writes) ReplayS9Receiver(string product, byte firstUuid, byte nextUuid,
            bool online = true, byte battery = 63, byte identityLength = 8, bool timeout = false)
        {
            int identities = 0;
            var receiver = new ReplayLink(packet =>
            {
                if (timeout) return [];
                var reply = new byte[64];
                reply[0] = 3; reply[1] = 80; reply[3] = 8;
                reply[4] = packet[4]; reply[5] = packet[6];
                if (packet[5] == 0x47) reply[6] = online ? (byte)1 : (byte)0;
                if (packet[6] == 0x80)
                {
                    reply[3] = identityLength;
                    reply[8] = 9;
                    reply[13] = identities++ == 0 ? firstUuid : nextUuid;
                }
                if (packet[6] == 0x81) reply[6] = battery;
                return reply;
            });
            var result = VendorProtocol.MouseExpansionTransaction("s9", 0x372E, 0x103D, product, receiver);
            return (result.Level, receiver.Writes.Count);
        }
        Check(ReplayS9Receiver("Gaming 2.4G", 0x2A, 0x2A) == (63, 8), "S9 Max shared receiver");
        Check(ReplayS9Receiver("EWEADN S7Max", 0x3E, 0x3E) == (63, 8), "S7 Max shared receiver");
        Check(ReplayS9Receiver("Gaming 2.4G", 0x2A, 0x0E).Level < 0, "S9 receiver rejects pairing change");
        Check(ReplayS9Receiver("Gaming 2.4G", 0x3E, 0x3E).Level < 0, "S9 receiver rejects foreign UUID");
        Check(ReplayS9Receiver("Unknown receiver", 0x2A, 0x2A) == (-1, 0), "S9 unknown HID product performs no I/O");
        Check(ReplayS9Receiver("Gaming 2.4G", 0x2A, 0x2A, online: false) == (-1, 1), "S9 offline receiver");
        Check(ReplayS9Receiver("Gaming 2.4G", 0x2A, 0x2A, identityLength: 7).Level < 0, "S9 truncated UUID");
        Check(ReplayS9Receiver("Gaming 2.4G", 0x2A, 0x2A, battery: 0).Level == 0, "S9 empty battery");
        Check(ReplayS9Receiver("Gaming 2.4G", 0x2A, 0x2A, battery: 100).Level == 100, "S9 full battery");
        Check(ReplayS9Receiver("Gaming 2.4G", 0x2A, 0x2A, battery: 101).Level < 0, "S9 invalid battery level");
        Check(ReplayS9Receiver("Gaming 2.4G", 0x2A, 0x2A, timeout: true).Level < 0, "S9 response timeout");
        var finalLink = new ReplayLink(packet =>
        {
            byte command = (byte)(packet[2] & 0x7F);
            var reply = new byte[64];
            reply[0] = 5; reply[2] = command;
            reply[3] = command == 0x26 ? (byte)3 : (byte)1;
            reply[1] = (byte)(reply[3] + 2);
            reply[4] = command == 0x26 ? (byte)52 : (byte)1;
            return reply;
        });
        Check(VendorProtocol.MouseExpansionTransaction("finalmouse", 0x361D, 0x100, "", finalLink, 5, 4)
            is { Level: 52, ChargingKnown: false }, "Finalmouse SOC with online bracket");
        Check(finalLink.Writes.Count == 4, "Finalmouse online SOC charge online sequence");
        var pulsarLink = new ReplayLink(packet =>
        {
            var reply = new byte[17];
            reply[0] = 8; reply[1] = packet[1]; reply[5] = 2;
            reply[6] = packet[1] == 3 ? (byte)1 : (byte)77;
            reply[16] = (byte)(0x55 - Sum(reply));
            return reply;
        });
        Check(VendorProtocol.MouseExpansionTransaction("pulsar", 0x3710, 0x5406, "", pulsarLink).Level == 77,
            "Pulsar uses reported percent with held online session");
        Check(pulsarLink.Writes[^1][1] == 3 && pulsarLink.Writes[^1][6] == 0, "Pulsar releases hold");
        var pulsarFlood = new PulsarFailureLink(flood:true);
        Check(VendorProtocol.MouseExpansionTransaction("pulsar",0x3710,0x5406,"",pulsarFlood).Level<0 &&
            pulsarFlood.HoldAttempts==0&&pulsarFlood.ReleaseAttempts==0,
            "Pulsar flooded drain does not release an unattempted hold");
        var pulsarDrainFailure = new PulsarFailureLink(drainThrows:true);
        Check(VendorProtocol.MouseExpansionTransaction("pulsar",0x3710,0x5406,"",pulsarDrainFailure).Level<0 &&
            pulsarDrainFailure.HoldAttempts==0&&pulsarDrainFailure.ReleaseAttempts==0,
            "Pulsar throwing drain does not release an unattempted hold");
        var pulsarWriteFailure = new PulsarFailureLink();
        Check(VendorProtocol.MouseExpansionTransaction("pulsar",0x3710,0x5406,"",pulsarWriteFailure).Level<0 &&
            pulsarWriteFailure.HoldAttempts==1&&pulsarWriteFailure.ReleaseAttempts==1,
            "Pulsar failed hold transfer releases exactly once");
        var get10Link = new ReplayLink(packet =>
        {
            var reply = new byte[32];
            new byte[] { 0x10, 0, 1, 11, 77, 49, 56, 51 }.CopyTo(reply, 0);
            reply[13] = 58; reply[14] = 1;
            return reply;
        });
        Check(VendorProtocol.MouseExpansionTransaction("get10", 0x248A, 0x5C2F, "", get10Link).Level == 58,
            "GET10 M183 identity and live flag");
        ReplayLink Rawm(string receiver, string mouseJson) => new ReplayLink(packet =>
        {
            bool mouse = packet[1] == 0xC0;
            byte[] json = System.Text.Encoding.UTF8.GetBytes(mouse ?
                mouseJson : "{\"dn\":\"" + receiver + "\"}");
            int size = json.Length + 2;
            var reply = new byte[64];
            int offset = mouse ? 1 : 0;
            if (mouse) reply[0] = 0xC0;
            reply[offset] = (byte)(json.Length + 6);
            reply.AsSpan(offset + 1, 4).Fill(255);
            reply[offset + 5] = (byte)((size >> 8) << 4 | 2);
            reply[offset + 6] = (byte)size;
            json.CopyTo(reply, offset + 7);
            return reply;
        });
        var rawmLink = Rawm("USB Receiver","{\"dn\":\"SA-ML01\",\"battery\":57}");
        foreach (int product in new[] { 0x2334, 0x2352 })
        {
            string json = System.Text.Json.JsonSerializer.Serialize(new { dn="ES21Pro", pi=product, battery=0, chr=1 });
            var reading = VendorProtocol.MouseExpansionTransaction("rawm",0x1915,0x234B,"",Rawm("RAWM HS Receiver",json));
            Check(reading is { Level:0, Charging:true, ChargingKnown:true }, "Both registered ES21Pro identities may report zero while charging");
        }
        Check(VendorProtocol.MouseExpansionTransaction("rawm",0x1915,0x234B,"",
            Rawm("RAWM HS Receiver","{\"dn\":\"ES21Pro\",\"pi\":1,\"battery\":50}")).Level < 0, "Reject mismatched RAWM product identity");
        Check(VendorProtocol.MouseExpansionTransaction("rawm", 0x1915, 0x232A, "", rawmLink)
            is { Level: 57, ChargingKnown: false }, "RAWM receiver and JSON mouse identities");
        var ajazzLink = new ReplayLink(packet =>
        {
            var reply = new byte[65];
            if (packet[1] == 0xF7) { reply[1] = 1; reply[3] = 44; reply[6] = 1; }
            if (packet[1] == 0xFC) { reply[1] = 0x8F; reply[2] = 0xDA; reply[3] = 6; }
            return reply;
        });
        Check(VendorProtocol.MouseExpansionTransaction("aj159-pro", 0x3151, 0x4027, "", ajazzLink)
            is { Level: 44, Identity: "1754" }, "AJ159 PRO paired model before status percent");
        var ipiLink = new ReplayLink(packet =>
        {
            var reply = new byte[65];
            packet.AsSpan(0, 3).CopyTo(reply);
            if (packet[1] == 0xFD)
            {
                reply[12] = 32; reply[13] = (byte)(255 - Sum(reply));
                return reply;
            }
            byte[] payload = packet[1] == 1 ? new byte[] { 0,0,0,0,0x23,0,0,0,0,2 } :
                new byte[] { 0,1,64,0 };
            reply[4] = (byte)payload.Length;
            payload.CopyTo(reply, 5);
            reply[5 + payload.Length] = (byte)(255 - Sum(reply));
            return reply;
        });
        Check(VendorProtocol.MouseExpansionTransaction("ipi-uni", 0x372E, 0x10F9, "", ipiLink,
            8, 8, outputLength:65, inputLength:65) is { Level:64, Identity:"230000000002" },
            "IPI negotiated frames UUID and two live statuses");
        var raceLink = new ReplayLink(packet =>
        {
            ushort command = (ushort)(packet[7] | packet[8] << 8);
            byte[] payload = command == 0x2C94 ? new byte[11] : new byte[] { 0,0,67,0,1,0,0 };
            if (command == 0x2C94) payload[8] = 128;
            var reply = new byte[packet.Length];
            reply[0] = packet[0] == 6 ? (byte)7 : (byte)20;
            reply[1] = (byte)(payload.Length + 6); reply[2] = packet[2];
            reply[3] = 5; reply[4] = 0x5B; reply[5] = (byte)(payload.Length + 2);
            reply[7] = packet[7]; reply[8] = packet[8]; payload.CopyTo(reply, 9);
            return reply;
        });
        Check(VendorProtocol.MouseExpansionTransaction("infinity97", 0x0E8D, 0x0703, "", raceLink,
            7, 6, featureLength:0, page:0xFF14).Level == 67, "Infinity97 Output6 Input7 remote bracket");
        Check(VendorProtocol.MouseExpansionTransaction("infinity97", 0x0E8D, 0x0703, "", raceLink,
            featureId:20, featureLength:62, page:0xFF13).Level == 67, "Infinity97 sole Feature20 native62");
        var freqLink = new ReplayLink(packet =>
        {
            var reply = new byte[64];
            reply[0] = 0xF0; reply[1] = 0x30; reply[8] = 61;
            return reply;
        });
        Check(VendorProtocol.MouseExpansionTransaction("freq", 0xA8A5, 0x2255, "EWEADN X23Pro", freqLink,
            0xF0, 0xF0, outputLength:64).Level == 61, "FREQ c exact numbered offsets");
        var badFinal = new ReplayLink(packet =>
        {
            var reply = new byte[64]; reply[0] = 5; reply[1] = 3; reply[2] = 0x24; reply[3] = 1; reply[4] = 2;
            return reply;
        });
        Check(VendorProtocol.MouseExpansionTransaction("finalmouse",0x361D,0x100,"",badFinal,5,4).Level<0 &&
            badFinal.Writes.Count==1, "Finalmouse malformed online flag does not trigger fallback");
        int infinityStage = 0;
        byte[] InfinityReply(byte[] packet)
        {
            var reply = new byte[65];
            if (packet[1] == 0xD6) infinityStage = 1;
            if (packet[1] == 0xF7)
            { reply[1] = (byte)infinityStage; reply[6] = 1; reply[7] = 2; }
            if (packet[1] == 0xFC) { reply[1] = 0xD6; reply[3] = 1; reply[4] = 66; }
            return reply;
        }
        var infinityLink = new ReplayLink(InfinityReply);
        Check(VendorProtocol.MouseExpansionTransaction("infinity100",0x3151,0x5007,"",infinityLink).Level==66,
            "Infinity100 documented forwarding battery frame");
        Check(infinityLink.Writes[^1][1]==0xF6&&infinityLink.Writes[^1][2]==0,
            "Infinity100 releases temporary forwarding");
        var infinityCloseFailure = new ReplayLink(InfinityReply){FailInfinityClose=true};
        Check(VendorProtocol.MouseExpansionTransaction("infinity100",0x3151,0x5007,"",infinityCloseFailure).Level<0,
            "Infinity100 failed forwarding release does not return a successful battery");
        var infinityPrimaryFailure = new ReplayLink(packet=>
        {
            var reply=InfinityReply(packet);
            if(packet[1]==0xFC)reply[4]=101;
            return reply;
        }){FailInfinityClose=true};
        Check(VendorProtocol.MouseExpansionTransaction("infinity100",0x3151,0x5007,"",infinityPrimaryFailure)
            .Diagnostic.Contains("invalid Infinity battery frame"),
            "Infinity100 preserves primary failure when forwarding release also fails");
        Check(!VendorProtocol.ExtraMouseCandidate(0x2023,0xF019,"")&&
            !VendorProtocol.ExtraMouseCandidate(0x2023,0xF013,"")&&
            !VendorProtocol.ExtraMouseCandidate(0x2023,0xF015,"")&&
            !VendorProtocol.ExtraMouseCandidate(0x2023,0xF024,""),
            "old Mchose A5 AX5 unverified raw percentage routes suppressed");
        bool Layout(string family,int input,int output,int feature,byte[] ins,byte[] outs,byte[] features,
            ushort page=0xFF02,ushort revision=0) => VendorProtocol.MouseExpansionLayout(family,
                new VendorProtocol.MouseExpansionLayoutInfo(new HidDiscovery.VendorCollectionInfo
                { Vid=0x373B,Pid=0x10C9,UsagePage=page,Usage=2,Input=(ushort)input,Output=(ushort)output,Feature=(ushort)feature },
                    new HashSet<byte>(ins),new HashSet<byte>(outs),new HashSet<byte>(features),revision));
        byte[] empty=Array.Empty<byte>(), id8={8};
        Check(Layout("nk",17,17,0,id8,id8,empty), "NK sole Report8 native17 accepted");
        Check(!Layout("nk",64,64,0,id8,id8,empty), "NK short report in native64 closed");
        Check(Layout("ipi-uni",65,65,0,id8,id8,empty), "IPI sole report layouts accepted");
        Check(!Layout("ipi-uni",65,65,0,new byte[]{8,9},id8,empty), "IPI mixed report lengths closed");
        Check(Layout("feature83",0,0,65,empty,empty,new byte[]{0}), "Feature83 sole ID0 accepted");
        Check(!Layout("feature83",0,0,512,empty,empty,new byte[]{0}), "Feature83 display-sized feature closed");
        Check(!Layout("sora-v2",0,0,704,empty,empty,new byte[]{4,5}), "Sora per-ID32 inside max704 closed");
        Check(Layout("infinity97",0,0,62,empty,empty,new byte[]{20},0xFF13), "Infinity97 sole Feature20 native62 accepted");
        Check(!Layout("infinity97",64,64,0,new byte[]{47},new byte[]{46},empty,0xFF14), "Infinity97 incorrect46/47 closed");
        Check(!Layout("mchose4d",64,64,64,new byte[]{77,78},new byte[]{77},new byte[]{77}), "MHUB ambiguous report length closed");
        foreach (string family in new[] { "sc525","zynoo","compx","hts","hitscan","nk","nordic54","akko","cube",
            "delux","aula-v9","keychron","hecate","rog-standard","rog8k","x7","mchose4d","incott","rapoo-short",
            "rapoo","delux-pro","x23-pro" })
            for (int length=0;length<64;length++)
                Check(VendorProtocol.MouseExpansionDecode(family,new byte[length])==null,
                    family+" all-zero/short "+length);
        return count;
    }

    private static int Sum(byte[] bytes)
    {
        int sum = 0;
        foreach (byte value in bytes) sum += value;
        return sum & 255;
    }

    private sealed class ReplayLink(Func<byte[], byte[]> responder) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> pending = new();
        private byte[] feature=Array.Empty<byte>();
        public long Now { get; private set; }
        internal List<byte[]> Writes { get; } = new();
        internal void Enqueue(byte[] report) => pending.Enqueue(report);
        internal bool FailInfinityClose { get; init; }
        public byte[] Read(int timeoutMs)
        {
            Now += Math.Max(1, timeoutMs);
            return pending.Count == 0 ? Array.Empty<byte>() : pending.Dequeue();
        }
        public bool Write(byte[] request, int timeoutMs)
        {
            Writes.Add((byte[])request.Clone());
            var reply = responder(request);
            if (reply != null) pending.Enqueue(reply);
            return true;
        }
        public bool SetFeature(byte[] request)
        {
            Writes.Add((byte[])request.Clone());
            if(FailInfinityClose&&request[1]==0xF6&&request[2]==0)return false;
            feature = responder(request);
            return true;
        }
        public byte[] GetFeature(byte reportId, int length) { Now += 1; return feature; }
        public void Delay(int milliseconds) => Now += milliseconds;
    }

    private sealed class PulsarFailureLink(bool flood=false,bool drainThrows=false) : ReferenceBatteryProtocol.ILink
    {
        public long Now { get; private set; }
        internal int HoldAttempts { get; private set; }
        internal int ReleaseAttempts { get; private set; }
        public byte[] Read(int timeoutMs)
        {
            Now+=Math.Max(1,timeoutMs);
            if(drainThrows)throw new System.IO.IOException("drain failure");
            return flood?new byte[17]:Array.Empty<byte>();
        }
        public bool Write(byte[] request,int timeoutMs)
        {
            if(request[1]==3&&request[6]==1){HoldAttempts++;return false;}
            if(request[1]==3&&request[6]==0){ReleaseAttempts++;return true;}
            throw new InvalidOperationException("unexpected Pulsar failure transfer");
        }
        public bool SetFeature(byte[] request) => throw new InvalidOperationException();
        public byte[] GetFeature(byte reportId,int length) => throw new InvalidOperationException();
        public void Delay(int milliseconds) => Now+=milliseconds;
    }
}
