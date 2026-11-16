using System;

using Reading = MouseDock.Services.Detection.ReferenceBatteryProtocol.Reading;
using State = MouseDock.Services.Detection.ReferenceBatteryProtocol.State;

namespace MouseDock.Services.Detection;

internal static class RapooVt9sBattery
{
    internal static bool TryDecode(ReadOnlySpan<byte> report, out Reading reading)
    {
        reading = new(-1, false, false, State.Invalid, "应答不符合所选协议");
        int level;
        byte status;
        switch (report.Length)
        {
            case 7:
                // A HUB confirms the BB/B0 marker and percentage position.
                // Status 2 remains a compatibility rule for older receivers.
                if (report[0] != 0xBB || report[1] != 0xB0) return false;
                status = report[5];
                if (status is not (1 or 2)) return false;
                level = report[6];
                break;
            case 19:
            case 20:
                if (!TryReadReport7(report, out level, out status)) return false;
                if (status == 0)
                {
                    reading = new(-1, false, false, State.Sleep, "雷柏状态为休眠，未采用电量字节");
                    return true;
                }
                break;
            default:
                return false;
        }

        if (level > 100) return false;
        bool charging = status == 2;
        reading = new(level, charging, true, charging ? State.Charging : State.Online,
            "本次事务取得有效电量应答");
        return true;
    }

    internal static bool TryReadReport7(ReadOnlySpan<byte> report, out int percent, out byte state)
    {
        percent = -1;
        state = 0;
        if (report.Length is not (19 or 20) || report[0] != 7)
            return false;

        // A HUB's VT_nrf54L parser takes the transport from the low nibble.
        // Report 07 payload bytes 6 and 7 are battery state and percentage.
        ReadOnlySpan<byte> payload = report[1..];
        if ((payload[0] & 0x0f) > 2 || payload[6] > 2)
            return false;

        state = payload[6];
        percent = payload[7];
        return true;
    }
}
