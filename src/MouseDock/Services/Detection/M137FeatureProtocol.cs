using System;
using System.Text;

namespace MouseDock.Services.Detection;

internal static class M137FeatureProtocol
{
    internal const string Model = "狼蛛 SC580";
    internal static bool IsCandidate(ushort vid, ushort pid, string product) =>
        ReceiverIdentityProtocol.IsCandidate(vid, pid) &&
        string.Equals(product?.Trim(), "Wireless-Receiver", StringComparison.OrdinalIgnoreCase);

    internal static ReferenceBatteryProtocol.Reading Read(ReceiverIdentityProtocol.ILink link)
    {
        var trace = new StringBuilder("SC580 Feature12 GetInfo");
        ReferenceBatteryProtocol.Reading Failed(string reason) =>
            ReferenceBatteryProtocol.Reading.Failed(trace + "；" + reason);
        if (link.FeatureReportId != 12) return Failed("未确认 report12，未查询");
        byte sequence = 0;
        int level = -1;
        try
        {
            for (int transaction = 0; transaction < 2; transaction++)
            {
                var set = ReceiverIdentityProtocol.Request(sequence++);
                trace.Append(" SET=").Append(Convert.ToHexString(set));
                if (!link.SetFeature(set)) return Failed("SET 失败");
                bool accepted = false;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    link.Delay(16);
                    var get = ReceiverIdentityProtocol.Request(sequence++);
                    trace.Append(" GET=").Append(Convert.ToHexString(get));
                    byte[] response;
                    try { response = link.GetFeature(get); }
                    catch (Exception ex) { trace.Append(" ERROR=").Append(ex.Message); continue; }
                    trace.Append(" RX=").Append(response == null ? "无" : Convert.ToHexString(response));
                    if (!ReceiverIdentityProtocol.TryIdentity(response, out string identity)) continue;
                    if (identity != "M137") return Failed("身份不是 M137，未采用读数");
                    // Official GetInfo -> device+0x47C -> SetDeviceBattery percent.
                    // Byte19 and wireless online semantics are deliberately not interpreted.
                    if (response[18] > 100) continue;
                    level = response[18];
                    accepted = true;
                    break;
                }
                if (!accepted) return Failed("未取得两次有效 M137 响应");
            }
            return new(level, false, false, ReferenceBatteryProtocol.State.Online,
                trace + "；两次 M137 响应；接收器报告电量，充电与外设在线状态未确认", Model, "M137");
        }
        catch (Exception ex) { return Failed("接口异常: " + ex.Message); }
    }
}
