using System;
using System.Text;
using System.Threading;

namespace MouseDock.Services.Detection;

// New battery-only transactions, using HID++ facts and the existing original-GPW
// model. No broad Logitech admission and no uncorrelated/zero-client-ID fallback.
internal static class OriginalGpwProtocol
{
    internal const string Model = "Logitech G Pro Wireless";
    internal const string G603Model = "Logitech G603";
    internal static bool IsG603Name(string name) =>
        string.Equals(name, "G603 Wireless Gaming Mouse", StringComparison.Ordinal);
    internal static bool IsCandidate(ushort vid, ushort pid) => vid == 0x046D && pid is 0xC539 or 0xC088 or 0xC547;
    internal static bool IsChannel(ushort vid, ushort pid, ushort page, ushort usage, int input, int output, int feature) =>
        IsCandidate(vid,pid) && page == 0xFF00 && input == 20 && output == 20 && feature == 0 &&
        (pid == 0xC539 ? usage == 2 : usage is 1 or 2);

    internal static bool TryVoltage(ReadOnlySpan<byte> frame, out int level, out bool charging)
    {
        level = -1; charging = false;
        if (frame.Length != 20 || frame[0] != 0x11) return false;
        int mv = (frame[4] << 8) | frame[5];
        byte flags = frame[6];
        if (mv is < 2800 or > 4500 || (flags & 0x40) != 0 || ((flags & 0x80) != 0 && (flags & 7) > 2)) return false;
        bool active = (flags & 0x80) != 0 && (flags & 7) == 0;
        if ((flags & 0x80) != 0 && (flags & 7) == 1) { level = 100; return true; }
        if (!LogitechGpwBattery.TryEstimateGpwPercentage(mv, active, out level)) return false;
        charging = active;
        return true;
    }

    internal static bool TryDeviceIdentity(ReadOnlySpan<byte> frame, out string identity)
    {
        identity = null;
        if (frame.Length != 20 || frame[0] != 0x11) return false;
        // DEVICE_FW_VERSION getDeviceInfo: transport bits at payload6, compact
        // transport-ordered 16-bit model IDs from payload7. Never search for a
        // matching ID at arbitrary byte offsets or interpret the unit serial.
        byte transports = frame[10];
        if (transports is not (4 or 8 or 12)) return false;
        int offset = 11;
        if ((transports & 4) != 0)
        { if (frame[offset] != 0x40 || frame[offset + 1] != 0x79) return false; offset += 2; }
        if ((transports & 8) != 0 && (frame[offset] != 0xC0 || frame[offset + 1] != 0x88)) return false;
        identity = transports == 4 ? "HID++:WPID=4079" : transports == 8 ? "HID++:USB=C088" : "HID++:WPID=4079,USB=C088";
        return true;
    }

    internal static bool IsIdentityEvidence(string identity) => LogitechHidpp.IsOriginalGpwName(identity) ||
        identity is "HID++:WPID=4079" or "HID++:USB=C088" or "HID++:WPID=4079,USB=C088";

    internal static bool IsSuperlightName(string name)=>(name??"").Trim().ToUpperInvariant() is
        "PRO X SUPERLIGHT" or "PRO X WIRELESS" or "LOGITECH G PRO X SUPERLIGHT";
    internal static bool TrySuperlightPercentage(byte[] frame,out int level,out bool charging)
    {
        level=-1;charging=false;
        if(frame==null||frame.Length!=20||frame[0]!=0x11||frame[4]>100||frame[6]>3)return false;
        level=frame[4];charging=frame[6] is 1 or 2;return true;
    }

    private static int softwareId;
    internal static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link, ushort pid) => new Reader(link).Read(pid);

    private sealed class Reader(ReferenceBatteryProtocol.ILink link)
    {
        private readonly long deadline = link.Now + 4000;
        private readonly StringBuilder trace = new();
        private string issue = "未返回有效应答";
        private string lastRejected = "无";
        private int rejected, echoes;
        private bool faulted;

        internal ReferenceBatteryProtocol.Reading Read(ushort pid)
        {
            if(pid==0xC547)return ReadSuperlight();
            if (pid is not (0xC539 or 0xC088)) return Fail("PID 不属于本适配");
            // C539 is a receiver, not a model. Only read the paired device slot1.
            // A direct C088 device may expose the HID++ endpoint as slot1 or FF.
            foreach (byte slot in pid == 0xC539 ? new byte[] { 1 } : new byte[] { 1, 0xFF })
            {
                string name = ReadName(slot);
                if (pid == 0xC539 && IsG603Name(name)) return ReadG603(slot, name);
                if (name != null && !LogitechHidpp.IsOriginalGpwName(name))
                    return Fail("实时设备名不是一代 GPW，未读取/套用 GPW 电压曲线：" + name);
                if (name == null && pid == 0xC539) name = ReadDeviceIdentity(slot);
                bool confirmed = name != null || pid == 0xC088;
                if (!confirmed) continue;
                byte voltage = Feature(slot, 0x1001);
                if (voltage == 0)
                {
                    if (pid == 0xC088 && slot == 1 && name == null) continue;
                    return Fail("型号已确认，但没有取得 0x1001 特征：" + issue, name, confirmed);
                }
                var first = Call(slot, voltage, 0);
                if (first == null || !TryVoltage(first, out _, out _))
                    return Fail("第一次电压响应无效/充电平台不能唯一换算：" + issue, name, confirmed);
                var second = Call(slot, voltage, 0);
                if (second == null || !TryVoltage(second, out int level, out bool charging))
                    return Fail("第二次电压响应无效/充电平台不能唯一换算：" + issue, name, confirmed);
                bool full = (second[6] & 0x87) == 0x81;
                string source = full ? "设备明确满电状态" : "一代 GPW 官方电池模型估算（非设备直接百分比）";
                return new(level, charging, true, full ? ReferenceBatteryProtocol.State.Full :
                    charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
                    source + $"；slot={slot:X2} mV={(second[4] << 8) | second[5]} flags={second[6]:X2}；两次有效电压响应；回显={echoes} 无效={rejected}" + trace,
                    Model, name);
            }
            return Fail("没有取得一代 GPW 的实时名称/设备型号身份，接收器不等于外设型号：" + issue);
        }

        private ReferenceBatteryProtocol.Reading ReadG603(byte slot, string name)
        {
            if (ReadName(slot) != name)
                return Fail("G603 两次实时身份不一致，未读取电量");
            ReferenceBatteryProtocol.Reading Stop(string reason) =>
                ReferenceBatteryProtocol.Reading.Failed(reason + "：" + issue + trace) with
                { Model = G603Model, Identity = name };

            // G603 uses replaceable AA cells; only its reported percentage is valid.
            // Never apply the original GPW's lithium battery voltage curve here.
            byte feature = Feature(slot, 0x1000);
            if (feature == 0) return Stop("G603 未提供 0x1000 百分比特征");
            var first = Call(slot, feature, 0);
            if (first == null || !LogitechHidpp.TryParseBattery(0x1000, first, out _, out _))
                return Stop("G603 第一次百分比响应无效");
            var second = Call(slot, feature, 0);
            if (second == null || !LogitechHidpp.TryParseBattery(0x1000, second, out int level, out _))
                return Stop("G603 第二次百分比响应无效");
            return new(level, false, true, ReferenceBatteryProtocol.State.Online,
                "G603 实时身份确认；0x1000 设备百分比，两次有效响应；可更换电池，不支持充电" + trace,
                G603Model, name);
        }

        private ReferenceBatteryProtocol.Reading ReadSuperlight()
        {
            const string model="Logitech G PRO X SUPERLIGHT";
            ReferenceBatteryProtocol.Reading Stop(string reason,string name=null)=>ReferenceBatteryProtocol.Reading.Failed(reason+trace) with{Model=name==null?null:model,Identity=name};
            string name=ReadName(1);
            if(!IsSuperlightName(name))return Stop("C547尚未取得GPW2精确实时名称，不套用初代GPW模型");
            if(ReadName(1)!=name)return Stop("GPW2两次实时身份不一致");
            byte feature=Feature(1,0x1004);
            if(feature==0)return Stop("GPW2已确认，未提供可读取的1004百分比特征；不做电压猜测",name);
            var capabilities=Call(1,feature,0);
            if(capabilities==null||(capabilities[5]&2)==0)return Stop("1004未确认支持真实百分比",name);
            var first=Call(1,feature,1);var second=Call(1,feature,1);
            if(!TrySuperlightPercentage(first,out _,out _)||!TrySuperlightPercentage(second,out int level,out bool charging))return Stop("1004未取得两次有效百分比/状态",name);
            return new(level,charging,true,charging?ReferenceBatteryProtocol.State.Charging:ReferenceBatteryProtocol.State.Online,
                "GPW2实时身份确认；1004设备直接百分比，两次有效响应"+trace,model,name);
        }

        private ReferenceBatteryProtocol.Reading Fail(string reason, string name = null, bool confirmed = false) =>
            ReferenceBatteryProtocol.Reading.Failed(reason + $"；回显={echoes} 无效={rejected} LASTREJECT={lastRejected}" + trace) with
            { Model = confirmed ? Model : null, Identity = name };

        private byte Feature(byte slot, ushort id)
        {
            var r = Call(slot, 0, 0, (byte)(id >> 8), (byte)id);
            return r is { Length: 20 } && r[4] is > 0 and < 255 ? r[4] : (byte)0;
        }

        private string ReadDeviceIdentity(byte slot)
        {
            byte feature = Feature(slot, 3);
            if (feature == 0) return null;
            var a = Call(slot, feature, 0);
            var b = Call(slot, feature, 0);
            return a != null && b != null && TryDeviceIdentity(a, out string first) &&
                TryDeviceIdentity(b, out string second) && first == second ? second : null;
        }

        private string ReadName(byte slot)
        {
            byte feature = Feature(slot, 5);
            if (feature == 0) return null;
            var header = Call(slot, feature, 0);
            if (header == null || header[4] is 0 or > 64) return null;
            var bytes = new byte[header[4]];
            for (int offset = 0; offset < bytes.Length; offset += 16)
            {
                var r = Call(slot, feature, 1, (byte)offset);
                if (r == null) return null; // No partial name confirmation.
                r.AsSpan(4, Math.Min(16, bytes.Length - offset)).CopyTo(bytes.AsSpan(offset));
            }
            try
            {
                string name = new UTF8Encoding(false, true).GetString(bytes).TrimEnd('\0').Trim();
                if (name.Length == 0) return null;
                foreach (char c in name) if (char.IsControl(c)) return null;
                return name;
            }
            catch (DecoderFallbackException) { return null; }
        }

        private byte[] Call(byte slot, byte feature, byte function, byte p0 = 0, byte p1 = 0)
        {
            if (faulted) return null;
            if (link.Now >= deadline) { issue = "本实例总预算耗尽"; faulted = true; return null; }
            bool drained = false;
            for (int i = 0; i < 32; i++) if (link.Read(1) is not { Length: > 0 }) { drained = true; break; }
            if (!drained) { issue = "旧输入队列未排空"; faulted = true; return null; }
            var request = new byte[20];
            request[0] = 0x11; request[1] = slot; request[2] = feature;
            byte client = (byte)(1 + (unchecked((uint)Interlocked.Increment(ref softwareId)) % 15));
            request[3] = (byte)((function << 4) | client); request[4] = p0; request[5] = p1;
            trace.Append(" TX=").Append(Convert.ToHexString(request));
            if (!link.Write(request, 250)) { issue = "请求未完整发送"; faulted = true; return null; }
            long until = Math.Min(deadline, link.Now + 300);
            while (link.Now < until)
            {
                var raw = link.Read((int)Math.Max(1, Math.Min(100, until - link.Now)));
                if (raw is not { Length: > 0 }) continue;
                if (raw.AsSpan().SequenceEqual(request)) { echoes++; continue; }
                if (raw.Length is 7 or 20 && raw[0] is 0x10 or 0x11 && raw[1] == slot &&
                    raw[2] == 0xFF && raw[3] == feature && raw[4] == request[3])
                { issue = $"HID++ 错误 0x{raw[5]:X2}"; trace.Append(" ERR=").Append(Convert.ToHexString(raw)); return null; }
                if (raw.Length != 20 || raw[0] != 0x11 || raw[1] != slot || raw[2] != feature || raw[3] != request[3])
                { rejected++; lastRejected = Convert.ToHexString(raw.AsSpan(0, Math.Min(65,raw.Length))); issue = "编号/槽位/特征/函数/软件ID不匹配"; continue; }
                trace.Append(" RX=").Append(Convert.ToHexString(raw));
                return raw;
            }
            issue = "等待严格匹配响应超时；" + issue;
            return null;
        }
    }
}
