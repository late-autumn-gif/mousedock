using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static int blackSharkSequence;

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(IntPtr handle, byte[] report, int length);

    internal static ReferenceBatteryProtocol.Reading ReadRazerBlackShark(string path, ushort pid, BatteryReplay.Trace trace)
    {
        if (!RazerBlackSharkProtocol.IsCandidate(0x1532, pid) ||
            string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("黑鲨 V2 极速版缺少匹配的物理实例，未读取");
        return ReferenceQueryGate.Run(path, () =>
        {
            IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
                throw new IOException("黑鲨 V2 极速版接口打开失败 Win32=" + Marshal.GetLastWin32Error());
            IntPtr signal = IntPtr.Zero;
            try
            {
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                var caps = Caps(handle);
                if (!HidD_GetAttributes(handle, ref attributes) || attributes.ProductID != pid ||
                    !RazerBlackSharkProtocol.MatchesChannel(attributes.VendorID, attributes.ProductID,
                        caps.UsagePage, caps.Usage, caps.InputReportByteLength,
                        caps.OutputReportByteLength, caps.FeatureReportByteLength))
                    throw new IOException("黑鲨 V2 极速版打开后的型号或集合能力不符");
                if (!HidD_GetPreparsedData(handle, out var prep))
                    throw new IOException("无法核对黑鲨 V2 极速版报告编号");
                try
                {
                    if (!TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps, out byte input) || input != 2 ||
                        !TryGetReportId(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps, out byte output) || output != 2 ||
                        !TryGetReportId(prep, 2, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps, out byte feature) || feature != 2)
                        throw new IOException("黑鲨 V2 极速版集合不是唯一的 Input2/Output2/Feature2");
                }
                finally { HidD_FreePreparsedData(prep); }
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (signal == IntPtr.Zero) throw new IOException("黑鲨 V2 极速版异步事件创建失败");
                return RazerBlackSharkProtocol.Read(new BlackSharkLink(handle, signal), pid, trace,
                    (byte)(Interlocked.Add(ref blackSharkSequence, 4) & 31));
            }
            finally
            {
                if (signal != IntPtr.Zero) CloseHandle(signal);
                CloseHandle(handle);
            }
        }, 4000);
    }

    private sealed class BlackSharkLink(IntPtr handle, IntPtr signal) : ReferenceBatteryProtocol.ILink
    {
        public long Now => Environment.TickCount64;
        public byte[] Read(int timeoutMs)
        {
            var bytes = new byte[64];
            return Transfer(handle, signal, bytes, false, timeoutMs, out int count) && count > 0
                ? bytes.AsSpan(0, count).ToArray() : null;
        }
        // This device has only an interrupt IN endpoint; OUT uses HID SET_REPORT.
        public bool Write(byte[] request, int timeoutMs) => HidD_SetOutputReport(handle, request, request.Length);
        public bool SetFeature(byte[] request) => throw new NotSupportedException();
        public byte[] GetFeature(byte reportId, int length) => throw new NotSupportedException();
        public void Delay(int milliseconds) => throw new NotSupportedException();
    }
}
