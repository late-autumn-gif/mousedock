using System;

namespace MouseDock.Services.Detection;

internal static partial class OriginalGpwProtocol
{
    private sealed partial class Reader
    {
        private ReferenceBatteryProtocol.Reading ReadPairedMouse(byte slot, string name)
        {
            if (name is "USB Receiver" or "Receiver" or "Logitech USB Receiver")
                return Fail("接收器名称不能证明配对鼠标型号");
            byte deviceName = Feature(slot, 0x0005);
            if (deviceName == 0 || !LogitechHidpp.IsMouseType(Call(slot, deviceName, 2)))
                return Fail("配对外设没有确认鼠标类型，不读取电量");

            bool g403 = LogitechG403Battery.IsName(name);
            bool voltageRead = false;
            var reading = ReferenceBatteryProtocol.Reading.Failed("配对鼠标没有返回有效的直接百分比") with
                { Model = g403 ? LogitechG403Battery.Model : name, Identity = name, MouseTypeConfirmed = true };
            foreach (ushort id in new ushort[] { 0x1004, 0x1000 })
            {
                byte feature = Feature(slot, id);
                if (feature == 0) continue;
                if (id == 0x1004)
                {
                    var capabilities = Call(slot, feature, 0);
                    if (capabilities == null || (capabilities[5] & 2) == 0) continue;
                }
                byte function = id == 0x1004 ? (byte)1 : (byte)0;
                var first = Call(slot, feature, function);
                var second = Call(slot, feature, function);
                if (!Valid(first, id, out _, out _) ||
                    !Valid(second, id, out int level, out bool charging)) continue;
                reading = reading with
                {
                    Level = level, Charging = charging, ChargingKnown = true,
                    Status = second[6] == 3 ? ReferenceBatteryProtocol.State.Full :
                        charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
                    Diagnostic = $"HID++ {id:X4} 设备直接百分比；两次有效响应"
                };
                break;
            }

            if (reading.Level < 0)
            {
                byte voltage = Feature(slot, 0x1001);
                if (voltage != 0)
                {
                    var first = Call(slot, voltage, 0);
                    var second = Call(slot, voltage, 0);
                    if (ValidVoltage(first, out _) && ValidVoltage(second, out int millivolts))
                    {
                        voltageRead = true;
                        reading = reading with
                        {
                            Diagnostic = $"HID++ 1001 原始电压 {millivolts} mV；" +
                                (g403 ? "G403 当前充电电压不能唯一换算，百分比保持未知" :
                                    "未确认此型号电压曲线，百分比保持未知")
                        };
                        if (g403 && LogitechG403Battery.TryEstimate(millivolts, second[6],
                            out int percent, out bool charging, out bool full))
                            reading = reading with
                            {
                                Level = percent, Charging = charging, ChargingKnown = true,
                                Status = full ? ReferenceBatteryProtocol.State.Full :
                                    charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
                                Diagnostic = (full ? "G403 设备明确满电状态" :
                                    "G403 官方电池模型估算（非设备直接百分比）") +
                                    $"；HID++ 1001 原始电压 {millivolts} mV；flags={second[6]:X2}；两次有效响应"
                            };
                    }
                }
            }

            // Receiver PID is shared. Recheck the live peripheral after its battery transactions.
            if (ReadName(slot) != name || !LogitechHidpp.IsMouseType(Call(slot, deviceName, 2)))
                return Fail("配对鼠标实时名称或类型复核失败，丢弃本次电量");
            return reading.Level >= 0 || voltageRead ? reading : reading with
                { Diagnostic = reading.Diagnostic + "；" + issue };
        }

        private static bool Valid(byte[] frame, ushort feature, out int level, out bool charging)
        {
            level = -1; charging = false;
            return frame is { Length: 20 } && frame[6] <= 3 &&
                LogitechHidpp.TryParseBattery(feature, frame, out level, out charging);
        }

        private static bool ValidVoltage(byte[] frame, out int millivolts)
        {
            millivolts = 0;
            if (frame is not { Length: 20 } || (frame[6] & 0x40) != 0 ||
                ((frame[6] & 0x80) != 0 && (frame[6] & 7) > 2)) return false;
            millivolts = (frame[4] << 8) | frame[5];
            return millivolts is >= 2800 and <= 4500;
        }
    }
}
