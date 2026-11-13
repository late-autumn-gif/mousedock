using System.Reflection;
using MouseDock.Services.Detection;

internal static class DpiProtocolRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string why)
        {
            checks++;
            if (!value) throw new Exception("DPI: " + why);
        }
        var assembly = typeof(DpiSnapshot).Assembly;
        var profiles = assembly.GetType("MouseDock.Services.Detection.DpiProfiles");
        Check(profiles != null, "reviewed DPI profiles must exist before settings can be opened");
        var find = profiles!.GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? Find(ushort vid, ushort pid) => find.Invoke(null, new object[] { vid, pid });
        Check(Find(0x3554, 0xF517) != null, "the reviewed Z7 route is available");
        Check(Find(0x3837, 0x100B) == null, "MCHOSE battery support does not grant settings writes");
        Check(Find(0x1915, 0xAE8C) == null, "a different Sora generation does not inherit DPI support");
        Check(Find(0x373B, 0x1155) == null, "Nordic Zero cannot enter the COMPX settings route");
        Check(Convert.ToHexString(DpiProtocol.BuildCompx(8, 12, 4)) == "080800000C040000000000000000000035",
            "memory reads use reviewed report ID, big endian address, exact count and checksum");
        foreach (var row in new[]
        {
            ("atk",800,1600,"4F9F0067"), ("master",800,800,"1F031F030011"),
            ("z7",800,800,"0F0F0037"), ("z7",60000,60000,"5757990E"),
            ("step50",800,800,"0F0F0037"), ("se",800,800,"12120031"),
            ("3955",801,801,"20032003000F")
        })
        {
            var bytes = DpiProtocol.EncodeStage(row.Item2,row.Item3,row.Item1);
            Check(Convert.ToHexString(bytes)==row.Item4,"literal stage encoding "+row.Item1);
            Check(DpiProtocol.DecodeStage(Convert.FromHexString(row.Item4),row.Item1)==new DpiStage(row.Item2,row.Item3),
                "literal stage decoding "+row.Item1);
        }
        foreach(string codec in new[]{"atk","ultra","master","z7","step50","se","x1","3950","3955"})
        {
            for(int value=0;value<=60500;value+=50)
            {
                byte[]? encoded=null;
                try { encoded=DpiProtocol.EncodeStage(value,800,codec); }
                catch(ArgumentException) { }
                if(encoded!=null)Check(DpiProtocol.DecodeStage(encoded,codec)==new DpiStage(value,800),"codec roundtrip "+codec);
            }
            var damaged=DpiProtocol.EncodeStage(800,800,codec);damaged[^1]++;
            Check(Throws(()=>DpiProtocol.DecodeStage(damaged,codec)),"corrupt stage checksum is rejected "+codec);
        }
        Check(Throws(()=>DpiProtocol.EncodeStage(30050,800,"atk")),"ATK over 30000 requires step 100");
        Check(Throws(()=>DpiProtocol.EncodeStage(10050,800,"se")),"SE high range does not accept step 50");
        Check(Throws(()=>DpiProtocol.EncodeStage(801,800,"master")),"ATK master step is ten");
        Check(DpiProtocol.DecodeStage(DpiProtocol.EncodeStage(801,800,"3955"),"3955").X==801,
            "Pulsar 3955 uses its own one-DPI step");
        Check(DpiProfiles.PulsarSensor(150)==null && DpiProfiles.PulsarSensor(0)==null,"unknown Pulsar sensor models remain unavailable");

        var profile=DpiProfiles.Find(0x373B,0x11D8)!;
        var link=new MemoryLink();
        var state=DpiProtocol.Read(profile,link);
        Check(state.Active==0 && state.Stages.SequenceEqual(new[]{new DpiStage(800,800),new DpiStage(1600,1600)}),
            "full device snapshot preserves order and active index");
        var target=state.Target(1,1200);
        Check(state.Stages[1].X==1600 && target.Active==0 && target.Stages[1].X==1200,
            "editing an inactive stage leaves original snapshot and active stage unchanged");
        Check(state.Target(1,null).Active==1 && state.Target(1,null).Stages.SequenceEqual(state.Stages),
            "switching selects a stage without altering its values");
        var applied=DpiProtocol.Apply(profile,link,state,1,1200,()=>{});
        Check(applied.Active==0 && applied.Stages[0].X==800 && applied.Stages[1].X==1200,
            "apply rereads the preserved active stage and complete updated table");
        Check(link.Edits.Count==1 && Convert.ToHexString(link.Edits[0].AsSpan(6,8))=="4F4F00B777770067",
            "paired writes preserve the other record's original bytes");

        link=new MemoryLink();
        state=DpiProtocol.Read(profile,link);
        link.Memory[12]=0x63;link.Memory[13]=0x63;link.Memory[15]=0x8F;
        Check(Throws(()=>DpiProtocol.Apply(profile,link,state,1,1200,()=>{})) && link.Edits.Count==0,
            "changed snapshot prevents every settings write");
        link=new MemoryLink();
        state=DpiProtocol.Read(profile,link);
        Check(Throws(()=>DpiProtocol.Apply(profile,link,state,1,1200,()=>throw new InvalidOperationException("disabled"))) &&
            link.Edits.Count==0,"policy recheck blocks a write after snapshot acquisition");
        link=new MemoryLink {IgnoreEdits=true};
        state=DpiProtocol.Read(profile,link);
        Check(Throws(()=>DpiProtocol.Apply(profile,link,state,1,1200,()=>{})) && link.Edits.Count==1,
            "missing readback confirmation fails once without retrying a settings write");
        link=new MemoryLink {Cid=99};
        Check(Throws(()=>DpiProtocol.Read(profile,link)) && link.Edits.Count==0,"unknown runtime identity grants no settings access");
        var readOnly=new DpiSnapshot(DpiProfiles.Find(0x3554,0xF58E)!,0,new[]{new DpiStage(800,800)},"2,32|0055|",200,18000,50);
        Check(!readOnly.Writable && Throws(()=>readOnly.Target(0,1200)),"SE+ receiver remains read-only despite related wired support");
        return checks;
    }

    private static bool Throws(Action action)
    {
        try { action();return false; }
        catch(Exception error) when(error is ArgumentException or InvalidOperationException or System.IO.IOException or System.IO.InvalidDataException or TimeoutException)
        { return true; }
    }

    private sealed class MemoryLink : IDpiLink
    {
        internal byte[] Memory { get; }=new byte[7000];
        internal List<byte[]> Edits { get; }=new();
        internal bool IgnoreEdits;
        internal byte Cid=1,Mid=26;
        private readonly Queue<byte[]> replies=new();
        public long Now { get; private set; }
        internal MemoryLink()
        {
            Convert.FromHexString("005502530055").CopyTo(Memory,0);
            Convert.FromHexString("4F4F00B79F9F0017").CopyTo(Memory,12);
        }
        public byte[] Read(int ms)
        { Now+=Math.Max(1,ms);return replies.Count>0?replies.Dequeue():null!; }
        public bool Write(byte[] packet,int ms)
        {
            var b=new byte[17];b[0]=8;b[1]=packet[1];
            if(packet[1]==16){b[5]=2;b[6]=Cid;b[7]=Mid;}
            else
            {
                int address=packet[3]*256+packet[4],count=packet[5];
                b[3]=packet[3];b[4]=packet[4];b[5]=packet[5];
                if(packet[1]==7)
                {
                    Edits.Add(packet.ToArray());
                    if(!IgnoreEdits)packet.AsSpan(6,count).CopyTo(Memory.AsSpan(address,count));
                }
                Memory.AsSpan(address,count).CopyTo(b.AsSpan(6,count));
            }
            b[16]=unchecked((byte)(85-b.Take(16).Sum(x=>(int)x)));
            replies.Enqueue(b);return true;
        }
        public bool SetFeature(byte[] packet)=>throw new NotSupportedException();
        public byte[] GetFeature(byte id,int count)=>throw new NotSupportedException();
        public void Delay(int ms)=>Now+=ms;
    }
}
