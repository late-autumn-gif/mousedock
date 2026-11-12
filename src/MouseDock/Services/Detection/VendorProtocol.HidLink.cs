using System;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    /// <summary>
    /// 把 HID 的原始读写包装成 <see cref="ReferenceBatteryProtocol.ILink"/>，
    /// 供 VGN F1 / AULA F75 这类"收发原始帧"的协议使用。
    /// 只读写厂商集合，不做 SetFeature/GetFeature。
    /// </summary>
    internal sealed class HidStreamLink : ReferenceBatteryProtocol.ILink, IDisposable
    {
        private readonly IntPtr _handle;
        private readonly IntPtr _signal;
        private readonly int _length;

        internal HidStreamLink(IntPtr handle, int reportLength)
        {
            _handle = handle;
            _length = reportLength;
            _signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        }

        public long Now => Environment.TickCount64;

        public byte[] Read(int timeoutMs)
        {
            var buffer = new byte[_length];
            return Transfer(_handle, _signal, buffer, false, Math.Max(1, timeoutMs), out int count) && count > 0
                ? buffer.AsSpan(0, count).ToArray()
                : null;
        }

        public bool Write(byte[] request, int timeoutMs)
        {
            var buffer = new byte[_length];
            Array.Copy(request, buffer, Math.Min(request.Length, _length));
            return Transfer(_handle, _signal, buffer, true, Math.Max(1, timeoutMs), out int written) &&
                   written == buffer.Length;
        }

        public bool SetFeature(byte[] request) => false;
        public byte[] GetFeature(byte reportId, int length) => null;
        public void Delay(int milliseconds) => System.Threading.Thread.Sleep(milliseconds);

        public void Dispose()
        {
            if (_signal != IntPtr.Zero) CloseHandle(_signal);
        }
    }
}
