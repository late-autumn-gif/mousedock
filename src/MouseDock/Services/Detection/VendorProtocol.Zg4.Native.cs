using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal sealed class Zg4HidLink(IntPtr handle, IntPtr signal, int size) : ReferenceBatteryProtocol.ILink
    {
        public long Now => Environment.TickCount64;

        public byte[] Read(int timeoutMs)
        {
            IntPtr buffer = Marshal.AllocHGlobal(size);
            IntPtr overlapped = IntPtr.Zero;
            bool outstanding = false;
            try
            {
                overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
                Marshal.StructureToPtr(new NativeOverlapped { EventHandle = signal }, overlapped, false);
                if (!ResetEvent(signal)) return null;
                bool started = ReadFile(handle, buffer, size, IntPtr.Zero, overlapped);
                if (!started && Marshal.GetLastWin32Error() != 997) return null;
                outstanding = true;
                if (WaitForSingleObject(signal, (uint)Math.Max(0, timeoutMs)) != 0)
                    CancelIoEx(handle, overlapped);

                // Cancellation can race a completed HID read; retain a successful completion.
                bool completed = GetOverlappedResult(handle, overlapped, out uint count, true);
                outstanding = false;
                if (!completed || count == 0 || count > size) return null;
                var report = new byte[(int)count];
                Marshal.Copy(buffer, report, 0, report.Length);
                return report;
            }
            finally
            {
                if (outstanding)
                {
                    CancelIoEx(handle, overlapped);
                    GetOverlappedResult(handle, overlapped, out _, true);
                }
                if (overlapped != IntPtr.Zero) Marshal.FreeHGlobal(overlapped);
                Marshal.FreeHGlobal(buffer);
            }
        }

        public bool Write(byte[] request, int timeoutMs) =>
            Transfer(handle, signal, request, true, timeoutMs, out int count) && count == request.Length;
        public bool SetFeature(byte[] request) => throw new NotSupportedException();
        public byte[] GetFeature(byte reportId, int length) => throw new NotSupportedException();
        public void Delay(int milliseconds) => throw new NotSupportedException();
    }
}
