using MouseDock.Services.Detection;

internal static class DpiDeviceRegression
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string why){checks++;if(!value)throw new Exception("DPI device: "+why);}
        Check(Convert.ToHexString(DpiProtocol.BuildCompx(16,count:2))=="081000000000000000000000000000003D",
            "identity query carries no memory-read count");
        Check(Fails(()=>DpiProtocol.BuildPulsar(7,2,2,new byte[]{1,84}))&&
            Fails(()=>DpiProtocol.BuildPulsar(7,0,6,new byte[6]))&&
            Fails(()=>DpiProtocol.BuildPulsar(8,12,8))&&
            Fails(()=>DpiProtocol.BuildPulsar(16)),
            "Pulsar packet builder rejects system/count writes, paired rows and unrelated commands");
        foreach(var profile in DpiProfiles.All.Where(p=>p.Family is DpiFamily.Catalog or DpiFamily.Atk or DpiFamily.Z7))
        {
            var link=new CompxLink(profile);
            var before=DpiProtocol.Read(profile,link);
            Check(before.Stages.SequenceEqual(new[]{new DpiStage(800,800),new DpiStage(1600,1600)}),
                profile.Model+" reads the complete ordered table");
            if(!profile.WriteApproved)
            {Check(!before.Writable&&Fails(()=>DpiProtocol.Apply(profile,link,before,1,1200,()=>{}))&&link.Edits.Count==0,
                "SE+ receiver remains read-only");continue;}
            var after=DpiProtocol.Apply(profile,link,before,1,1200,()=>{});
            Check(after.Active==0&&after.Stages[0].X==800&&after.Stages[1].X==1200,
                profile.Model+" saves inactive stage and preserves current/neighbor");
            Check(link.Edits.Count==1,profile.Model+" performs no automatic retry of a settings write");
            after=DpiProtocol.Apply(profile,link,after,1,null,()=>{});
            Check(after.Active==1&&after.Stages[0].X==800&&after.Stages[1].X==1200,
                profile.Model+" switches with no numeric edits");
            var edit=link.Edits[^1];
            if(profile.Codec=="step50"&&!profile.Wired)
                Check(edit[3]==0&&edit[4]==0&&edit[5]==10&&edit.AsSpan(6,4).SequenceEqual(new byte[]{0,85,2,83})&&
                    edit.AsSpan(12,4).SequenceEqual(new byte[]{91,92,93,94}),
                    "wireless R1 switch preserves the complete ten-byte system record");
            else Check(edit[3]==0&&edit[4]==4&&edit[5]==2,"switch touches only the active-stage pair");
        }
        foreach(byte mid in new byte[]{47,62})
        {
            var profile=DpiProfiles.Find(0x373B,0x1278)!;var link=new CompxLink(profile){Mid=mid};
            link.SetRows("master");var before=DpiProtocol.Read(profile,link);
            var after=DpiProtocol.Apply(profile,link,before,1,1210,()=>{});
            Check(after.Stages[1].X==1210&&link.Edits.Single()[3]==27&&link.Edits.Single()[4]==6&&link.Edits.Single()[5]==6,
                "ATK master identity selects six-byte records at 6912");
        }
        foreach(byte mid in new byte[]{1,68,101})
        {
            var profile=DpiProfiles.Find(0x3710,21510)!;var link=new CompxLink(profile){Mid=mid};
            string sensor=DpiProfiles.PulsarSensor(mid)!;link.SetRows(sensor);
            var before=DpiProtocol.Read(profile,link);
            Check(link.Releases==1&&before.Context==$"87,{mid},1,2","Pulsar read holds/releases and binds runtime identity plus profile");
            int value=mid==101?1201:1200;
            var after=DpiProtocol.Apply(profile,link,before,1,value,()=>{});
            Check(after.Stages[0].X==800&&after.Stages[1].X==value&&after.Active==0&&link.Edits.Count==1,
                "Pulsar "+sensor+" writes only its selected record");
            var edit=link.Edits[0];
            Check(edit[5]==(mid==101?6:4)&&edit[3]*256+edit[4]==(mid==101?6918:16)&&link.Releases==2,
                "Pulsar record address follows approved runtime sensor");
            after=DpiProtocol.Apply(profile,link,after,1,null,()=>{});
            Check(after.Active==1&&link.Edits[^1][4]==4&&link.Edits[^1][5]==2&&link.Releases==3,
                "Pulsar switches only the active pair and releases session");
            link=new CompxLink(profile){Mid=150};
            Check(Fails(()=>DpiProtocol.Read(profile,link))&&link.Edits.Count==0&&link.Releases==1,
                "unknown Pulsar identity fails closed and releases the receiver");
        }

        var scyrox=DpiProfiles.Find(0x395A,0x0006)!;var sc=new ScyroxLink();
        var snapshot=DpiProtocol.Read(scyrox,sc);
        var saved=DpiProtocol.Apply(scyrox,sc,snapshot,1,1200,()=>{});
        Check(saved.Active==0&&saved.Stages[0].X==800&&saved.Stages[1].X==1200&&sc.Edits==1,
            "Scyrox whole-table command preserves other stages and the active index");
        saved=DpiProtocol.Apply(scyrox,sc,saved,1,null,()=>{});
        Check(saved.Active==1&&saved.Stages[1].X==1200,"Scyrox switching preserves values");
        sc=new ScyroxLink{Advanced=1};snapshot=DpiProtocol.Read(scyrox,sc);
        Check(!snapshot.Writable&&Fails(()=>DpiProtocol.Apply(scyrox,sc,snapshot,0,1200,()=>{}))&&sc.Edits==0,
            "Scyrox advanced sensitivity mode is read-only");
        sc=new ScyroxLink{Corrupt=true};
        Check(Fails(()=>DpiProtocol.Read(scyrox,sc)),"Scyrox corrupt reports are rejected");

        var sora=DpiProfiles.Find(0x1915,0xAE8A)!;var so=new SoraLink();
        snapshot=DpiProtocol.Read(sora,so);saved=DpiProtocol.Apply(sora,so,snapshot,1,1200,()=>{});
        Check(saved.Active==0&&saved.Stages[0].X==800&&saved.Stages[1].X==1200&&so.Edits.Single()[1]==5,
            "Sora table save preserves current stage and other values");
        saved=DpiProtocol.Apply(sora,so,saved,1,null,()=>{});
        Check(saved.Active==1&&so.Edits[^1][1]==14&&so.Edits[^1][9]==1,"Sora switch uses the approved feature command");
        so=new SoraLink{Values=new[]{800,31000}};snapshot=DpiProtocol.Read(sora,so);
        Check(!snapshot.Writable&&snapshot.Stages[1].X==31000&&Fails(()=>DpiProtocol.Apply(sora,so,snapshot,0,1200,()=>{})),
            "Sora can read high existing values without permitting a destructive rewrite");
        so=new SoraLink{Corrupt=true};Check(Fails(()=>DpiProtocol.Read(sora,so)),"Sora checksum mismatch cannot create a writable snapshot");

        var rapoo=DpiProfiles.Find(0x24AE,0x1453)!;var ra=new RapooLink();
        snapshot=DpiProtocol.Read(rapoo,ra);saved=DpiProtocol.Apply(rapoo,ra,snapshot,1,1200,()=>{});
        Check(saved.Active==0&&saved.Stages[0]==new DpiStage(800,900)&&saved.Stages[1]==new DpiStage(1200,1200)&&ra.Edits.Count==2,
            "Rapoo saves both selected axes and preserves asymmetric neighboring stage");
        Check(ra.Edits.All(b=>b[2]==165&&b[3]==2)&&ReadLe32(ra.Edits[0],4)==2186&&ReadLe32(ra.Edits[1],4)==2250,
            "Rapoo only writes selected two-byte X and Y table slots");
        saved=DpiProtocol.Apply(rapoo,ra,saved,1,null,()=>{});
        Check(saved.Active==1&&ra.Edits[^1][3]==1&&ReadLe32(ra.Edits[^1],4)==2200,"Rapoo switches a single active-stage byte");
        ra=new RapooLink{Partial=true};snapshot=DpiProtocol.Read(rapoo,ra);
        Check(!snapshot.Writable&&snapshot.Current==new DpiStage(1200,1300)&&snapshot.Context=="live:2",
            "Rapoo passive fallback exposes a read-only current pair without inventing a full table");
        return checks;
    }
    private static bool Fails(Action action)
    {try{action();return false;}catch(Exception e) when(e is InvalidOperationException or ArgumentException or System.IO.IOException or System.IO.InvalidDataException or TimeoutException){return true;}}
    private static int ReadLe32(byte[] b,int offset)=>BitConverter.ToInt32(b,offset);
    private abstract class Link : IDpiLink
    {
        protected readonly Queue<byte[]> Replies=new();
        public long Now{get;protected set;}
        public virtual byte[] Read(int ms){Now+=Math.Max(1,ms);return Replies.Count>0?Replies.Dequeue():null!;}
        public abstract bool Write(byte[] b,int ms);
        public virtual bool SetFeature(byte[] b)=>throw new NotSupportedException();
        public virtual byte[] GetFeature(byte id,int length)=>throw new NotSupportedException();
        public void Delay(int ms)=>Now+=ms;
    }
    private sealed class CompxLink : Link
    {
        private readonly DpiProfile profile;
        internal byte Mid;
        internal int Releases;
        internal readonly List<byte[]> Edits=new();
        private readonly byte[] memory=new byte[7000];
        internal CompxLink(DpiProfile profile)
        {
            this.profile=profile;Mid=(byte)(profile.Family==DpiFamily.Pulsar?1:profile.Family==DpiFamily.Catalog?profile.Mid:26);
            Convert.FromHexString("0055025300555B5C5D5E").CopyTo(memory,0);
            SetRows(profile.Family==DpiFamily.Z7?"z7":profile.Codec.Length>0?profile.Codec:profile.Family==DpiFamily.Pulsar?"x1":"atk");
        }
        internal void SetRows(string codec)
        {
            int start=codec is "master" or "3955"?6912:12,size=codec is "master" or "3955"?6:4;
            DpiProtocol.EncodeStage(800,800,codec).CopyTo(memory,start);DpiProtocol.EncodeStage(1600,1600,codec).CopyTo(memory,start+size);
        }
        public override bool Write(byte[] q,int ms)
        {
            var b=new byte[17];b[0]=8;b[1]=q[1];
            if(q[1]==3)
            {if(q[6]==0){Releases++;return true;}b[5]=1;b[6]=1;}
            else if(q[1]==14){b[5]=1;b[6]=2;}
            else if(q[1]==1)
            {b[5]=8;q.AsSpan(6,4).CopyTo(b.AsSpan(6));b[10]=(byte)(profile.Family==DpiFamily.Z7?41:87);b[11]=profile.Family==DpiFamily.Z7?(byte)8:Mid;b[12]=profile.Family==DpiFamily.Z7?(byte)5:(byte)1;}
            else if(q[1]==16){b[5]=2;b[6]=(byte)(profile.Family==DpiFamily.Catalog?profile.Cid:1);b[7]=Mid;}
            else
            {
                int address=q[3]*256+q[4],count=q[5];q.AsSpan(3,3).CopyTo(b.AsSpan(3));
                if(q[1]==7){Edits.Add(q.ToArray());q.AsSpan(6,count).CopyTo(memory.AsSpan(address,count));}
                memory.AsSpan(address,count).CopyTo(b.AsSpan(6));
            }
            b[16]=unchecked((byte)(85-b.Take(16).Sum(v=>(int)v)));Replies.Enqueue(b);return true;
        }
    }
    private sealed class ScyroxLink : Link
    {
        internal byte Advanced;internal bool Corrupt;internal int Edits;private byte active;private int[] values={800,1600};
        public override bool Write(byte[] q,int ms)
        {
            if(q[1]==10){Edits++;active=q[4];values=Enumerable.Range(0,q[5]).Select(i=>q[6+i*2]|q[7+i*2]<<8).ToArray();return true;}
            var b=new byte[64];b[0]=8;b[1]=1;b[3]=1;b[4]=b[5]=1;b[6]=active;b[7]=(byte)values.Length;
            for(int i=0;i<values.Length;i++){b[8+i*2]=(byte)values[i];b[9+i*2]=(byte)(values[i]>>8);}
            b[63]=unchecked((byte)b.Take(63).Sum(x=>(int)x));if(Corrupt)b[63]++;
            Replies.Enqueue(b);Replies.Enqueue(DpiProtocol.ScyroxPacket(36,0,1,Advanced));Replies.Enqueue(DpiProtocol.ScyroxPacket(1,0,0));return true;
        }
    }
    private sealed class SoraLink : Link
    {
        internal int[] Values={800,1600};internal bool Corrupt;internal readonly List<byte[]> Edits=new();private byte active;
        public override bool Write(byte[] b,int ms)=>throw new NotSupportedException();
        public override bool SetFeature(byte[] q)
        {
            if(q[1] is 5 or 14)
            {
                Edits.Add(q.ToArray());if(q[1]==14)active=q[9];
                else Values=Enumerable.Range(0,q[9]).Select(i=>((q[10+i*2]|q[11+i*2]<<8)+1)*50).ToArray();
            }
            return true;
        }
        public override byte[] GetFeature(byte id,int length)
        {
            var b=new byte[length];b[0]=id;b[1]=id==5?(byte)13:(byte)38;
            if(id==5){b[9]=1;return b;}
            b[20]=(byte)Values.Length;b[29]=active;
            for(int i=0;i<Values.Length;i++){int raw=Values[i]/50-1;b[21+i*2]=(byte)raw;b[22+i*2]=(byte)(raw>>8);}
            int sum=b.Take(71).Sum(v=>(int)v);b[72]=(byte)sum;b[73]=(byte)(sum>>8);if(Corrupt)b[72]++;return b;
        }
    }
    private sealed class RapooLink : Link
    {
        internal bool Partial;internal readonly List<byte[]> Edits=new();private byte[] response=Array.Empty<byte>();
        private readonly byte[] memory=new byte[10000];
        internal RapooLink()
        {
            memory[2198]=1;memory[2200]=0;
            foreach(var row in new[]{(2184,800),(2186,1600),(2248,900),(2250,1700)})
            {memory[row.Item1]=(byte)row.Item2;memory[row.Item1+1]=(byte)(row.Item2>>8);}
        }
        public override bool Write(byte[] q,int ms)
        {
            if(Partial)throw new System.IO.IOException("missing settings sibling");
            int address=ReadLe32(q,4),count=q[3];response=new byte[33];response[0]=1;
            if(q[2]==165){Edits.Add(q.ToArray());q.AsSpan(8,count).CopyTo(memory.AsSpan(address));}
            memory.AsSpan(address,count).CopyTo(response.AsSpan(4));return true;
        }
        public override byte[] GetFeature(byte id,int count)=>response;
        public override byte[] Read(int ms)
        {
            if(!Partial)return base.Read(ms);
            Now+=ms;var b=new byte[19];b[0]=7;b[1]=32;b[2]=2;b[3]=176;b[4]=4;b[5]=20;b[6]=5;b[7]=1;return b;
        }
    }
}
