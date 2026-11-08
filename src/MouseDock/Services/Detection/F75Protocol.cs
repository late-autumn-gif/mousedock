using System;
using System.Text;

namespace MouseDock.Services.Detection;

/// <summary>
/// 狼蛛 AULA F75（3554:FA09，产品串 "2.4G Wireless Receiver"）。
///
/// 协议来源：合作者提供的 1.1.1+6329a7d 构建（"F75-Wireless-Retry"）反编译结果。
/// 关键点是重试策略而不是帧格式：接收器在键盘刚唤醒时经常前几次不回应，
/// 所以身份查询给 8 次 × 200ms、电量查询给 6 次 × 500ms，共用 5 秒总预算；
/// 旧版只试 2 次 × 400ms，在真机上就会"未取得有效响应"。
///
/// 帧：20 字节，<c>[0]=0x13</c>、<c>[1]=命令</c>、<c>[2] = 命令==5 时为 1</c>，
/// <c>[19] = sum([0..18])</c>。命令 5 身份回 <c>0300000000CD</c>，命令 74 回百分比。
/// 官方驱动界面对这个型号是 ShowPower=0，走的是固件留的 Report13 通道，充电状态未提供。
/// </summary>
internal static class F75Protocol
{
    internal const string Model = "AULA F75";
    internal const string Identity = "0300000000CD";

    /// <summary>一次读数的总等待预算；身份 + 电量都在这 5 秒里。</summary>
    internal const int QueryTimeoutMs = 5000;

    internal static bool IsCandidate(ushort vid, ushort pid, string product) =>
        vid == 0x3554 && pid == 0xFA09 &&
        string.Equals(product?.Trim(), "2.4G Wireless Receiver", StringComparison.OrdinalIgnoreCase);

    internal static bool MatchesChannel(ushort vid, ushort pid, string product, ushort page, ushort usage,
        int input, int output, int feature) =>
        IsCandidate(vid, pid, product) && MatchesSignature(page, usage, input, output, feature);

    /// <summary>只校验集合签名，不校验产品串（读取阶段拿不到产品名）。</summary>
    internal static bool MatchesSignature(ushort page, ushort usage, int input, int output, int feature) =>
        page == 0xFF02 && usage == 2 && input == 20 && output == 20 && feature == 0;

    private static byte[] Request(byte command)
    {
        var frame = new byte[20];
        frame[0] = 19;
        frame[1] = command;
        frame[2] = command == 5 ? (byte)1 : (byte)0;
        frame[19] = (byte)(frame[0] + frame[1] + frame[2]);
        return frame;
    }

    internal static bool ValidReply(byte[] packet, byte command, int dataLength)
    {
        if (packet == null || packet.Length != 20 || packet[0] != 19 || packet[1] != command ||
            packet[2] != 1 || packet[3] != 0 || packet[4] != dataLength) return false;
        int sum = 0;
        for (int i = 0; i < 19; i++) sum += packet[i];
        return packet[19] == (byte)sum;
    }

    /// <summary>
    /// 清旧队列 → 发 <paramref name="attempts"/> 轮请求，每轮等 <paramref name="waitMs"/> 收应答。
    /// 全部时间受 <paramref name="deadline"/> 约束，避免某一次卡死把整轮读数拖过预算。
    /// </summary>
    private static byte[] Exchange(ReferenceBatteryProtocol.ILink link, byte command, int dataLength,
        int attempts, int waitMs, long deadline, StringBuilder trace)
    {
        bool drained = false;
        for (int i = 0; i < 32; i++)
        {
            if (link.Now >= deadline) break;
            byte[] stale = link.Read(1);
            if (stale == null || stale.Length <= 0)
            {
                drained = true;
                break;
            }
        }
        if (!drained)
        {
            trace.Append("；旧报告队列未清空");
            return null;
        }

        var request = Request(command);
        int received = 0;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (link.Now >= deadline) break;
            long remaining = deadline - link.Now;
            if (remaining <= 0) break;
            trace.Append(" TX=").Append(Convert.ToHexString(request));
            if (!link.Write(request, (int)Math.Min(250L, remaining)))
            {
                trace.Append("；请求未完整发送");
                return null;
            }
            long until = Math.Min(deadline, link.Now + waitMs);
            for (int i = 0; i < 32; i++)
            {
                if (link.Now >= until) break;
                byte[] reply = link.Read((int)Math.Max(1L, Math.Min(80L, until - link.Now)));
                if (reply == null || reply.Length <= 0) continue;
                received++;
                trace.Append(" RX=").Append(Convert.ToHexString(reply.AsSpan(0, Math.Min(reply.Length, 20))));
                if (ValidReply(reply, command, dataLength)) return reply;
            }
        }
        trace.Append(received == 0 ? "；查询超时，未收到输入报告" : "；收到输入报告，但没有本轮有效响应");
        if (link.Now >= deadline) trace.Append("；本轮总等待预算已用尽");
        return null;
    }

    internal static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link)
    {
        var trace = new StringBuilder("F75 官方无线 Report13");
        try
        {
            long deadline = link.Now + QueryTimeoutMs;
            // 身份：短等多次，键盘唤醒后往往前几轮不应答。
            byte[] identity = Exchange(link, 5, 6, 8, 200, deadline, trace);
            if (identity == null || Convert.ToHexString(identity.AsSpan(5, 6)) != Identity)
                return ReferenceBatteryProtocol.Reading.Failed(trace + "；未确认 F75 型号");
            // 电量：次数少一点、每次等久一点，给固件算电量的时间。
            byte[] battery = Exchange(link, 74, 2, 6, 500, deadline, trace);
            if (battery == null || battery[5] > 100)
                return ReferenceBatteryProtocol.Reading.Failed(trace + "；电量未知");
            return new ReferenceBatteryProtocol.Reading(battery[5], false, false,
                ReferenceBatteryProtocol.State.Online, trace + "；充电状态未确认", Model, Identity);
        }
        catch (Exception ex)
        {
            return ReferenceBatteryProtocol.Reading.Failed(trace + "；接口异常：" + ex.Message);
        }
    }
}
