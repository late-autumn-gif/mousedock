using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading ReadRogOmniKeyboard(string pairingPath, string keyboardPath)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) ||
            !InstanceScope.Allows(pairingPath) || !InstanceScope.Allows(keyboardPath))
            return ReferenceBatteryProtocol.Reading.Failed("OMNI 两个接口必须属于当前接收器实例");
        return ReferenceQueryGate.Run(pairingPath, () =>
        {
            using var pairing = new RogOmniLink(pairingPath, RogOmniKeyboardProtocol.Channel.Pairing);
            using var keyboard = new RogOmniLink(keyboardPath, RogOmniKeyboardProtocol.Channel.Keyboard);
            return RogOmniKeyboardProtocol.Read(pairing, keyboard);
        }, 3500);
    }

    private sealed class RogOmniLink : ReferenceBatteryProtocol.ILink, IDisposable
    {
        private IntPtr handle, signal;
        private readonly RogOmniKeyboardProtocol.Channel channel;

        internal RogOmniLink(string path, RogOmniKeyboardProtocol.Channel channel)
        {
            this.channel = channel;
            handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                handle = IntPtr.Zero;
                throw new IOException("OMNI 接口打开失败 Win32=" + Marshal.GetLastWin32Error());
            }
            try
            {
                var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                var caps = Caps(handle);
                if (!HidD_GetAttributes(handle, ref attributes) ||
                    RogOmniKeyboardProtocol.Match(attributes.VendorID, attributes.ProductID,
                        HidDiscovery.ReadHidProduct(handle, path), path, caps.UsagePage, caps.Usage,
                        caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength) != channel)
                    throw new IOException("OMNI 打开后的产品与集合签名不符");
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (signal == IntPtr.Zero) throw new IOException("OMNI 异步事件创建失败");
            }
            catch { Dispose(); throw; }
        }

        public long Now => Environment.TickCount64;
        public byte[] Read(int timeoutMs)
        {
            var bytes = new byte[64];
            return Transfer(handle, signal, bytes, false, Math.Max(1, timeoutMs), out int count) && count > 0
                ? bytes.AsSpan(0, count).ToArray() : null;
        }
        public bool Write(byte[] request, int timeoutMs)
        {
            byte report = channel == RogOmniKeyboardProtocol.Channel.Pairing ? (byte)1 : (byte)2;
            byte command = channel == RogOmniKeyboardProtocol.Channel.Pairing ? (byte)0xA0 : (byte)0x12;
            byte sub = channel == RogOmniKeyboardProtocol.Channel.Pairing ? (byte)0 : (byte)1;
            if (request.Length != 64 || request[0] != report || request[1] != command || request[2] != sub)
                throw new InvalidOperationException("OMNI 只允许配对列表和键盘电量查询");
            for (int i = 3; i < request.Length; i++)
                if (request[i] != 0) throw new InvalidOperationException("OMNI 查询不接受配置载荷");
            return Transfer(handle, signal, request, true, timeoutMs, out int count) && count == 64;
        }
        public bool SetFeature(byte[] request) => throw new InvalidOperationException();
        public byte[] GetFeature(byte reportId, int length) => throw new InvalidOperationException();
        public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
        public void Dispose()
        {
            if (signal != IntPtr.Zero) { CloseHandle(signal); signal = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }
}
