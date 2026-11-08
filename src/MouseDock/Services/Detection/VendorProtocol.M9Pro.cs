using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading ReadM9Pro(string path)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("M9 Pro 缺少匹配的物理实例，未读取");
        return RunReceiverQuery(InstanceScope.Current, () =>
        {
            IntPtr handle = CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                throw new IOException("M9 Pro 被动接口打开失败");
            IntPtr signal = IntPtr.Zero;
            try
            {
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                var caps = Caps(handle);
                string product = HidDiscovery.ReadHidProduct(handle, path);
                if (!HidD_GetAttributes(handle, ref attributes) ||
                    !M9ProProtocol.MatchesChannel(attributes.VendorID, attributes.ProductID, product,
                        caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength,
                        caps.FeatureReportByteLength))
                    throw new IOException("M9 Pro 打开后的型号或集合能力不符");
                if (!HidD_GetPreparsedData(handle, out var prep))
                    throw new IOException("无法核对 M9 Pro 报告编号");
                try
                {
                    if (!TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps, out byte id) ||
                        id != 9)
                        throw new IOException("M9 Pro 输入报告编号不是唯一 report9");
                }
                finally { HidD_FreePreparsedData(prep); }
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (signal == IntPtr.Zero) throw new IOException("M9 Pro 异步事件创建失败");
                return M9ProProtocol.Read(new G7Link(handle, signal, 33));
            }
            finally
            {
                if (signal != IntPtr.Zero) CloseHandle(signal);
                CloseHandle(handle);
            }
        }, ReferenceBatteryProtocol.Reading.Failed, 4000);
    }
}
