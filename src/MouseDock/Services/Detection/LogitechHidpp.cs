using System;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 罗技 HID++ 2.0 电量读取（特性 0x1001 Battery Level Status / 0x1000 Battery Voltage）。
    /// 走特性报告通道，读不到就返回 -1，不会阻塞界面。
    /// 没有罗技硬件时可安全跳过——所有调用都在同一把锁里做，超时靠特性报告自身的同步语义。
    /// </summary>
    internal static class LogitechHidpp
    {
        private const byte ReportShort = 0x10;
        private const byte ReportLong = 0x11;
        private const byte SwId = 0x0F;
        private const byte FeatureBatteryLevelStatus = 0x1001 >> 8;

        [StructLayout(LayoutKind.Sequential)]
        private struct Padding { public ushort Unused; }

        [DllImport("hid.dll")] private static extern bool HidD_SetFeature(IntPtr h, byte[] buffer, int length);
        [DllImport("hid.dll")] private static extern bool HidD_GetFeature(IntPtr h, byte[] buffer, int length);

        private static readonly object Gate = new object();
        private static bool _unsupported;

        public static int TryReadBattery(IntPtr handle, int featureReportByteLength)
        {
            if (_unsupported || handle == IntPtr.Zero) return -1;
            if (featureReportByteLength < 7) return -1;

            lock (Gate)
            {
                try
                {
                    int level = QueryLong(handle, featureReportByteLength, 0xFF, 0x1001);
                    if (level < 0) level = QueryLong(handle, featureReportByteLength, 0x01, 0x1001);
                    if (level < 0) level = QueryLong(handle, featureReportByteLength, 0xFF, 0x1000);
                    return level;
                }
                catch
                {
                    _unsupported = true;
                    return -1;
                }
            }
        }

        /// <summary>发一条 HID++ 长报文并解析响应。</summary>
        private static int QueryLong(IntPtr handle, int featureReportByteLength, byte deviceIndex, ushort featureId)
        {
            int size = Math.Max(7, featureReportByteLength);

            byte featureIndex = GetFeatureIndex(handle, size, deviceIndex, featureId);
            if (featureIndex == 0) return -1;

            var request = new byte[size];
            request[0] = ReportLong;
            request[1] = deviceIndex;
            request[2] = featureIndex;
            request[3] = (byte)(0x00 | (SwId << 4));   // function 0 = get status

            if (!HidD_SetFeature(handle, request, request.Length)) return -1;

            var response = new byte[size];
            response[0] = ReportLong;
            if (!HidD_GetFeature(handle, response, response.Length)) return -1;

            // 0x1000 返回电压（mV），0x1001 返回 [电量%, 状态, ...]
            if (featureId == 0x1001)
            {
                byte percent = response[4];
                if (percent <= 100) return percent;
            }
            else
            {
                int millivolts = response[4] | (response[5] << 8);
                if (millivolts <= 0) return -1;
                return VoltageToPercent(millivolts);
            }
            return -1;
        }

        private static byte GetFeatureIndex(IntPtr handle, int size, byte deviceIndex, ushort featureId)
        {
            var request = new byte[size];
            request[0] = ReportLong;
            request[1] = deviceIndex;
            request[2] = 0x00;                                  // IRootFeatureSet
            request[3] = (byte)(0x00 | (SwId << 4));            // getFeature
            request[4] = (byte)(featureId >> 8);
            request[5] = (byte)(featureId & 0xFF);

            if (!HidD_SetFeature(handle, request, request.Length)) return 0;

            var response = new byte[size];
            response[0] = ReportLong;
            if (!HidD_GetFeature(handle, response, response.Length)) return 0;

            byte index = response[4];
            return index == 0x00 || index == 0xFF ? (byte)0 : index;
        }

        /// <summary>锂电经验曲线：3.7V 约 30%，4.2V 满电。</summary>
        private static int VoltageToPercent(int millivolts)
        {
            if (millivolts < 3000 || millivolts > 4500) return -1;
            const int empty = 3450, full = 4180;
            int pct = (int)Math.Round((millivolts - empty) * 100.0 / (full - empty));
            return Math.Clamp(pct, 0, 100);
        }
    }
}
