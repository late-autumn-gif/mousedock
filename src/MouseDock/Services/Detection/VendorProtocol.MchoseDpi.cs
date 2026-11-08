using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    /// <summary>
    /// 读取迈从鼠标的 DPI 档位。只发官方 0x12 0x67 只读请求；
    /// 任何一步不满足就返回未知，不影响电量。
    ///
    /// A7 V2 系列（3837:100A/100B）的 Feature 集合是 65 字节、无 report id，
    /// 请求从 <c>[0]=0x12,[1]=0x67</c> 开始；GetFeature 缓冲区必须和
    /// FeatureReportByteLength 一致，否则直接 Win32=87。
    /// </summary>
    internal static MchoseDpi ReadMchoseDpi(string path, bool wired, out string diagnostic)
    {
        diagnostic = "MCHOSE DPI 未读取";
        IntPtr h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == IntPtr.Zero || h == new IntPtr(-1))
        {
            diagnostic = "MCHOSE DPI 接口打开失败 Win32=" + Marshal.GetLastWin32Error();
            return MchoseDpi.Unknown;
        }
        try
        {
            var caps = Caps(h);
            int length = Math.Max((int)caps.FeatureReportByteLength, MchoseDpiProtocol.FrameLength);
            var request = new byte[length];
            request[0] = 0x12;
            request[1] = 0x67;
            if (!HidD_SetFeature(h, request, length))
            {
                diagnostic = "MCHOSE DPI SET_FEATURE 失败 Win32=" + Marshal.GetLastWin32Error();
                return MchoseDpi.Unknown;
            }
            Thread.Sleep(30);
            var response = new byte[length];
            if (!HidD_GetFeature(h, response, length))
            {
                int error = Marshal.GetLastWin32Error();
                // Win32=0 表示驱动层直接拒绝：这台设备（例如 A7 V2 的 11/F9 旧通道）
                // 不实现官方 0x12 0x67，不是本次代码的失败。
                diagnostic = error == 0
                    ? "MCHOSE DPI：设备不响应 0x12 0x67（旧 11/F9 通道，不支持该命令）"
                    : "MCHOSE DPI GET_FEATURE 失败 Win32=" + error;
                return MchoseDpi.Unknown;
            }

            // 回包有两种排布：从 0x12 开始，或首字节是 report id。
            byte[] frame = null;
            if (response.Length >= 2 && response[0] == 0x12 && response[1] == 0x67) frame = response;
            else if (response.Length >= 3 && response[1] == 0x12 && response[2] == 0x67)
                frame = response.Skip(1).ToArray();

            if (frame == null)
            {
                diagnostic = "MCHOSE DPI 回包不是 allInfo（" +
                             Convert.ToHexString(response.AsSpan(0, Math.Min(16, response.Length))) + "）";
                return MchoseDpi.Unknown;
            }
            if (!MchoseDpiProtocol.TryParse(frame, wired, out int dpi, out int index, out int slots,
                    out diagnostic))
            {
                diagnostic = "MCHOSE DPI allInfo 解析被拒（" +
                             Convert.ToHexString(frame.AsSpan(0, Math.Min(24, frame.Length))) + "）";
                return MchoseDpi.Unknown;
            }
            return new MchoseDpi(dpi, index, slots, MchoseDpiProtocol.ReadSlots(frame));
        }
        catch (Exception ex)
        {
            diagnostic = "MCHOSE DPI 读取异常: " + ex.Message;
            return MchoseDpi.Unknown;
        }
        finally { CloseHandle(h); }
    }

    internal readonly record struct MchoseDpi(int Dpi, int Index, int Slots, int[] Values)
    {
        internal static readonly MchoseDpi Unknown = new(-1, -1, 0, new int[6]);
    }
}
