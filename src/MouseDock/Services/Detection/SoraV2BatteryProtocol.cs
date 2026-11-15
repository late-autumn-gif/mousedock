using System;
using Link = MouseDock.Services.Detection.ReferenceBatteryProtocol.ILink;
using Reading = MouseDock.Services.Detection.ReferenceBatteryProtocol.Reading;
using State = MouseDock.Services.Detection.ReferenceBatteryProtocol.State;

namespace MouseDock.Services.Detection;

internal static class SoraV2BatteryProtocol
{
    private const byte ReportId = 5;
    private const byte BatteryCommand = 0x15;
    private const int ReportLength = 32;

    internal static byte[] CreateQuery()
    {
        // Windows includes the report ID before the 31-byte NinjaForce payload.
        var report = new byte[ReportLength];
        report[0] = ReportId;
        report[1] = BatteryCommand;
        report[4] = 1;
        report[7] = 4;
        return report;
    }

    internal static bool TryDecode(ReadOnlySpan<byte> report, out Reading reading)
    {
        reading = new(-1, false, false, State.Invalid, "Sora 电量应答格式无效");
        if (report.Length != ReportLength || report[0] != ReportId || report[1] != BatteryCommand ||
            report.SequenceEqual(CreateQuery())) return false;
        if (report[12] == 0)
        {
            reading = new(-1, false, false, State.Sleep, "Sora 外设休眠或离线");
            return true;
        }
        if (report[9] > 100 || report[10] > 1) return false;

        bool charging = report[10] == 1;
        reading = new(report[9], charging, true, charging ? State.Charging : State.Online, "Sora 取得有效电量应答");
        return true;
    }

    internal static Reading Read(Link link)
    {
        if (!link.SetFeature(CreateQuery())) return Reading.Failed("Sora Feature 查询发送失败");
        link.Delay(15);
        byte[] reply = link.GetFeature(ReportId, ReportLength);
        if (!TryDecode(reply, out var reading)) return Reading.Failed("Sora Feature 应答缺失或无效");
        if (reading.Level != 0) return reading;

        // NinjaForce retries once after a zero response. Require a fresh valid reply.
        link.Delay(15);
        if (!link.SetFeature(CreateQuery())) return Reading.Failed("Sora Feature 复查发送失败");
        link.Delay(5);
        return TryDecode(link.GetFeature(ReportId, ReportLength), out reading)
            ? reading : Reading.Failed("Sora Feature 复查应答缺失或无效");
    }
}
