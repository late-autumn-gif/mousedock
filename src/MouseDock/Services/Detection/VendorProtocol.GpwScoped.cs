using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading ReadOriginalGpw(string path, ushort vid, ushort pid)
    {
        if (!OriginalGpwProtocol.IsCandidate(vid, pid) || InstanceScope.Current == null || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("一代 GPW 候选/实例不匹配，未发送查询");
        return ReferenceQueryGate.Run(path, () =>
        {
            using var link = new OriginalGpwLink(path, vid, pid);
            return OriginalGpwProtocol.Read(link, pid);
        }, 5000);
    }

    private sealed class OriginalGpwLink : ReferenceBatteryProtocol.ILink, IDisposable
    {
        private IntPtr handle, signal;
        internal OriginalGpwLink(string path, ushort vid, ushort pid)
        {
            handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            { handle = IntPtr.Zero; throw new IOException("GPW HID++ 接口打开失败 Win32=" + Marshal.GetLastWin32Error()); }
            try
            {
                var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(handle, ref attr) || attr.VendorID != vid || attr.ProductID != pid)
                    throw new IOException("打开后 VID/PID 不符");
                var caps = Caps(handle);
                if (!OriginalGpwProtocol.IsChannel(vid, pid, caps.UsagePage, caps.Usage,
                    caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength))
                    throw new IOException("不是本实例 FF00 长报文20/20/0集合，未查询");
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (signal == IntPtr.Zero) throw new IOException("创建 HID++ 查询事件失败");
            }
            catch { Dispose(); throw; }
        }
        public long Now => Environment.TickCount64;
        public byte[] Read(int timeoutMs)
        {
            var b = new byte[20];
            return Transfer(handle, signal, b, false, timeoutMs, out int count) && count > 0 ? b.AsSpan(0, count).ToArray() : null;
        }
        public bool Write(byte[] request, int timeoutMs) => request.Length == 20 && request[0] == 0x11 &&
            Transfer(handle, signal, request, true, timeoutMs, out int count) && count == request.Length;
        public bool SetFeature(byte[] request) => throw new NotSupportedException();
        public byte[] GetFeature(byte reportId, int length) => throw new NotSupportedException();
        public void Delay(int milliseconds) => throw new NotSupportedException();
        public void Dispose()
        {
            if (signal != IntPtr.Zero) { CloseHandle(signal); signal = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }
}
