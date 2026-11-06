using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    // Report evidence: 2026-11-05 09:23; no report IDs or battery responses were supplied.
    internal static bool IsReportedX23Se(ushort vid, ushort pid, string product) =>
        vid == 0xA8A5 && pid == 0x2255 && !string.IsNullOrWhiteSpace(product) &&
        string.Equals(NormalizeProduct(product), "EWEADN X23SE", StringComparison.OrdinalIgnoreCase);

    internal static bool IsReportedX23SeCollection(ushort vid, ushort pid, string product, string path,
        ushort page, ushort usage, int input, int output, int feature) =>
        IsReportedX23Se(vid, pid, product) && page == 0xFF01 && usage == 0x10 &&
        input == 65 && output == 65 && feature == 0 && path != null &&
        (path.Contains("&mi_02#", StringComparison.OrdinalIgnoreCase) || path.Contains("&mi_02&", StringComparison.OrdinalIgnoreCase));

    internal static ReferenceBatteryProtocol.Reading ReadReportedX23Se(string path)
    {
        if (!InstanceScope.Allows(path)) return ReferenceBatteryProtocol.Reading.Failed("设备实例不符，未发送查询");
        return ReferenceQueryGate.Run(path, () =>
        {
            using var link = new ReportedFreqLink(path);
            return ReadReportedFreqExchange(link, link.InputId, link.OutputId);
        }, 3600);
    }

    internal static ReferenceBatteryProtocol.Reading ReadReportedFreqExchange(ReferenceBatteryProtocol.ILink link,
        byte inputId, byte outputId)
    {
        if (inputId == 0 || outputId == 0)
            return ReferenceBatteryProtocol.Reading.Failed("65/65 接口没有明确的非零报告编号，未猜测对齐或发送查询");
        var packet = BuildFreqSchemeAPacket(outputId, 65);
        int level = -1, rejected = 0, echoes = 0;
        bool charging = false;
        string acceptedFrames = "";
        string prefix = $"FREQ a in=0x{inputId:X2}/65 out=0x{outputId:X2}/65";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            bool drained = false;
            for (int i = 0; i < 32; i++) if (link.Read(1) is not { Length: > 0 }) { drained = true; break; }
            if (!drained) return ReferenceBatteryProtocol.Reading.Failed(prefix + "；旧队列未清空，停止查询");
            if (!link.Write(packet, 300)) return ReferenceBatteryProtocol.Reading.Failed(prefix + "；电量请求未完整发送");
            long deadline = link.Now + 1200;
            bool found = false;
            string last = "无";
            while (link.Now < deadline)
            {
                var raw = link.Read((int)Math.Max(1, Math.Min(100, deadline - link.Now)));
                if (raw is not { Length: > 0 }) continue;
                if (raw.AsSpan().SequenceEqual(packet)) { echoes++; continue; }
                if (!TryParseFreqFrame(raw, "a", inputId, 65, out level, out charging))
                { rejected++; last = Convert.ToHexString(raw.AsSpan(0, Math.Min(raw.Length, 65))); continue; }
                found = true;
                acceptedFrames += $" RX{attempt + 1}=" + Convert.ToHexString(raw);
                break;
            }
            if (!found) return ReferenceBatteryProtocol.Reading.Failed(prefix +
                $"；第 {attempt + 1}/2 次无有效应答；回显={echoes} 无效={rejected} 最近拒绝帧={last}" + acceptedFrames);
        }
        return new(level, charging, true,
            charging ? ReferenceBatteryProtocol.State.Charging : ReferenceBatteryProtocol.State.Online,
            prefix + $"；本轮两次有效接收器应答，回显={echoes} 无效={rejected}；不据此推断无线外设在线状态" + acceptedFrames);
    }

    private sealed class ReportedFreqLink : ReferenceBatteryProtocol.ILink, IDisposable
    {
        private IntPtr handle, signal;
        internal readonly byte InputId, OutputId;
        internal ReportedFreqLink(string path)
        {
            handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            { handle = IntPtr.Zero; throw new IOException("X23SE 专用接口打开失败，可能被驱动占用"); }
            try
            {
                var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(handle, ref attr)) throw new IOException("无法核对接口 VID/PID");
                var caps = Caps(handle);
                string product = HidDiscovery.ReadHidProduct(handle, path);
                if (!IsReportedX23SeCollection(attr.VendorID, attr.ProductID, product, path, caps.UsagePage,
                    caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength))
                    throw new IOException("接口产品名或集合签名不符合报告边界，未发送查询");
                if (!HidD_GetPreparsedData(handle, out var prep)) throw new IOException("无法读取报告编号，未发送查询");
                try
                {
                    if (!TryGetReportId(prep, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps, out InputId) ||
                        !TryGetReportId(prep, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps, out OutputId))
                        throw new IOException($"报告编号缺失或不唯一（in V/B={caps.NumberInputValueCaps}/{caps.NumberInputButtonCaps}, out V/B={caps.NumberOutputValueCaps}/{caps.NumberOutputButtonCaps}），未发送查询");
                }
                finally { HidD_FreePreparsedData(prep); }
                signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (signal == IntPtr.Zero) throw new IOException("无法创建异步查询事件");
            }
            catch { Dispose(); throw; }
        }
        public long Now => Environment.TickCount64;
        public byte[] Read(int timeoutMs)
        {
            var buffer = new byte[65];
            return Transfer(handle, signal, buffer, false, timeoutMs, out int count) && count > 0 ? buffer.AsSpan(0, count).ToArray() : null;
        }
        public bool Write(byte[] request, int timeoutMs) => request.Length == 65 &&
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
