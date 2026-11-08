using System;
using System.Text;

namespace MouseDock.Services.Detection;

internal static class F75Protocol
{
    internal const string Model = "AULA F75";
    internal const string Identity = "0300000000CD";

    internal static bool IsCandidate(ushort vid, ushort pid, string product) =>
        vid == 0x3554 && pid == 0xFA09 &&
        string.Equals(product?.Trim(), "2.4G Wireless Receiver", StringComparison.OrdinalIgnoreCase);

    internal static bool MatchesChannel(ushort vid, ushort pid, string product,
        ushort page, ushort usage, int input, int output, int feature) =>
        IsCandidate(vid, pid, product) && page == 0xFF02 && usage == 2 &&
        input == 20 && output == 20 && feature == 0;

    private static byte[] Request(byte command)
    {
        var packet = new byte[20];
        packet[0] = 0x13;
        packet[1] = command;
        packet[2] = command == 5 ? (byte)1 : (byte)0;
        packet[19] = (byte)(packet[0] + packet[1] + packet[2]);
        return packet;
    }

    private static bool ValidReply(byte[] packet, byte command, int dataLength)
    {
        if (packet == null || packet.Length != 20 || packet[0] != 0x13 ||
            packet[1] != command || packet[2] != 1 || packet[3] != 0 || packet[4] != dataLength)
            return false;
        int sum = 0;
        for (int i = 0; i < 19; i++) sum += packet[i];
        return packet[19] == (byte)sum;
    }

    private static byte[] Exchange(ReferenceBatteryProtocol.ILink link, byte command,
        int dataLength, StringBuilder trace)
    {
        bool drained = false;
        for (int i = 0; i < 32; i++)
        {
            if (link.Read(1) is { Length: > 0 }) continue;
            drained = true;
            break;
        }
        if (!drained) { trace.Append("；旧报告队列未清空"); return null; }
        byte[] request = Request(command);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            trace.Append(" TX=").Append(Convert.ToHexString(request));
            if (!link.Write(request, 250)) { trace.Append("；请求未完整发送"); return null; }
            long until = link.Now + 400;
            for (int count = 0; count < 32 && link.Now < until; count++)
            {
                var packet = link.Read((int)Math.Max(1, Math.Min(80, until - link.Now)));
                if (packet is not { Length: > 0 }) continue;
                trace.Append(" RX=").Append(Convert.ToHexString(packet.AsSpan(0, Math.Min(packet.Length, 20))));
                if (ValidReply(packet, command, dataLength)) return packet;
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
            // The shared receiver PID is resolved by the official six-byte UUID.
            var identity = Exchange(link, 5, 6, trace);
            if (identity == null || Convert.ToHexString(identity.AsSpan(5, 6)) != Identity)
                return ReferenceBatteryProtocol.Reading.Failed(trace + "；未确认 F75 型号");
            var battery = Exchange(link, 0x4A, 2, trace);
            if (battery == null || battery[5] > 100)
                return ReferenceBatteryProtocol.Reading.Failed(trace + "；电量未知");
            return new(battery[5], false, false, ReferenceBatteryProtocol.State.Online,
                trace + "；充电状态未确认", Model, Identity);
        }
        catch (Exception ex)
        {
            return ReferenceBatteryProtocol.Reading.Failed(trace + "；接口异常：" + ex.Message);
        }
    }
}
