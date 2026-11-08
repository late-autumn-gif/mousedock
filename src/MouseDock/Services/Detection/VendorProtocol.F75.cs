using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading ReadF75(string path)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("F75 物理实例不符");
        return ReferenceQueryGate.Run(path, () =>
        {
            IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                throw new IOException("F75 报告接口打开失败");
            IntPtr signal = IntPtr.Zero;
            try
            {
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                var caps = Caps(handle);
                string product = HidDiscovery.ReadHidProduct(handle, path);
                if (!HidD_GetAttributes(handle, ref attributes) ||
                    !F75Protocol.MatchesChannel(attributes.VendorID, attributes.ProductID, product,
                        caps.UsagePage, caps.Usage, caps.InputReportByteLength,
                        caps.OutputReportByteLength, caps.FeatureReportByteLength))
                    throw new IOException("F75 实际型号或报告布局不符");
                if (!HidD_GetPreparsedData(handle, out var preparsed))
                    throw new IOException("无法核对 F75 报告能力");
                try
                {
                    bool ReportMatches(int type, ushort values, ushort buttons) =>
                        values + buttons == 0 ||
                        (TryGetReportId(preparsed, type, values, buttons, out byte id) && id == 0x13);
                    if (!ReportMatches(0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps) ||
                        !ReportMatches(1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps))
                        throw new IOException("F75 输入和输出均须为唯一 Report13");
                }
                finally { HidD_FreePreparsedData(preparsed); }
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (signal == IntPtr.Zero) throw new IOException("F75 事件创建失败");
                return F75Protocol.Read(new G7Link(handle, signal, 20));
            }
            finally
            {
                if (signal != IntPtr.Zero) CloseHandle(signal);
                CloseHandle(handle);
            }
        }, 3500);
    }
}
