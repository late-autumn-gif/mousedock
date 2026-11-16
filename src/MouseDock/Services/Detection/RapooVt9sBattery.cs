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
        byte batteryState;
        if (report.Length == 7)
        {
            // Historical receiver format. Its charging state is not established by A HUB.
            if (report[0] != 0xBB || report[1] != 0xB0 || report[5] is not (1 or 2))
                return false;
            batteryState = report[5];
            level = report[6];
        }
        else if (!TryReadReport7(report, out level, out batteryState))
        {
            return false;
        }
        else if (batteryState == 0)
        {
            reading = new(-1, false, false, State.Sleep, "雷柏状态为休眠，未采用电量字节");
            return true;
        }

        if (level > 100) return false;
        bool charging = batteryState == 2;
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
