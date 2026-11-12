using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    /// <summary>Only an exact, already-selected instance path is passed to this worker.
    /// The worker owns the handle until the native operation has actually returned.</summary>
    internal static ReferenceBatteryProtocol.Reading ReadReferenceBattery(
        ReferenceProfiles.Profile profile, string path) => ReadReferenceBattery(profile, path, false, null);

    internal static ReferenceBatteryProtocol.Reading ReadReferenceBattery(
        ReferenceProfiles.Profile profile, string path, bool readDpi, MouseDpiChange change)
    {
        if (profile == null || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("未匹配设备实例，未发送命令");
        readDpi = readDpi && ScyroxV12Dpi.Supports(profile) &&
            (change == null || change.Expected.Protocol == MouseDpiProtocol.ScyroxV12);
        int budget = profile.Battery switch
        {
            ReferenceProfiles.Protocol.SoraV2 => 1000,
            ReferenceProfiles.Protocol.Compx => profile.ReadIdentity ? 3300 : 1800,
            _ => 4600
        };
        return ReferenceQueryGate.Run(path, () =>
        {
            using var link = new NativeReferenceLink(profile, path);
            MouseDpiState dpi = readDpi && link.HasReport8 ? ScyroxV12Dpi.ReadOrChange(link, change) : null;
            return ReferenceBatteryProtocol.Read(profile, link) with { Dpi = dpi };
        }, budget + (readDpi ? 5600 : 0));
    }

    private sealed class NativeReferenceLink : ReferenceBatteryProtocol.ILink, IDisposable
    {
        private IntPtr handle;
        private IntPtr signal;
        private readonly int inputLength;
        private readonly ReferenceProfiles.Protocol protocol;

        internal bool HasReport8
        {
            get
            {
                var caps = Caps(handle);
                if (!HidD_GetPreparsedData(handle, out var data)) return false;
                try
                {
                    return TryGetReportId(data, 0, caps.NumberInputValueCaps, caps.NumberInputButtonCaps, out byte input) && input == 8 &&
                        TryGetReportId(data, 1, caps.NumberOutputValueCaps, caps.NumberOutputButtonCaps, out byte output) && output == 8;
                }
                finally { HidD_FreePreparsedData(data); }
            }
        }

        internal NativeReferenceLink(ReferenceProfiles.Profile profile, string path)
        {
            protocol = profile.Battery;
            // Rapoo is passive: request read permission only and never open an output transaction.
            uint access = protocol == ReferenceProfiles.Protocol.Rapoo ? GENERIC_READ : GENERIC_READ | GENERIC_WRITE;
            uint flags = protocol == ReferenceProfiles.Protocol.SoraV2 ? 0 : FILE_FLAG_OVERLAPPED;
            handle = CreateFile(path, access, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                handle = IntPtr.Zero;
                throw new System.IO.IOException("接口打开失败 Win32=" + Marshal.GetLastWin32Error());
            }
            try
            {
                var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                if (!HidD_GetAttributes(handle, ref attr) || attr.VendorID != profile.Vid || attr.ProductID != profile.Pid)
                    throw new System.IO.IOException("打开后的 VID/PID 与配置不符");
                var caps = Caps(handle);
                if (!ReferenceProfiles.AcceptsChannel(profile, caps.UsagePage, caps.Usage,
                    caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength))
                    throw new System.IO.IOException("打开后的集合签名与配置不符");
                inputLength = caps.InputReportByteLength;
                if (protocol != ReferenceProfiles.Protocol.SoraV2)
                {
                    signal = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                    if (signal == IntPtr.Zero) throw new System.IO.IOException("创建异步事件失败");
                }
            }
            catch { Dispose(); throw; }
        }

        public long Now => Environment.TickCount64;
        public byte[] Read(int timeoutMs)
        {
            if (protocol == ReferenceProfiles.Protocol.SoraV2) throw new InvalidOperationException();
            var buffer = new byte[inputLength];
            if (!Transfer(handle, signal, buffer, false, Math.Max(1, timeoutMs), out int count) || count <= 0)
                return null;
            return buffer.AsSpan(0, count).ToArray();
        }

        public bool Write(byte[] request, int timeoutMs)
        {
            if (protocol is not (ReferenceProfiles.Protocol.Compx or ReferenceProfiles.Protocol.ScyroxV12))
                throw new InvalidOperationException("该协议没有 Output 查询");
            return Transfer(handle, signal, request, true, timeoutMs, out int count) && count == request.Length;
        }

        public bool SetFeature(byte[] request)
        {
            if (protocol != ReferenceProfiles.Protocol.SoraV2) throw new InvalidOperationException();
            return HidD_SetFeature(handle, request, request.Length);
        }

        public byte[] GetFeature(byte reportId, int length)
        {
            if (protocol != ReferenceProfiles.Protocol.SoraV2 || reportId != 5 || length != 32)
                throw new InvalidOperationException();
            var buffer = new byte[length];
            buffer[0] = reportId;
            return HidD_GetFeature(handle, buffer, buffer.Length) ? buffer : null;
        }

        public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
        public void Dispose()
        {
            if (signal != IntPtr.Zero) { CloseHandle(signal); signal = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }
}

/// <summary>A timed-out native call must retain its handle and buffers on its worker.
/// At most four pending workers; subsequent polls never reuse their late readings.</summary>
internal static class ReferenceQueryGate
{
    private static readonly object gate = new();
    private static readonly Dictionary<string, Task<ReferenceBatteryProtocol.Reading>> pending =
        new(StringComparer.OrdinalIgnoreCase);
    internal const int MaxPending = 4;
    internal static int PendingCount
    {
        get
        {
            lock (gate)
            {
                int count = 0;
                foreach (var task in pending.Values) if (!task.IsCompleted) count++;
                return count;
            }
        }
    }

    internal static ReferenceBatteryProtocol.Reading Run(string path,
        Func<ReferenceBatteryProtocol.Reading> operation, int timeoutMs)
    {
        Task<ReferenceBatteryProtocol.Reading> worker;
        lock (gate)
        {
            foreach (string key in new List<string>(pending.Keys))
                if (pending[key].IsCompleted) pending.Remove(key); // Discard late results; never promote them.
            if (pending.ContainsKey(path))
                return ReferenceBatteryProtocol.Reading.Failed("上次原生查询尚未结束，本轮未启动重复查询");
            if (pending.Count >= MaxPending)
                return ReferenceBatteryProtocol.Reading.Failed("原生查询等待数已达上限，本轮未启动查询");
            worker = Task.Factory.StartNew(() =>
            {
                try { return operation(); }
                catch (Exception ex) { return ReferenceBatteryProtocol.Reading.Failed("接口查询失败：" + ex.Message); }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            pending[path] = worker;
        }
        if (!worker.Wait(timeoutMs))
            return ReferenceBatteryProtocol.Reading.Failed("原生查询超时；保留工作线程持有的资源直至原生调用结束");
        lock (gate)
            if (pending.TryGetValue(path, out var current) && ReferenceEquals(current, worker)) pending.Remove(path);
        return worker.Result;
    }
}
