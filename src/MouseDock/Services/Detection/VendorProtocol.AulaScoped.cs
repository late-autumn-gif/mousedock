using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    internal static ReferenceBatteryProtocol.Reading ReadAulaBattery(AulaBatteryProtocol.Route route, string path,
        ushort vid, ushort pid, string instanceProduct = null)
    {
        if (InstanceScope.Current == null || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("缺少匹配的设备实例，未发送查询");
        var trace = BatteryReplay.Current?.Active;
        return ReferenceQueryGate.Run(path, () =>
        {
            using var link = new AulaLink(route, path, vid, pid, instanceProduct);
            trace?.Add("open-and-validate", true);
            var reading = route == AulaBatteryProtocol.Route.Sc580Receiver
                ? AulaBatteryProtocol.ReadIdentify(link, trace)
                : AulaBatteryProtocol.ReadSc630(link, NextSc630Sequence, trace);
            return reading with { Diagnostic = link.ReportMetadata + "；" + reading.Diagnostic };
        }, route == AulaBatteryProtocol.Route.Sc580Receiver ? 6000 : 4000);
    }

    private static byte NextSc630Sequence()
    {
        byte value;
        do { value = unchecked((byte)Interlocked.Increment(ref _sc630Sequence)); } while (value == 0);
        return value;
    }

    private sealed class AulaLink : ReferenceBatteryProtocol.ILink, IDisposable
    {
        private IntPtr handle, signal;
        private readonly AulaBatteryProtocol.Route route;
        internal string ReportMetadata { get; private set; }
        internal AulaLink(AulaBatteryProtocol.Route route, string path, ushort vid, ushort pid, string instanceProduct)
        {
            this.route = route;
            handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, route == AulaBatteryProtocol.Route.Sc580Receiver ? FILE_FLAG_OVERLAPPED : 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            { handle = IntPtr.Zero; throw new IOException("狼蛛专用接口打开失败 Win32=" + Marshal.GetLastWin32Error()); }
            try
            {
                var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(handle, ref attr) || attr.VendorID != vid || attr.ProductID != pid)
                    throw new IOException("打开后 VID/PID 不符，未发送查询");
                var caps = Caps(handle);
                string product = AulaBatteryProtocol.CollectionProduct(HidDiscovery.ReadHidProduct(handle, path), instanceProduct);
                if (AulaBatteryProtocol.Match(vid, pid, product, caps.UsagePage, caps.Usage,
                    caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength) != route)
                    throw new IOException("打开后产品名/集合不符，未发送查询");
                if (!HidD_GetPreparsedData(handle, out var prep)) throw new IOException("无法确认实际报告编号");
                try
                {
                    if (route == AulaBatteryProtocol.Route.Sc580Receiver)
                    {
                        bool inputOk = TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps, out byte input);
                        bool outputOk = TryGetReportId(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps, out byte output);
                        if (!AulaBatteryProtocol.AcceptsProfileReportId(route, inputOk, input, caps.NumberInputValueCaps, caps.NumberInputButtonCaps) ||
                            !AulaBatteryProtocol.AcceptsProfileReportId(route, outputOk, output, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps))
                            throw new IOException($"SC580 专用集合不是唯一 report0（in={inputOk}/{input:X2} V/B={caps.NumberInputValueCaps}/{caps.NumberInputButtonCaps}; out={outputOk}/{output:X2} V/B={caps.NumberOutputValueCaps}/{caps.NumberOutputButtonCaps}），未发送查询");
                        ReportMetadata = $"FF02/0000 in/out=33/33 report0（历史实测协议）；实际能力 in V/B={caps.NumberInputValueCaps}/{caps.NumberInputButtonCaps} out V/B={caps.NumberOutputValueCaps}/{caps.NumberOutputButtonCaps}";
                    }
                    else
                    {
                        bool featureOk = TryGetReportId(prep, 2, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps, out byte feature);
                        if (!AulaBatteryProtocol.AcceptsProfileReportId(route, featureOk, feature, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps))
                            throw new IOException($"SC630 Feature 编号能力与官方 report6 不符或不唯一（V/B={caps.NumberFeatureValueCaps}/{caps.NumberFeatureButtonCaps}），未发送查询");
                        ReportMetadata = $"FF01/0001 Feature6/41（官方协议）；实际能力 V/B={caps.NumberFeatureValueCaps}/{caps.NumberFeatureButtonCaps}";
                    }
                }
                finally { HidD_FreePreparsedData(prep); }
                if (route == AulaBatteryProtocol.Route.Sc580Receiver)
                {
                    signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                    if (signal == IntPtr.Zero) throw new IOException("无法创建查询事件");
                }
            }
            catch { Dispose(); throw; }
        }
        public long Now => Environment.TickCount64;
        public byte[] Read(int timeoutMs)
        {
            if (route != AulaBatteryProtocol.Route.Sc580Receiver) throw new InvalidOperationException();
            var b = new byte[33];
            return Transfer(handle, signal, b, false, timeoutMs, out int count) && count > 0 ? b.AsSpan(0, count).ToArray() : null;
        }
        public bool Write(byte[] request, int timeoutMs) => route == AulaBatteryProtocol.Route.Sc580Receiver &&
            request.AsSpan().SequenceEqual(AulaBatteryProtocol.IdentifyRequest()) &&
            Transfer(handle, signal, request, true, timeoutMs, out int count) && count == request.Length;
        public bool SetFeature(byte[] request) => route == AulaBatteryProtocol.Route.Sc630Pro && request.Length == 41 &&
            request.AsSpan().SequenceEqual(BuildAulaSc630BatteryRequest(request[4])) && HidD_SetFeature(handle, request, request.Length);
        public byte[] GetFeature(byte reportId, int length)
        {
            if (route != AulaBatteryProtocol.Route.Sc630Pro || reportId != 6 || length != 41) throw new InvalidOperationException();
            var b = new byte[41]; b[0] = 6; return HidD_GetFeature(handle, b, b.Length) ? b : null;
        }
        public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
        public void Dispose()
        {
            if (signal != IntPtr.Zero) { CloseHandle(signal); signal = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }
}
