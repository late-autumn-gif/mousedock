using System;
using System.Text;

namespace MouseDock.Services.Detection;

/// <summary>
/// 狼蛛 AULA F75（3554:FA09，产品串 "2.4G Wireless Receiver"）。
///
/// 协议来源：用户提供的 1.1.1+bac923f 构建里 <c>F75Protocol</c> 的反编译结果。
/// 注意与官方驱动配置的矛盾：官方 <c>AULA F75 Setup v2.0</c> 的
/// <c>Dev\kb\F75KR\KB.ini</c> 写的是 <c>ShowPower=0</c>，即厂商软件自己不在界面上显示电量；
/// 但接收器的 hid 通道确实提供 20 字节 Feature 报告，命令 5 返回型号、
/// 命令 74 返回百分比。这条路径属于"厂商没显示但固件支持"，可靠性低于有官方显示的设备。
///
/// 帧：20 字节，<c>[0]=0x13</c>、<c>[1]=命令</c>、<c>[2] = 命令==5 时为 1</c>，
/// <c>[19] = sum([0..18])</c>。应答 <c>[2]==1</c>、<c>[4]=数据长度</c>、<c>[19]</c> 校验。
/// </summary>
internal static class F75Protocol
{
    internal const string Model = "AULA F75";
    internal const string Identity = "0300000000CD";

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

    private static byte[] Exchange(ReferenceBatteryProtocol.ILink link, byte command, int dataLength, StringBuilder trace)
    {
        bool drained = false;
        for (int i = 0; i < 32; i++)
        {
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
        byte[] request = Request(command);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            trace.Append(" TX=").Append(Convert.ToHexString(request));
            if (!link.Write(request, 250))
            {
                trace.Append("；请求未完整发送");
                return null;
            }
            long until = link.Now + 400;
            for (int i = 0; i < 32; i++)
            {
                if (link.Now >= until) break;
                byte[] reply = link.Read((int)Math.Max(1L, Math.Min(80L, until - link.Now)));
                if (reply == null || reply.Length <= 0) continue;
                trace.Append(" RX=").Append(Convert.ToHexString(reply.AsSpan(0, Math.Min(reply.Length, 20))));
                if (ValidReply(reply, command, dataLength)) return reply;
            }
        }
        trace.Append("；未取得本轮有效响应");
        return null;
    }

    internal static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link)
    {
        var trace = new StringBuilder("F75 官方无线 Report13");
        try
        {
            byte[] identity = Exchange(link, 5, 6, trace);
            if (identity == null || Convert.ToHexString(identity.AsSpan(5, 6)) != Identity)
                return ReferenceBatteryProtocol.Reading.Failed(trace + "；未确认 F75 型号");
            byte[] battery = Exchange(link, 74, 2, trace);
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

