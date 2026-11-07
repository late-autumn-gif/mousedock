using System;
using System.Text;

namespace MouseDock.Services.Detection;

// Battery queries only. SC580 fields have historical physical captures; SC630
// fields come from the hashed official 1.0.5 driver, not a physical SC630 reply.
internal static class AulaBatteryProtocol
{
    internal enum Route { Sc580Receiver, Sc630Pro }
    internal static bool IsCandidate(ushort vid, ushort pid, string product) =>
        (vid == 0x089D && pid == 0x062F) || (vid != 0 && pid != 0 && VendorProtocol.IsAulaSc630(product));
    internal static Route? Match(ushort vid, ushort pid, string product, ushort page, ushort usage,
        int input, int output, int feature)
    {
        if (VendorProtocol.IsAulaSc630(product))
            return vid != 0 && pid != 0 && page == 0xFF01 && usage == 1 && feature == 41 ? Route.Sc630Pro : null;
        if (vid == 0x089D && pid == 0x062F && page == 0xFF02 && usage == 0 && input == 33 && output == 33)
            return Route.Sc580Receiver;
        return null;
    }

    internal static byte[] IdentifyRequest()
    {
        var request = new byte[33]; request[1] = 0x30; request[3] = 1; return request;
    }

    // Constant-only vendor reports have no button/value capabilities. For these
    // two exact profiles the ID is independently established: SC580's captured
    // native 33-byte report0; SC630 official transport FUN_00480b90 report6/41.
    // A populated table must still agree; this is not a generic length fallback.
    internal static bool AcceptsProfileReportId(Route route, bool readOk, byte id, int values, int buttons) =>
        (values == 0 && buttons == 0) || (readOk && id == (route == Route.Sc580Receiver ? 0 : 6));

    internal static bool TryDecodeIdentify(ReadOnlySpan<byte> frame, out ReferenceBatteryProtocol.Reading result)
    {
        result = ReferenceBatteryProtocol.Reading.Failed("Identify 帧格式不符");
        if (frame.Length == 33 && frame[0] == 0) frame = frame[1..];
        if (frame.Length != 32 || frame[0] != 0x30) return false;
        if (frame[1] == 2 && frame[2] == 1)
        { result = new(-1, false, false, ReferenceBatteryProtocol.State.Sleep, "接收器未就绪/鼠标休眠，未采用缓存电量"); return true; }
        if (frame[1] != 0 || frame[2] != 1 || frame[3] != 11 || frame[12] > 1 || frame[13] > 100 || frame[14] > 1) return false;
        string identity = Encoding.ASCII.GetString(frame.Slice(4, 4));
        // This shared receiver is also historically known to carry M230. Never
        // turn its ID, a product alias or a cached identity into an SC580 name.
        if (identity is not ("M137" or "M230")) return false;
        if (frame[14] == 0)
        { result = new(-1, false, false, ReferenceBatteryProtocol.State.Sleep, "Identify online=0，接收器旧电量不作为当前读数"); return true; }
        VendorMap.TryIdentityModel(identity, out string model, out _);
        result = new(frame[13], frame[12] == 1, true,
            frame[12] == 1 ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
            "有效 Identify", model, identity);
        return true;
    }

    internal static ReferenceBatteryProtocol.Reading ReadIdentify(ReferenceBatteryProtocol.ILink link)
    {
        var request = IdentifyRequest();
        ReferenceBatteryProtocol.Reading first = null;
        int rejected = 0, echoes = 0, notReady = 0, writeFailures = 0;
        string last = "无", accepted = "";
        for (int exchange = 0; exchange < 4; exchange++)
        {
            bool drained = false;
            for (int i = 0; i < 32; i++) if (link.Read(1) is not { Length: > 0 }) { drained = true; break; }
            if (!drained) return ReferenceBatteryProtocol.Reading.Failed("Identify 旧队列未清空");
            if (!link.Write(request, 400))
            { writeFailures++; continue; }
            long deadline = link.Now + 900;
            while (link.Now < deadline)
            {
                var raw = link.Read((int)Math.Max(1, deadline - link.Now));
                if (raw is not { Length: > 0 }) continue;
                last = Convert.ToHexString(raw.AsSpan(0, Math.Min(raw.Length, 65)));
                if (raw.AsSpan().SequenceEqual(request) || (raw.Length == 32 && raw.AsSpan().SequenceEqual(request.AsSpan(1))))
                { echoes++; continue; }
                if (!TryDecodeIdentify(raw, out var reading)) { rejected++; continue; }
                if (reading.Status == ReferenceBatteryProtocol.State.Sleep)
                { notReady++; first = null; accepted = ""; continue; }
                if (first != null && first.Identity != reading.Identity)
                    return ReferenceBatteryProtocol.Reading.Failed("两次 Identify 型号不一致，拒绝电量与命名");
                accepted += " RX=" + last;
                if (first != null) return reading with { Diagnostic = $"Identify 两次有效应答；回显={echoes} 无效={rejected} 未就绪={notReady}" + accepted };
                first = reading;
                break;
            }
        }
        return new(-1, false, false, notReady > 0 ? ReferenceBatteryProtocol.State.Sleep : ReferenceBatteryProtocol.State.Failed,
            $"Identify 未取得两次同型号在线应答；回显={echoes} 无效={rejected} 未就绪={notReady} 写入失败={writeFailures} 最后RX={last}" + accepted);
    }

    internal static ReferenceBatteryProtocol.Reading ReadSc630(ReferenceBatteryProtocol.ILink link, Func<byte> sequence)
    {
        ReferenceBatteryProtocol.Reading first = null;
        string diagnostic = "", last = "无";
        int notEchoed = 0;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            byte seq = sequence();
            if (seq == 0) return ReferenceBatteryProtocol.Reading.Failed("SC630 查询序号为零，未发送");
            var request = VendorProtocol.BuildAulaSc630BatteryRequest(seq);
            if (!link.SetFeature(request)) { last = "SetFeature 失败"; continue; }
            link.Delay(130);
            var reply = link.GetFeature(6, 41);
            last = reply == null ? "GetFeature 失败" : Convert.ToHexString(reply.AsSpan(0, Math.Min(41, reply.Length)));
            if (reply == null || !VendorProtocol.TryParseAulaSc630Battery(reply, seq, out int level, out bool charging)) continue;
            if (reply[4] == 0) notEchoed++;
            diagnostic += " RX=" + last;
            var result = new ReferenceBatteryProtocol.Reading(level, charging, true,
                charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
                $"SC630 Feature6/41 两次有效响应；序号未回显={notEchoed}（兼容官方读取规则）" + diagnostic,
                "AULA SC630 Pro");
            if (first != null) return result;
            first = result;
        }
        return ReferenceBatteryProtocol.Reading.Failed("SC630 未取得两次有效 Feature 响应；最后=" + last + diagnostic);
    }
}
