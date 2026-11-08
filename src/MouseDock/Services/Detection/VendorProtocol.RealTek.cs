using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    /// <summary>
    /// RealTek 系鼠标的只读电量：发官方 <c>0x11 0x06</c>，读回后按官方 parser 解码。
    /// 只发只读请求；失败返回 -1 并留下诊断。
    /// </summary>
    internal static int ReadRealTekBattery(ushort vid, ushort pid, string path, out bool charging,
        out string diagnostic)
    {
        charging = false;
        diagnostic = "RealTek 未读取";
        if (!VendorProtocol.IsRealTekShape(vid, pid)) return -1;

        IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1))
        {
            diagnostic = "RealTek 接口打开失败 Win32=" + Marshal.GetLastWin32Error();
            return -1;
        }
        try
        {
            var caps = Caps(h);
            int length = Math.Max((int)caps.FeatureReportByteLength, RealTekHidProtocol.BatteryRequestLength);
            var request = new byte[length];
            Array.Copy(RealTekHidProtocol.BatteryRequest(), request, RealTekHidProtocol.BatteryRequestLength);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!HidD_SetFeature(h, request, length))
                {
                    diagnostic = "RealTek SET_FEATURE 失败 Win32=" + Marshal.GetLastWin32Error();
                    return -1;
                }
                Thread.Sleep(40);
                var response = new byte[length];
                if (!HidD_GetFeature(h, response, length))
                {
                    int error = Marshal.GetLastWin32Error();
                    diagnostic = error == 0
                        ? "RealTek 设备不响应 0x11 0x06"
                        : "RealTek GET_FEATURE 失败 Win32=" + error;
                    continue;
                }
                if (!RealTekHidProtocol.TryParseBattery(response, vid, out int level, out bool charge,
                        out string parse) || level < 0)
                {
                    diagnostic = parse;
                    continue;
                }
                charging = charge;
                diagnostic = parse;
                return level;
            }
            return -1;
        }
        catch (Exception ex)
        {
            diagnostic = "RealTek 读取异常: " + ex.Message;
            return -1;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>目前只放行用户报告里出现过的凌豹 M9 Pro；其余同形状设备未验证，不主动发帧。</summary>
    internal static bool IsRealTekShape(ushort vid, ushort pid) =>
        vid == 0x093A && pid == 0x522C;
}
