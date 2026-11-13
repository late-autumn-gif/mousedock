using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MouseDock.Services.Detection;

internal static class KeyboardExpansionTransactions
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok,string name)
        {
            if (!ok) throw new InvalidOperationException("Keyboard transactions: " + name);
            checks++;
        }
        bool Reject(Action action)
        {
            try { action(); return false; }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { return true; }
        }
        byte[] Frame(int size,params (int At,byte Value)[] fields)
        {
            var b = new byte[size];
            foreach (var (at,value) in fields) b[at] = value;
            return b;
        }
        KeyboardExpansionWire Wire(Input link,int input,int output,byte id = 0,
            int feature = 0,byte featureId = 0,ushort revision = 0) =>
            new(link,input,output,feature,id,id,featureId,revision);

        int statusCount = 0,batteryCount = 0;
        var atk = new Input(packet =>
        {
            if (packet.Length != 33 || packet[0] != 0) throw new InvalidDataException("Wrong HFD framing");
            if (packet[1] == 0xAA && packet[2] == 0xFD)
            { statusCount++; return [Frame(32,(0,0x55),(1,0xFD),(2,1))]; }
            if (packet[1] != 2 || packet[2] != 0x96 || packet[3] != 0x24)
                throw new InvalidDataException("Undocumented HFD command");
            batteryCount++;
            return [Frame(32,(0,2),(1,0x96),(2,0x24),(3,(byte)(60 + batteryCount)))];
        });
        Check(KeyboardExpansionAtk.Read(Wire(atk,32,33),false).Level == 62 &&
            statusCount == 3 && batteryCount == 2,"ATK final fresh reading and online recheck");
        statusCount = 0;
        var atkDisconnect = new Input(packet => packet[1] == 0xAA
            ? [Frame(32,(0,0x55),(1,0xFD),(2,(byte)(++statusCount == 3 ? 2 : 1)))]
            : [Frame(32,(0,2),(1,0x96),(2,0x24),(3,64))]);
        Check(Reject(() => KeyboardExpansionAtk.Read(Wire(atkDisconnect,32,33),false)),
            "ATK final disconnect rejects earlier valid battery");

        byte[] HubInfo(ushort pid,byte level) => Frame(56,(4,0xA6),(5,0x38),
            (6,(byte)pid),(7,(byte)(pid >> 8)),(12,1),(17,level),(18,1));
        Input Hub(ushort board,bool change = false,bool disconnect = false)
        {
            int reads = 0;
            return new Input(packet =>
            {
                if (packet.Length != 33 || packet[1] != 0xAA || packet[2] != 0x10)
                    throw new InvalidDataException("Unexpected HUB command");
                int count = packet[3],at = packet[4] | packet[5] << 8;
                if (count != Math.Min(24,56 - at)) throw new InvalidDataException("Wrong HUB block range");
                int round = reads++ / 3;
                byte[] data = HubInfo((ushort)(change && round == 1 ? 0x2738 : board),(byte)(77 + round));
                var reply = Frame(32,(0,0x55),(1,0x10),(2,(byte)count),(3,(byte)at));
                Array.Copy(data,at,reply,8,count);
                return disconnect && reads == 6 ? [reply,new byte[] {0x55,0xFC,6}] : [reply];
            });
        }
        var hub = KeyboardExpansionAulaHub.Read(Wire(Hub(0x2712),32,33),0xFEFC,"AULA F87ProV2C Dongle",false);
        Check(hub.Level == 78 && hub.Model == "AULA F87 Pro V2 C" &&
            hub.Identity == "38A6:2712" && !hub.ChargingKnown,"AULA verifies board and suppresses unverified charging");
        Check(Reject(() => KeyboardExpansionAulaHub.Read(Wire(Hub(0x9999),32,33),0xFEFC,
            "AULA F87ProV2C Dongle",false)),"AULA rejects another board on shared receiver");
        Check(Reject(() => KeyboardExpansionAulaHub.Read(Wire(Hub(0x2712,true),32,33),0xFEFC,
            "AULA F87ProV2C Dongle",false)),"AULA rejects board replacement");
        Check(Reject(() => KeyboardExpansionAulaHub.Read(Wire(Hub(0x2712,disconnect:true),32,33),0xFEFC,
            "AULA F87ProV2C Dongle",false)),"AULA invalidating event in final queue rejects battery");

        int byReads = 0;
        var by = new Input(packet =>
        {
            if (packet.Length != 20 || !packet.AsSpan(0,3).SequenceEqual(new byte[] {19,74,1}) ||
                packet[19] != 94) throw new InvalidDataException("Wrong BY query");
            return [Frame(20,(0,19),(1,74),(2,1),(5,(byte)(80 + ++byReads)),(6,0x11))];
        });
        var byResult = KeyboardExpansionMchoseBy.Read(Wire(by,20,20,19));
        Check(byResult.Level == 82 && !byResult.ChargingKnown && byResult.Model == null,
            "MCHOSE BY uses two fresh replies without guessing charge or model");

        Input Glw(bool change)
        {
            int baseReads = 0;
            return new Input(packet =>
            {
                if (packet.Length != 65 || packet[1] != 0x55) throw new InvalidDataException("Wrong GLW header");
                int command = packet[2],count = packet[5],offset = packet[6] | packet[7] << 8;
                if (command != 4 && command != 5) throw new InvalidDataException("GLW settings write");
                var b = Frame(64,(0,0xAA),(1,(byte)command),(4,(byte)count),(5,(byte)offset));
                if (command == 4)
                {
                    baseReads++;
                    byte[] basis = [0,4,2,0,3,1];
                    if (change && baseReads == 2) basis[0] = 1;
                    basis.CopyTo(b,8);
                }
                else
                {
                    if (offset is not (128 or 184)) throw new InvalidDataException("Wrong active physical slot");
                    if (offset == 128) b[8 + 32] = 0x80 | 69;
                }
                return [b];
            });
        }
        Check(KeyboardExpansionGlw.Read(Wire(Glw(false),64,65)) is {Level:69,Charging:true},
            "GLW resolves permutation to physical slot");
        Check(Reject(() => KeyboardExpansionGlw.Read(Wire(Glw(true),64,65))),
            "GLW rejects base change without repairing settings");

        Input Qhw(bool badXor = false,bool wrongBoard = false)
        {
            return new Input(packet =>
            {
                int cmd = packet[4] | packet[5] << 8;
                if (cmd is not (0x0900 or 0x0904)) throw new InvalidDataException("QHW settings write");
                int count = cmd == 0x0900 ? 16 : 2;
                var b = Frame(32,(0,77),(1,1),(2,3),(3,(byte)count),
                    (4,packet[4]),(5,packet[5]),(7,packet[7]));
                if (cmd == 0x0900)
                {
                    b[8] = 0x37; b[9] = 0x38;
                    b[10] = wrongBoard ? (byte)0x23 : (byte)0x25; b[11] = 0x20;
                    b[18] = 2; b[20] = 48;
                }
                else { b[8] = 1; b[9] = 49; }
                byte xor = 0;
                for (int i = 2; i < 8 + count; i++) xor ^= b[i];
                b[8 + count] = badXor ? (byte)(xor ^ 1) : xor;
                return [b];
            });
        }
        Check(KeyboardExpansionQhw.Read(Wire(Qhw(),32,32,77)) is {Level:49,Charging:true,Model:null},
            "G98 checks identity and battery XOR without changing K87");
        Check(Reject(() => KeyboardExpansionQhw.Read(Wire(Qhw(true),32,32,77))),"G98 rejects response XOR mismatch");
        Check(Reject(() => KeyboardExpansionQhw.Read(Wire(Qhw(wrongBoard:true),32,32,77))),
            "G98 does not accept K87-specific downstream board");

        Input Fa09(bool wrongModel)
        {
            return new Input(packet =>
            {
                if (packet.Length != 20 || packet[0] != 19 || packet[2] != 1 ||
                    packet[1] is not (5 or 74)) throw new InvalidDataException("Wrong FA09 query");
                var b = Frame(20,(0,19),(1,packet[1]),(2,1),(4,(byte)(packet[1] == 5 ? 6 : 2)));
                if (packet[1] == 5)
                    new byte[] {3,0,0,0,(byte)(wrongModel ? 3 : 1),0x58}.CopyTo(b,5);
                else { b[5] = 43; b[6] = 0xFF; }
                b[19] = unchecked((byte)b.Take(19).Sum(x => (int)x));
                return [b];
            });
        }
        Check(KeyboardExpansionFa09.Read(Wire(Fa09(false),20,20,19,revision:0x2007)) is
            {Level:43,Model:"AULA F99 Pro",ChargingKnown:false},"F99 runtime identity before and after power");
        Check(Reject(() => KeyboardExpansionFa09.Read(Wire(Fa09(true),20,20,19,revision:0x2007))),
            "F99 shared receiver cannot imply model identity");

        Input RogPair(bool changed)
        {
            int calls = 0;
            return new Input(packet =>
            {
                if (packet.Length != 64 || packet[0] != 1 || packet[1] != 0xA0)
                    throw new InvalidDataException("Wrong ROG pairing query");
                ushort pid = changed && ++calls == 2 ? (ushort)0x1C25 : (ushort)0x1C12;
                return [Frame(64,(0,1),(1,0xA0),(3,1),(5,(byte)pid),(6,(byte)(pid >> 8)),(7,2),(8,4))];
            });
        }
        Input RogPower(bool offline = false) => new(packet =>
        {
            if (packet.Length != 64 || packet[0] != 2 || packet[1] != 0x12 ||
                packet[2] is not (1 or 3)) throw new InvalidDataException("Wrong ROG power command");
            return packet[2] == 3 ? [Frame(64,(0,2),(1,0x12),(2,3),(5,(byte)(offline ? 255 : 1)))] :
                [Frame(64,(0,2),(1,0x12),(2,1),(6,45),(9,1))];
        });
        Check(KeyboardExpansionRog.ReadOmni(Wire(RogPair(false),64,64,1),Wire(RogPower(),64,64,2),true)
            is {Level:45,Model:"ROG Azoth 98 HE / LITE",ChargingKnown:false},"8K pairing and online checks");
        Check(Reject(() => KeyboardExpansionRog.ReadOmni(Wire(RogPair(true),64,64,1),
            Wire(RogPower(),64,64,2),true)),"8K pairing replacement rejects valid earlier power");
        Check(Reject(() => KeyboardExpansionRog.ReadOmni(Wire(RogPair(false),64,64,1),
            Wire(RogPower(true),64,64,2),true)),"8K offline state rejects numeric bytes");

        Input Pmo(bool invalidBattery)
        {
            return new Input(packet =>
            {
                if (packet.Length != 33 || packet[1] != 0xAA || packet[2] is not (0xD0 or 0x10 or 0x11 or 0x30))
                    throw new InvalidDataException("Unexpected PMO command");
                byte command = packet[2];
                var b = Frame(32,(0,0xAA),(1,command),(4,24),(7,0x55));
                if (command == 0xD0) { b[9] = 0xB0; b[10] = 0x36; b[11] = 9; b[12] = 0x30; b[13] = 4; }
                if (command == 0x30) { b[8] = invalidBattery ? (byte)101 : (byte)38; b[9] = 1; }
                return [b];
            });
        }
        var pmo = Pmo(false);
        Check(KeyboardExpansionPmo.Read(Wire(pmo,32,33)) is
            {Level:38,Model:"PMO WAVE 75 RGB",ChargingKnown:false},"PMO scoped session and downstream model");
        var pmoBad = Pmo(true);
        Check(Reject(() => KeyboardExpansionPmo.Read(Wire(pmoBad,32,33))) &&
            pmoBad.Outputs.Any(b => b[2] == 0x11),"PMO battery failure still stops session");

        var direct = new Input(_ => []);
        direct.Feature = packet =>
        {
            if (packet[0] != 0 || packet[1] != 0 || packet[2] != 3 || packet[3] != 7 || packet[4] != 0x80)
                throw new InvalidDataException("Direct A98 must not query receiver A0");
            return Frame(65,(1,2),(2,3),(3,7),(4,0x80),(7,1),(8,39));
        };
        Check(KeyboardExpansionDareu.Read(Wire(direct,0,0,feature:65),0x0051) is {Level:-1,Model:"DAREU A98"},
            "direct A98 uses target zero power command only");

        var lofree = new Input(packet =>
        {
            if (packet.Length != 32 || packet[0] != 4 || packet[3] is not (1 or 2 or 0xAA or 0x1A))
                throw new InvalidDataException("Unexpected Lofree command");
            return packet[3] == 2 ? [] : [Frame(32,(0,4),(3,packet[3]),(8,(byte)(packet[3] == 0x1A ? 52 : 1)))];
        });
        Check(KeyboardExpansionLofree.Read(Wire(lofree,32,32,4)).Level == 52 &&
            lofree.Outputs.Count(b => b[3] == 2) == 5,"Lofree session always stops");
        var lofreeFlood = new Input(_ => []) { Flood = true };
        Check(Reject(() => KeyboardExpansionLofree.Read(Wire(lofreeFlood,32,32,4))) &&
            lofreeFlood.Outputs.Count == 0, "Lofree does not stop a session whose start was never attempted");
        var pmoFlood = new Input(packet =>
        {
            if (packet[2] != 0xD0) return [];
            return new[] { Frame(32,(0,0xAA),(1,0xD0),(9,0xB0),(10,0x36),(11,9),(12,0x30),(13,4)) }
                .Concat(Enumerable.Range(0,32).Select(_ => new byte[32]));
        });
        Check(Reject(() => KeyboardExpansionPmo.Read(Wire(pmoFlood,32,33))) &&
            pmoFlood.Outputs.All(b => b[2] == 0xD0),
            "PMO does not release a session whose initial queue failed");

        var f = KeyboardExpansionIpi.Fragment(5,1,7,new byte[] {1,2,3},20);
        Check(KeyboardExpansionIpi.ParseFragment(f,20) is {Total:5,Current:1,Sync:7} &&
            KeyboardExpansionIpi.ParseFragment(f,20).Payload.SequenceEqual(new byte[] {1,2,3}),
            "IPI fragment counters and checksum");
        f[5] ^= 1;
        Check(Reject(() => KeyboardExpansionIpi.ParseFragment(f,20)),"IPI rejects fragment checksum corruption");

        var channel = new HidDiscovery.VendorCollectionInfo
        {
            Path = @"\\?\hid#vid_0c45&pid_fefc&mi_03&col01#test",Vid = 0x0C45,Pid = 0xFEFC,
            Product = "AULA F87ProV2C Dongle",UsagePage = 0xFF68,Usage = 0x61,Input = 33,Output = 33
        };
        var profile = KeyboardExpansionProfiles.Find(channel.Vid,channel.Pid,channel.Product);
        Check(profile != null && profile.Matches(channel),"exact HUB collection");
        channel.Output = 65;
        Check(!profile!.Matches(channel),"HUB rejects unequal report capacities");
        channel.Output = 33; channel.Product = "AULA F87ProV2D Dongle";
        Check(!profile.Matches(channel),"opened shared-ID product replacement rejected");
        Check(!KeyboardExpansionProfiles.Find(0x3837,0x201B,"MCHOSE 2.4G Wireless")!
            .RevisionAllowed(0x0120,"MCHOSE 2.4G Wireless"),"generic G87 1K name requires documented revision");
        return checks;
    }

    private sealed class Input(Func<byte[],IEnumerable<byte[]>> respond) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> queue = new();
        private byte[] featureRequest = [];
        internal Func<byte[],byte[]>? Feature;
        internal bool Flood;
        internal readonly List<byte[]> Outputs = new();
        public long Now { get; private set; }
        public byte[]? Read(int timeoutMs)
        {
            Now += Math.Max(1,queue.Count == 0 ? timeoutMs : 1);
            return queue.Count == 0 ? Flood ? new byte[32] : null : queue.Dequeue();
        }
        public bool Write(byte[] request,int timeoutMs)
        {
            Outputs.Add((byte[])request.Clone());
            foreach (var b in respond(request)) queue.Enqueue(b);
            return true;
        }
        public bool SetFeature(byte[] request) { featureRequest = request; return true; }
        public byte[]? GetFeature(byte reportId,int length) => Feature?.Invoke(featureRequest);
        public void Delay(int milliseconds) => Now += milliseconds;
    }
}
