// DPI protocol adaptation from MouseDock 1.0.3 (MIT, Copyright (c) 2026 MouseDock contributors).
// The complete reference permission notice is embedded through THIRD-PARTY-NOTICES.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MouseDock.Services.Detection;

/// <summary>Reviewed DPI memory/feature operations, adapted from the MIT 1.0.3 reference.</summary>
internal static class DpiProtocol
{
    private static readonly HashSet<int> seSkip = new(new[]
    {
        7,13,20,26,33,40,46,53,60,66,73,80,86,93,100,106,113,120,126,133,
        140,146,153,160,166,173,180,186,193,200,206,213,220,226,233
    });
    private static readonly int[] seCodes = Enumerable.Range(1, 235).Where(x => !seSkip.Contains(x)).ToArray();
    private static Exception Invalid(string text = "DPI 回复校验失败") => new InvalidDataException(text);
    private static int Le(byte[] b, int i) => b[i] | b[i + 1] << 8;
    private static void Put(byte[] b, int i, int value) { b[i] = (byte)value; b[i + 1] = (byte)(value >> 8); }
    private static bool Sum55(IEnumerable<byte> data) => (data.Sum(x => (int)x) & 255) == 85;
    private static byte Checksum(IEnumerable<byte> data) => unchecked((byte)(85 - data.Sum(x => (int)x)));

    internal static byte[] BuildCompx(byte command, int address = -1, int count = 0, byte[] data = null)
    {
        if (count is < 0 or > 10 || data != null && data.Length != count ||
            command is not (1 or 3 or 7 or 8 or 14 or 16) ||
            command is 7 or 8 && address is < 0 or > 65535)
            throw new ArgumentException("未开放的 DPI 请求");
        var b = new byte[17]; b[0] = 8; b[1] = command;
        if (address >= 0) { b[3] = (byte)(address >> 8); b[4] = (byte)address; }
        b[5] = command == 16 ? (byte)0 : (byte)count;
        if (data != null) data.CopyTo(b, 6);
        b[16] = Checksum(b.Take(16)); return b;
    }

    internal static byte[] BuildPulsar(byte command,int address=-1,int count=0,byte[] data=null)
    {
        bool slot=address==2&&count==2||address==4&&count==2||
            address>=12&&address<=40&&(address-12)%4==0&&count==4||
            address>=6912&&address<=6954&&(address-6912)%6==0&&count==6;
        if(command is 7 or 8)
        {
            if(!slot||command==7&&(address==2||data?.Length!=count)||command==8&&data!=null)
                throw new ArgumentException("未开放的 Pulsar DPI 地址");
        }
        else if(address!=-1||command==1&&(count!=8||data?.Length!=8)||
            command is 3 or 14&&(count!=0||data!=null)||command is not (1 or 3 or 14))
            throw new ArgumentException("未开放的 Pulsar DPI 请求");
        return BuildCompx(command,address,count,data);
    }

    private static void Drain(IDpiLink link)
    {
        for (int i = 0; i < 32; i++) if (link.Read(1) is not { Length: > 0 }) return;
        throw Invalid("旧输入队列未清空，已取消 DPI 操作");
    }

    private static void Send(IDpiLink link, byte[] b)
    {
        if (!link.Write(b, 400)) throw new IOException("DPI 命令未完整发送");
    }

    private static byte[] Exchange(IDpiLink link, byte command, int address = -1, int count = 0,
        byte[] data = null, bool pulsar = false)
    {
        Drain(link);
        byte[] query = pulsar?BuildPulsar(command,address,count,data):BuildCompx(command, address, count, data);
        Send(link, query);
        long end = link.Now + (pulsar ? 600 : 1500);
        while (link.Now < end)
        {
            var b = link.Read((int)Math.Min(pulsar ? 50 : 100, end - link.Now));
            if (b == null || b.Length != 17 || b[0] != 8 || b[1] != command || b[2] != 0 || !Sum55(b)) continue;
            if (command != 7 && b.SequenceEqual(query)) continue;
            if (address >= 0 && (b[3] != query[3] || b[4] != query[4] || b[5] != count)) continue;
            if (address < 0 && command is 1 or 16 && (b[5] < count || b[5] > 10)) continue;
            if (command == 7 && data != null && !b.AsSpan(6, count).SequenceEqual(data)) continue;
            return b;
        }
        throw new TimeoutException("DPI 读取超时，请唤醒鼠标并退出原厂驱动后重新读取");
    }

    private static byte[] At(IDpiLink link, int address, int count, bool pulsar = false) =>
        Exchange(link, 8, address, count, pulsar: pulsar).AsSpan(6, count).ToArray();

    internal static byte[] EncodeStage(int x, int y, string codec)
    {
        bool wide = codec is "master" or "3955";
        var b = new byte[wide ? 6 : 4];
        int flags = 0;
        foreach (var axis in new[] { (Value: x, Axis: 0), (Value: y, Axis: 1) })
        {
            int value = axis.Value, raw, flag = 0, low, high, step;
            switch (codec)
            {
                case "master": low = 50; high = 40000; step = 10; raw = value - 1; break;
                case "3955": low = 50; high = 42000; step = 1; raw = value - 1; break;
                case "z7":
                case "3950":
                    low = 50; high = codec == "z7" ? 60000 : 32000; step = value <= 30000 ? 50 : 100;
                    raw = value / step - 1; flag = raw >> 8 << 2 | (step == 100 ? 1 : 0); break;
                case "step50":
                    low = 100; high = 30000; step = 50; raw = value / 50 - 1; flag = raw >> 8 << 2; break;
                case "se":
                    low = 200; high = 18000; step = value > 10000 ? 100 : 50;
                    int index = value / step - 1;
                    raw = index >= 0 && index < seCodes.Length ? seCodes[index] : -1;
                    flag = value > 10000 ? 2 : 0; break;
                case "atk":
                case "ultra":
                case "x1":
                    low = 10; high = codec == "x1" ? 32000 : 42000;
                    step = value <= 10000 ? 10 : value <= 30000 ? 50 : 100;
                    int baseValue = value > 30000 ? value / 2 : value;
                    raw = baseValue <= 10000 ? baseValue / 10 - 1 : (baseValue - 10050) / 50;
                    flag = raw >> 8 << 2 | (baseValue > 10000 ? 2 : 0) | (value > 30000 ? 1 : 0); break;
                default: throw new ArgumentException("未知的 DPI 编码");
            }
            if (value < low || value > high || value % step != 0 || raw < 0)
                throw new ArgumentException($"当前范围的 DPI 应为 {low}–{high}，步进 {step}");
            if (wide)
            {
                Put(b, axis.Axis * 2, raw);
                if (codec == "3955") flag = raw >> 16 << 2;
            }
            else b[axis.Axis] = (byte)raw;
            flags |= flag << (axis.Axis * 4);
        }
        b[wide ? 4 : 2] = (byte)flags; b[^1] = Checksum(b.Take(b.Length - 1)); return b;
    }

    internal static DpiStage DecodeStage(byte[] b, string codec)
    {
        bool wide = codec is "master" or "3955";
        if (b.Length != (wide ? 6 : 4) || !Sum55(b)) throw Invalid();
        var values = new int[2];
        for (int axis = 0; axis < 2; axis++)
        {
            int flag = b[wide ? 4 : 2] >> (axis * 4) & 15;
            int raw = wide ? Le(b, axis * 2) : b[axis];
            int code = raw | (flag >> 2 << (wide ? 16 : 8));
            values[axis] = codec switch
            {
                "master" when (b[4] & ~17) == 0 => (raw + 1) * ((flag & 1) != 0 ? 2 : 1),
                "3955" => (code + 1) * ((flag & 2) != 0 ? 2 : 1) * ((flag & 1) != 0 ? 2 : 1),
                "z7" or "3950" => (code + 1) * 50 * ((flag & 1) != 0 ? 2 : 1) * ((flag & 2) != 0 ? 2 : 1),
                "step50" when (flag & 3) == 0 => (code + 1) * 50,
                "se" when (flag & ~2) == 0 && Array.IndexOf(seCodes, raw) >= 0 =>
                    (Array.IndexOf(seCodes, raw) + 1) * 50 * (flag == 2 ? 2 : 1),
                "atk" or "ultra" or "x1" =>
                    ((flag & 2) != 0 ? 10050 + 50 * code : 10 * (code + 1)) * ((flag & 1) != 0 ? 2 : 1),
                _ => throw Invalid("未知的 DPI 档位编码")
            };
            if (codec == "se" && flag != 0 && values[axis] <= 10000) throw Invalid();
        }
        byte[] canonical = EncodeStage(values[0], values[1], codec);
        if (codec is "x1" or "3950" or "3955" && !canonical.SequenceEqual(b)) throw Invalid("DPI 编码不规范");
        return new(values[0], values[1]);
    }

    private static string Codec(DpiSnapshot state) => state.Family switch
    {
        DpiFamily.Catalog => state.Profile.Codec,
        DpiFamily.Z7 => "z7",
        DpiFamily.Atk => state.Context.StartsWith("1,47") || state.Context.StartsWith("1,62") ? "master" : "atk",
        DpiFamily.Pulsar => DpiProfiles.PulsarSensor(int.Parse(state.Context.Split(',')[1])),
        _ => ""
    };

    internal static void ValidateValue(DpiSnapshot state, int value)
    {
        if (state.Family is DpiFamily.Atk or DpiFamily.Z7 or DpiFamily.Catalog or DpiFamily.Pulsar)
            EncodeStage(value, value, Codec(state));
    }

    private static void ValidateSystem(byte[] system, out int count, out int active)
    {
        if (system.Length < 6 || Enumerable.Range(0, 3).Any(i => !Sum55(system.Skip(i * 2).Take(2)))) throw Invalid();
        count = system[2]; active = system[4];
        if (count is < 1 or > 8 || active >= count) throw Invalid("DPI 档位数或当前档位无效");
    }

    private static DpiSnapshot ReadCompx(DpiProfile profile, IDpiLink link)
    {
        byte[] identity = profile.Family == DpiFamily.Z7
            ? Exchange(link, 1, count: 8, data: RandomNumberGenerator.GetBytes(4).Concat(new byte[4]).ToArray()).AsSpan(10, 3).ToArray()
            : Exchange(link, 16, count: 2).AsSpan(6, 2).ToArray();
        string identityText = string.Join(",", identity);
        if (profile.Family == DpiFamily.Z7 && identityText != "41,8,5" ||
            profile.Family == DpiFamily.Atk && identityText is not ("1,4" or "1,26" or "1,47" or "1,62") ||
            profile.Family == DpiFamily.Catalog && (identity[0] != profile.Cid || identity[1] != profile.Mid))
            throw Invalid("鼠标内部型号尚未核对，未开放 DPI 配置");
        bool master = profile.Family == DpiFamily.Atk && identityText is "1,47" or "1,62";
        int systemLength = profile.Family == DpiFamily.Catalog && profile.Codec == "step50" && !profile.Wired ? 10 : 6;
        byte[] system = At(link, 0, systemLength);
        ValidateSystem(system, out int count, out int active);
        string codec = profile.Family == DpiFamily.Z7 ? "z7" : profile.Family == DpiFamily.Catalog ? profile.Codec : master ? "master" : "atk";
        int size = master ? 6 : 4, baseAddress = master ? 6912 : 12;
        var stages = Enumerable.Range(0, count).Select(i => DecodeStage(At(link, baseAddress + i * size, size), codec)).ToArray();
        if (!At(link, 0, systemLength).SequenceEqual(system)) throw Invalid("鼠标档位在读取时变化，请重新读取");
        if (profile.Family == DpiFamily.Catalog &&
            !Exchange(link, 16, count: 2).AsSpan(6, 2).SequenceEqual(identity)) throw Invalid("鼠标身份在读取时变化");
        string context = identityText + (profile.Family == DpiFamily.Catalog
            ? "|" + Convert.ToHexString(system.AsSpan(0, 2)) + "|" + Convert.ToHexString(system.AsSpan(6)) : "");
        int min = master ? 50 : profile.Family == DpiFamily.Z7 ? 50 : profile.Codec == "se" ? 200 : profile.Codec == "step50" ? 100 : 10;
        int max = master ? 40000 : profile.Family == DpiFamily.Z7 ? 60000 : profile.Codec == "se" ? 18000 : profile.Codec == "step50" ? 30000 : 42000;
        int step = profile.Family == DpiFamily.Z7 || profile.Codec is "se" or "step50" ? 50 : 10;
        return new(profile, active, stages, context, min, max, step, note: profile.WriteApproved
            ? $"{min}–{max} DPI；步进随型号和数值范围变化" : "此接收器只支持读取；USB 直连鼠标后可调节");
    }

    internal static byte[] ScyroxPacket(params byte[] payload)
    {
        if (payload.Length > 62) throw new ArgumentException("DPI 数据过长");
        var b = new byte[64]; b[0] = 8; payload.CopyTo(b, 1); b[63] = unchecked((byte)b.Take(63).Sum(x => (int)x)); return b;
    }
    private static bool ValidScyrox(byte[] b) => b is { Length: 64 } && b[0] == 8 && b[62] == 0 &&
        (b.Take(63).Sum(x => (int)x) & 255) == b[63];

    private static DpiSnapshot ReadScyrox(DpiProfile profile, IDpiLink link)
    {
        Drain(link); Send(link, ScyroxPacket(1));
        byte[] config = null; int advanced = -1; bool belongs = false;
        long end = link.Now + 3000;
        while (link.Now < end)
        {
            var b = link.Read((int)Math.Min(100, end - link.Now));
            if (!ValidScyrox(b)) continue;
            if (b[1] == 1)
            {
                if (b[3] == 0) break;
                belongs = b[4] == b[5] && b[4] <= 3;
                if (belongs) { config = b; advanced = -1; }
            }
            else if (belongs && b[1] == 36 && b[3] == 1) advanced = b[4];
        }
        if (config == null || advanced < 0) throw Invalid("未读到完整的 V12 当前配置");
        int count = config[7], active = config[6];
        if (count is < 1 or > 5 || active >= count) throw Invalid();
        var rows = Enumerable.Range(0, count).Select(i => Le(config, 8 + i * 2)).ToArray();
        if (rows.Any(v => v is < 100 or > 30000 || v % 50 != 0)) throw Invalid();
        return new(profile, active, rows.Select(v => new DpiStage(v, v)), $"{config[4]},{advanced}", 100, 30000, 50,
            advanced == 0, advanced == 0 ? "100–30000 DPI，步进 50" : "高级灵敏度模式已开启；基础 DPI 仅供读取");
    }

    private static byte[] Feature(IDpiLink link, byte[] query, byte reportId, int length, int wait)
    {
        if (!link.SetFeature(query)) throw new IOException("DPI Feature 查询未完整发送");
        link.Delay(wait);
        return link.GetFeature(reportId, length) ?? throw Invalid("DPI Feature 应答缺失");
    }

    private static DpiSnapshot ReadSora(DpiProfile profile, IDpiLink link)
    {
        var query = new byte[32]; new byte[] {5,13,0,0,1,0,0,22}.CopyTo(query,0);
        int selected = -1;
        for (int i = 0; i < 2; i++)
        {
            var reply = Feature(link, query, 5, 32, selected == 0 ? 150 : 30);
            if (reply.Length != 32 || reply[0] != 5 || reply[1] != 13 || reply.SequenceEqual(query)) throw Invalid("Sora 配置编号回复无效");
            selected = reply[9]; if (selected != 0) break;
        }
        if (selected is < 0 or > 5) throw Invalid();
        var read = new byte[74]; new byte[] {4,38,0,0,1,0,(byte)selected}.CopyTo(read,0);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!link.SetFeature(read)) throw new IOException("Sora DPI 查询发送失败");
            foreach (int wait in new[] {150,100,200})
            {
                link.Delay(wait); var b = link.GetFeature(4, 74);
                if (b == null || b.Length != 74 || b[0] != 4 || b[1] != 38 || b.SequenceEqual(read)) continue;
                if (b.Take(71).Sum(x => (int)x) != Le(b, 72)) break;
                int count = b[20], active = b[29];
                if (count is < 1 or > 4 || active >= count) throw Invalid();
                var values = Enumerable.Range(0, count).Select(i => (Le(b, 21 + i * 2) + 1) * 50).ToArray();
                if (values.Any(v => v is < 50 or > 32000)) throw Invalid();
                return new(profile, active, values.Select(v => new DpiStage(v, v)), selected.ToString(), 50, 30000, 50,
                    values.All(v => v <= 30000), values.Any(v => v > 30000) ? "现有档位超出写入范围，当前只读" : "50–30000 DPI，步进 50");
            }
        }
        throw Invalid("Sora DPI 配置校验失败");
    }

    internal static byte[] RapooPacket(byte command, int address, int count, byte[] data = null)
    {
        if (command is not (164 or 165) || count is < 1 or > 14 || data != null && data.Length != count)
            throw new ArgumentException("未开放的雷柏 DPI 请求");
        var b = new byte[33]; b[0] = 6; b[1] = 165; b[2] = command; b[3] = (byte)count;
        for (int i = 0; i < 4; i++) b[4 + i] = (byte)(address >> (i * 8));
        if (data != null) data.CopyTo(b, 8); return b;
    }
    private static byte[] RapooResponse(IDpiLink link, int count)
    {
        for (int i = 0; i < 5; i++)
        {
            link.Delay(40); var b = link.GetFeature(8, 33);
            if (b is { Length: 32 or 33 } && b[0] == 1 && b[1] == 0 && b[2] == 0 && b[3] == 0)
                return b.AsSpan(4, count).ToArray();
            if (b is not { Length: 32 or 33 } || b[0] != 2) break;
            link.Delay(60);
        }
        throw Invalid("VT9S 未返回有效 DPI 配置");
    }
    private static byte[] RapooAt(IDpiLink link, int address, int count)
    { Send(link, RapooPacket(164, address, count)); return RapooResponse(link, count); }
    private static DpiSnapshot ReadRapoo(DpiProfile profile, IDpiLink link)
    {
        try
        {
            int selected = RapooAt(link, 268, 1)[0];
            if (selected > 7) throw Invalid();
            int baseAddress = 1536 + selected * 1024;
            int count = RapooAt(link, baseAddress + 662, 1)[0] + 1, active = RapooAt(link, baseAddress + 664, 1)[0];
            if (count is < 1 or > 7 || active >= count) throw Invalid();
            byte[] x = RapooAt(link, baseAddress + 648, count * 2), y = RapooAt(link, baseAddress + 712, count * 2);
            var rows = Enumerable.Range(0, count).Select(i => new DpiStage(Le(x,i*2),Le(y,i*2))).ToArray();
            if (rows.Any(v => v.X is < 50 or > 30000 || v.Y is < 50 or > 30000 || v.X % 10 != 0 || v.Y % 10 != 0) ||
                RapooAt(link,268,1)[0] != selected) throw Invalid("VT9S 配置在读取时变化或数据无效");
            return new(profile,active,rows,selected.ToString(),50,30000,10,note:"50–30000 DPI，步进 10");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or TimeoutException)
        {
            long end = link.Now + 3000;
            while (link.Now < end)
            {
                var b = link.Read((int)Math.Min(100,end-link.Now));
                if (b is not {Length:19} || b[0]!=7 || b[1]!=32 || b[7]!=1 || b[2]>=7) continue;
                int x=Le(b,3), y=Le(b,5);
                if (x is >=50 and <=45000 && y is >=50 and <=45000)
                    return new(profile,0,new[] {new DpiStage(x,y)},"live:"+b[2],50,45000,10,false,
                        $"当前第 {b[2]+1} 档；尚未取得完整配置，当前只读");
            }
            throw Invalid("未读到 VT9S 完整配置或实时 DPI");
        }
    }

    private static byte[] PulsarIdentity(IDpiLink link)
    {
        var b = Exchange(link,1,count:8,data:RandomNumberGenerator.GetBytes(4).Concat(new byte[4]).ToArray(),pulsar:true);
        var id=b.AsSpan(10,3).ToArray();
        if (id[0]!=87 || DpiProfiles.PulsarSensor(id[1])==null || id[2]>5) throw Invalid("接收器连接的 Pulsar 型号尚未确认");
        return id;
    }
    private static DpiSnapshot PulsarSnapshot(DpiProfile profile,IDpiLink link)
    {
        var id=PulsarIdentity(link); string sensor=DpiProfiles.PulsarSensor(id[1]); bool wide=sensor=="3955";
        int selected=Exchange(link,14,pulsar:true)[6];
        var count=At(link,2,2,true); var active=At(link,4,2,true);
        if (!Sum55(count)||!Sum55(active)||count[0] is <1 or >8||active[0]>=count[0]) throw Invalid();
        var rows=Enumerable.Range(0,count[0]).Select(i=>DecodeStage(At(link,(wide?6912:12)+i*(wide?6:4),wide?6:4,true),sensor)).ToArray();
        if (!At(link,2,2,true).SequenceEqual(count)||!At(link,4,2,true).SequenceEqual(active)||
            Exchange(link,14,pulsar:true)[6]!=selected||!PulsarIdentity(link).SequenceEqual(id)) throw Invalid("Pulsar 配置在读取时变化");
        return new(profile,active[0],rows,string.Join(",",id)+","+selected,sensor=="x1"?10:50,
            wide?42000:32000,wide?1:sensor=="x1"?10:50,note:sensor=="3955"?"50–42000 DPI，步进 1":"DPI 步进随数值范围变化");
    }
    private static T PulsarSession<T>(IDpiLink link,Func<T> operation)
    {
        bool holdAttempted=false;
        try
        {
            for(int i=0;i<3;i++)
            {
                var q=BuildPulsar(3); q[6]=1; q[16]=Checksum(q.Take(16));
                Drain(link);holdAttempted=true;Send(link,q);byte[] b=null;long end=link.Now+600;
                while(link.Now<end)
                {
                    var candidate=link.Read((int)Math.Min(50,end-link.Now));
                    if(candidate is {Length:17}&&candidate[0]==8&&candidate[1]==3&&candidate[2]==0&&Sum55(candidate)&&!candidate.SequenceEqual(q)){b=candidate;break;}
                }
                if(b==null)throw new TimeoutException("Pulsar 在线查询超时");
                if(b[10]==1){if(i==2)throw Invalid("Pulsar 接收器忙");continue;}
                if(b[6]!=1)throw Invalid("Pulsar 鼠标离线");
                return operation();
            }
            throw Invalid();
        }
        finally {if(holdAttempted)link.Write(BuildPulsar(3),400);}
    }

    internal static DpiSnapshot Read(DpiProfile profile,IDpiLink link) => profile.Family switch
    {
        DpiFamily.Atk or DpiFamily.Z7 or DpiFamily.Catalog=>ReadCompx(profile,link),
        DpiFamily.Scyrox=>ReadScyrox(profile,link),
        DpiFamily.Sora=>ReadSora(profile,link),
        DpiFamily.Rapoo=>ReadRapoo(profile,link),
        DpiFamily.Pulsar=>PulsarSession(link,()=>PulsarSnapshot(profile,link)),
        _=>throw Invalid("此型号尚未适配 DPI")
    };

    internal static DpiSnapshot Apply(DpiProfile profile,IDpiLink link,DpiSnapshot before,int stage,int? value,Action guard)
    {
        if(profile!=before.Profile)throw Invalid("DPI 设备配置已变化");
        var target=before.Target(stage,value);
        bool attempted=false;
        void Write(byte[] packet,bool feature=false)
        {
            guard(); attempted=true;
            if(feature){if(!link.SetFeature(packet))throw new IOException("DPI 设置未完整发送");}
            else Send(link,packet);
        }
        DpiSnapshot Execute()
        {
            var fresh=profile.Family==DpiFamily.Pulsar?PulsarSnapshot(profile,link):Read(profile,link);
            if(!before.SameSettings(fresh))throw Invalid("鼠标配置已变化，已取消写入；请重新读取");
            guard();
            if(profile.Family==DpiFamily.Pulsar)
            {
                string sensor=Codec(before); int address=value.HasValue?(sensor=="3955"?6912+6*stage:12+4*stage):4;
                byte[] data=value.HasValue?EncodeStage(value.Value,value.Value,sensor):new byte[]{(byte)stage,unchecked((byte)(85-stage))};
                if(address!=4 && address!=(sensor=="3955"?6912+6*stage:12+4*stage))throw Invalid();
                guard();attempted=true;Exchange(link,7,address,data.Length,data,true);
            }
            else if(profile.Family is DpiFamily.Atk or DpiFamily.Z7 or DpiFamily.Catalog)
            {
                string codec=Codec(before); bool wirelessR1=profile.Family==DpiFamily.Catalog&&profile.Codec=="step50"&&!profile.Wired;
                int address=4; byte[] data=new byte[]{(byte)stage,unchecked((byte)(85-stage))};
                if(!value.HasValue && wirelessR1)
                {
                    address=0;data=At(link,0,10);
                    string[] context=before.Context.Split('|');
                    ValidateSystem(data,out int count,out int active);
                    if(count!=before.Stages.Count||active!=before.Active||Convert.ToHexString(data.AsSpan(0,2))!=context[1]||
                        Convert.ToHexString(data.AsSpan(6))!=context[2])throw Invalid("写入前系统配置变化");
                    data[4]=(byte)stage;data[5]=unchecked((byte)(85-stage));
                }
                else if(value.HasValue)
                {
                    bool paired=profile.Family==DpiFamily.Atk&&codec=="atk"||profile.Family==DpiFamily.Catalog&&(profile.Codec=="ultra"||wirelessR1);
                    int size=codec=="master"?6:4;
                    address=codec=="master"?6912+stage*6:paired?12+stage/2*8:12+stage*4;
                    data=At(link,address,paired?8:size);
                    int first=paired?stage/2*2:stage;
                    for(int j=0;j<(paired?2:1)&&first+j<before.Stages.Count;j++)
                        if(DecodeStage(data.AsSpan(j*size,size).ToArray(),codec)!=before.Stages[first+j])throw Invalid("写入前相邻档位变化");
                    EncodeStage(value.Value,value.Value,codec).CopyTo(data,paired?stage%2*4:0);
                }
                if(profile.Family==DpiFamily.Catalog)
                {
                    var identity=Exchange(link,16,count:2);
                    if(identity[6]!=profile.Cid||identity[7]!=profile.Mid)throw Invalid("写入前型号核对失败");
                    guard();attempted=true;Exchange(link,7,address,data.Length,data);
                }
                else Write(BuildCompx(7,address,data.Length,data));
            }
            else if(profile.Family==DpiFamily.Scyrox)
            {
                var data=new List<byte>{10,0,(byte)(2+target.Stages.Count*2),(byte)target.Active,(byte)target.Stages.Count};
                foreach(var row in target.Stages){data.Add((byte)row.X);data.Add((byte)(row.X>>8));}
                Write(ScyroxPacket(data.ToArray()));
            }
            else if(profile.Family==DpiFamily.Sora)
            {
                var data=new byte[32];new byte[]{5,(byte)(value.HasValue?5:14),0,0,1,0,byte.Parse(before.Context),22}.CopyTo(data,0);
                data[9]=(byte)(value.HasValue?target.Stages.Count:target.Active);
                if(value.HasValue)for(int i=0;i<target.Stages.Count;i++)Put(data,10+2*i,target.Stages[i].X/50-1);
                Write(data,true);link.Delay(500);
            }
            else if(profile.Family==DpiFamily.Rapoo)
            {
                int baseAddress=1536+int.Parse(before.Context)*1024;
                if(!value.HasValue){Write(RapooPacket(165,baseAddress+664,1,new[]{(byte)stage}));RapooResponse(link,0);}
                else foreach(int axis in new[]{648,712})
                {var data=new byte[2];Put(data,0,value.Value);Write(RapooPacket(165,baseAddress+axis+stage*2,2,data));RapooResponse(link,0);}
            }
            link.Delay(100);
            var after=profile.Family==DpiFamily.Pulsar?PulsarSnapshot(profile,link):Read(profile,link);
            if(!target.SameSettings(after))throw Invalid("写后回读不一致，未确认生效");
            return after;
        }
        try {return profile.Family==DpiFamily.Pulsar?PulsarSession(link,Execute):Execute();}
        catch(Exception ex) when(attempted && ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or TimeoutException)
        {throw new InvalidOperationException("设置已尝试但未确认完整生效；请重新读取，不要重复点击。\n"+ex.Message,ex);}
    }
}
