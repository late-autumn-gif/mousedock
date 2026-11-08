using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading ReadK87(string path)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("K87 缺少匹配的物理实例");
        return RunReceiverQuery(InstanceScope.Current, () =>
        {
            IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                throw new IOException("K87 专用接口打开失败");
            IntPtr signal = IntPtr.Zero;
            try
            {
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                var caps = Caps(handle);
                if (!HidD_GetAttributes(handle, ref attributes) ||
                    !MchoseKeyboardProtocol.IsSupportedDevice(attributes.VendorID, attributes.ProductID,
                        HidDiscovery.ReadHidProduct(handle, path)) ||
                    !MchoseKeyboardProtocol.IsSupportedInterface(caps.UsagePage, caps.Usage,
                        caps.InputReportByteLength, caps.OutputReportByteLength))
                    throw new IOException("K87 打开后的型号或集合能力不符");
                if (!HidD_GetPreparsedData(handle, out var prep))
                    throw new IOException("无法核对 K87 报告编号");
                try
                {
                    bool Matches(int type, ushort values, ushort buttons) => values + buttons == 0 ||
                        (TryGetReportId(prep, type, values, buttons, out byte id) && id == 0x4D);
                    if (!Matches(0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps) ||
                        !Matches(1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps))
                        throw new IOException("K87 输入输出须为唯一 Report4D");
                }
                finally { HidD_FreePreparsedData(prep); }
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (signal == IntPtr.Zero) throw new IOException("K87 异步事件创建失败");
                return MchoseKeyboardProtocol.Read(new G7Link(handle, signal, 64));
            }
            finally
            {
                if (signal != IntPtr.Zero) CloseHandle(signal);
                CloseHandle(handle);
            }
        }, ReferenceBatteryProtocol.Reading.Failed, 1500);
    }
}
