using System;

namespace MouseDock.Services.Detection;

internal static class M9ProProtocol
{
    internal const string Product = "LB-M9PRO";
    internal const string Model = "凌豹 M9 Pro";

    internal static bool IsCandidate(ushort vid, ushort pid, string product) =>
        vid == 0x093A && pid == 0x522C &&
        string.Equals(product?.Trim(), Product, StringComparison.OrdinalIgnoreCase);

    internal static bool MatchesChannel(ushort vid, ushort pid, string product,
        ushort page, ushort usage, int input, int output, int feature) =>
        IsCandidate(vid, pid, product) && page == 0xFF05 && usage == 1 &&
        input == 33 && output == 33 && feature == 9;

    internal static ReferenceBatteryProtocol.Reading Read(ReferenceBatteryProtocol.ILink link)
    {
        try
        {
            long deadline = link.Now + 3500;
            // The caller opens a new read-only handle for this observation. Unlike a
            // request/reply exchange, its first Input9 is already part of this read.
            int rejected = 0;
            for (int i = 0; i < 64 && link.Now < deadline; i++)
            {
                var bytes = link.Read((int)Math.Max(1, Math.Min(100, deadline - link.Now)));
                if (bytes is not { Length: > 0 }) continue;
                if (bytes.Length != 33 || bytes[0] != 9 || (bytes[1] & 0x7F) > 100)
                { rejected++; continue; }
                // Official LB-M9/M9PRO driver: Input9 -> 0x446440 -> 0x435D10.
                // The battery widget selects charging images when bit 7 is set.
                int level = bytes[1] & 0x7F;
                bool charging = (bytes[1] & 0x80) != 0;
                return new(level, charging, true,
                    charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
                    "M9 Pro 本轮被动 Input9 电量上报；未发送厂商命令；RX=" + Convert.ToHexString(bytes), Model);
            }
            return ReferenceBatteryProtocol.Reading.Failed($"M9 Pro 未取得本轮 Input9 电量上报；拒绝无效帧={rejected}");
        }
        catch (Exception ex)
        {
            return ReferenceBatteryProtocol.Reading.Failed("M9 Pro 被动读取失败：" + ex.GetType().Name);
        }
    }
}
