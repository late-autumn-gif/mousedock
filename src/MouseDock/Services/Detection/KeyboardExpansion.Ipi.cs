using System;
using System.Collections.Generic;
using System.Linq;
using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionIpi
{
    internal static byte[] Request(byte command)
    {
        Require(command is 0x82 or 0x87,"Not an IPI battery query");
        var b = Packet(64,9,command,(byte)(command == 0x82 ? 1 : 0),0,1,0,
            (byte)(command == 0x82 ? 6 : 0));
        int sum = b.Sum(v => (int)v);
        b[63] = (byte)(255 - sum % 256);
        return b;
    }
    internal static byte[] Fragment(int total,int current,int sync,byte[] payload,int size)
    {
        Require(current >= 1 && current <= total && total <= 5 && payload.Length <= 14 &&
            sync is >= 0 and <= 7 && size is >= 20 and <= 65,"Invalid IPI fragment");
        var b = Packet(size,9,102,(byte)(total | (sync & 4) << 5),(byte)(current | (sync & 2) << 6),
            (byte)(payload.Length | (sync & 1) << 7));
        payload.CopyTo(b,5);
        int sum = 0;
        for (int i = 1; i < 5 + payload.Length; i++) sum += b[i];
        b[5 + payload.Length] = (byte)sum;
        return b;
    }
    internal static (int Total,int Current,int Sync,byte[] Payload) ParseFragment(byte[] b,int size)
    {
        Require(b.Length == size && b[0] == 9 && b[1] == 102,"Invalid IPI fragment framing");
        int total = b[2] & 127,current = b[3] & 127,length = b[4] & 127;
        int sync = (b[2] & 128) >> 5 | (b[3] & 128) >> 6 | b[4] >> 7;
        Require(current >= 1 && current <= total && total <= 5 && length <= 14,"Invalid IPI fragment counters");
        int sum = 0;
        for (int i = 1; i < 5 + length; i++) sum += b[i];
        Require((sum & 255) == b[5 + length],"IPI fragment checksum mismatch");
        return (total,current,sync,b.AsSpan(5,length).ToArray());
    }
    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        int sync = Random.Shared.Next(8);
        byte[] Exchange(byte command)
        {
            wire.Drain();
            var packet = Request(command);
            var parts = Enumerable.Range(0,5).Select(i => packet.AsSpan(i * 14,Math.Min(14,64 - i * 14)).ToArray()).ToArray();
            var received = new List<byte>();
            byte[] complete = null,lastPayload = null;
            int lastTotal = 0,lastCurrent = 0,lastSync = 0,reads = 0,sending = 0;
            long deadline = wire.Link.Now + 2000;
            void Send(byte[] b)
            {
                Require(wire.Link.Write(b,250),"IPI fragment short write");
                wire.Trace?.Add("keyboard-output",true,raw:b,keep:true);
            }
            bool Receive((int Total,int Current,int Sync)? ack)
            {
                wire.CheckTime();
                Require(++reads <= 96 && wire.Link.Now < deadline,"IPI fragment deadline expired");
                var raw = wire.Link.Read(50);
                if (raw == null || raw.Length == 0 || raw[0] != 9) return false;
                wire.Trace?.Add("keyboard-input",true,raw:raw,keep:true);
                Require(!(raw.Length >= 7 && raw[1] == 10 && raw[5] == 2 && raw[6] == 0),"IPI keyboard disconnected");
                if (raw.Length < 2 || raw[1] != 102) return false;
                var f = ParseFragment(raw,wire.Input);
                if (f.Payload.Length == 0) return ack.HasValue &&
                    ack.Value == (f.Total,f.Current,f.Sync);
                if (sending < parts.Length) return false;
                if (lastPayload != null && f.Total == lastTotal && f.Current == lastCurrent &&
                    f.Sync == lastSync && f.Payload.AsSpan().SequenceEqual(lastPayload))
                { Send(Fragment(f.Total,f.Current,f.Sync,[],wire.Output)); return false; }
                Require(complete == null && f.Current == lastCurrent + 1 &&
                    (lastTotal == 0 || f.Total == lastTotal),"IPI fragments out of order or changed");
                received.AddRange(f.Payload);
                Require(received.Count <= 63,"IPI business response too long");
                lastTotal = f.Total; lastCurrent = f.Current; lastSync = f.Sync; lastPayload = f.Payload;
                Send(Fragment(f.Total,f.Current,f.Sync,[],wire.Output));
                if (f.Current == f.Total)
                {
                    var b = received.ToArray();
                    if (b.Length == 63 && b[0] != command)
                    { received.Clear(); lastPayload = null; lastTotal = lastCurrent = 0; return false; }
                    int sum = 9 + b.Sum(v => (int)v);
                    Require(b.Length == 63 && b.AsSpan(0,5).SequenceEqual(packet.AsSpan(1,5)) &&
                        (sum & 255) == 255 && !b.AsSpan().SequenceEqual(packet.AsSpan(1)) &&
                        b[5] == (command == 0x82 ? 6 : 2),"Invalid IPI business response");
                    complete = b.AsSpan(6,b[5]).ToArray();
                }
                return false;
            }
            for (int i = 0; i < parts.Length; i++)
            {
                sending = i + 1; sync = (sync + 1) & 7;
                Send(Fragment(parts.Length,sending,sync,parts[i],wire.Output));
                while (!Receive((parts.Length,sending,sync))) { }
            }
            while (complete == null) Receive(null);
            return complete;
        }
        void Identity() => Require(Exchange(0x82).AsSpan().SequenceEqual(
            new byte[] {0x12,0,0,0,0,3}),"IPI Aurora UUID mismatch");
        Identity();
        int level = -1;
        for (int i = 0; i < 2; i++)
        {
            var b = Exchange(0x87);
            Require(b.Length == 2 && b[0] is >= 1 and <= 100,"IPI numeric battery unavailable");
            level = b[0];
        }
        Identity();
        return Value(level,model:"IPI Aurora 65W",identity:"120000000003");
    }
}
