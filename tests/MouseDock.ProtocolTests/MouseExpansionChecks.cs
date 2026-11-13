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
        Check(!VendorProtocol.ExtraMouseCandidate(0x0B05, 0x1AD0, ""), "existing OMNI ownership retained");
        Check(VendorProtocol.ExtraMouseCandidate(0x5253, 0x1020, "MCHOSE L7 Pro"),
            "L7 notification route requires its product identity");
        Check(!VendorProtocol.ExtraMouseCandidate(0x5253, 0x1020, "MCHOSE A7 Pro") &&
            !VendorProtocol.ExtraMouseCandidate(0x5253, 0x1020, "Wireless Receiver"),
            "shared MCHOSE ID does not assign the L7 notification protocol to another mouse");
        Check(!VendorProtocol.ExtraMouseCandidate(0x248A, 0x5C2F, "AJ159 Receiver") &&
            !VendorProtocol.ExtraMouseCandidate(0x249A, 0x5C2F, "DELUX Receiver"),
            "GET10 retains explicit foreign product exclusions on shared IDs");
        var hechi = new byte[64];
        hechi[0] = 11; hechi[1] = 19; hechi[18] = 57;
        Check(VendorProtocol.MouseExpansionDecode("hechi", hechi)?.Level == 57, "HECHI percent18");
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
        var s9 = VendorProtocol.MouseExpansionTransaction("s9", 0x372E, 0x1029, "", s9Link);
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
        Check(VendorProtocol.MouseExpansionTransaction("s9", 0x372E, 0x1029, "", wrongS9).Level < 0,
            "S9 foreign UUID rejected before battery");
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
        var rawmLink = new ReplayLink(packet =>
        {
            bool mouse = packet[1] == 0xC0;
            byte[] json = System.Text.Encoding.UTF8.GetBytes(mouse ?
                "{\"dn\":\"SA-ML01\",\"battery\":57}" : "{\"dn\":\"USB Receiver\"}");
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
