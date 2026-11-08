using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading ReadVgnF1(string path)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("VGN F1 缺少匹配的物理实例，未发送查询");
        return RunReceiverQuery(InstanceScope.Current, () =>
        {
            IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                throw new IOException("VGN F1 专用接口打开失败");
            IntPtr signal = IntPtr.Zero;
            try
            {
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                var caps = Caps(handle);
                string product = HidDiscovery.ReadHidProduct(handle, path);
                if (!HidD_GetAttributes(handle, ref attributes) ||
                    !VgnF1Protocol.MatchesChannel(attributes.VendorID, attributes.ProductID, product,
                        caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength,
                        caps.FeatureReportByteLength))
                    throw new IOException("VGN F1 打开后的型号或集合能力不符");
                if (!HidD_GetPreparsedData(handle, out var prep))
                    throw new IOException("无法核对 VGN F1 报告编号");
                try
                {
                    // Some HID descriptors expose only constant fields. The exact profile above
                    // is required before using its official report ID when caps contain no fields.
                    bool Check(int type, ushort values, ushort buttons) => values + buttons == 0 ||
                        (TryGetReportId(prep, type, values, buttons, out byte id) && id == 8);
                    if (!Check(0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps) ||
                        !Check(1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps))
                        throw new IOException("VGN F1 报告编号不是唯一 report8");
                }
                finally { HidD_FreePreparsedData(prep); }
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (signal == IntPtr.Zero) throw new IOException("VGN F1 异步事件创建失败");
                return VgnF1Protocol.Read(new G7Link(handle, signal, 17));
            }
            finally
            {
                if (signal != IntPtr.Zero) CloseHandle(signal);
                CloseHandle(handle);
            }
        }, ReferenceBatteryProtocol.Reading.Failed, 4000);
    }
}
