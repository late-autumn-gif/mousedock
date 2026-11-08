using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MouseDock.Models;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static readonly object identityGate = new();
    private static readonly Dictionary<string, Task> identityPending =
        new(StringComparer.OrdinalIgnoreCase);

    internal static DeviceProbeResult ProbeReceiverIdentity(DeviceInfo target, bool safeMode)
    {
        string instance = target.Id;
        if (safeMode || !target.RecognitionEnabled)
            return new DeviceProbeResult { Text = "安全模式或停用识别已开启，未发送身份查询。" };
        if (string.IsNullOrWhiteSpace(instance))
            return new DeviceProbeResult { Text = "缺少物理实例，未发送身份查询。" };
        var latest = new DeviceProbeResult { Text = "MouseDock 248A:5B2F 身份诊断\n物理实例: " + instance + "\n正在枚举接口。\n" };
        return RunReceiverQuery(instance, () =>
            ReceiverIdentityProtocol.Capture(instance, false,
                HidProbe.Enumerate(0x248A, 0x5B2F, includeInstance: true), n => new ReceiverIdentityLink(n),
                snapshot => Volatile.Write(ref latest, snapshot)),
            message =>
            {
                var snapshot = Volatile.Read(ref latest);
                return new DeviceProbeResult
                {
                    Text = snapshot.Text + "\n" + message,
                    Jsonl = snapshot.Jsonl, Exchanges = snapshot.Exchanges, Responses = snapshot.Responses
                };
            });
    }

    internal static ReferenceBatteryProtocol.Reading ReadM137Feature(string path)
    {
        if (string.IsNullOrWhiteSpace(InstanceScope.Current) || !InstanceScope.Allows(path))
            return ReferenceBatteryProtocol.Reading.Failed("缺少匹配的物理实例，未发送 Feature 查询");
        return RunReceiverQuery(InstanceScope.Current, () =>
        {
            using var link = new ReceiverIdentityLink(new HidProbe.Node { Path = path }, requireReportedProduct: true);
            return M137FeatureProtocol.Read(link);
        }, ReferenceBatteryProtocol.Reading.Failed);
    }

    // Diagnostics and polling share the same instance gate and pending limit.
    // A native call that outlives its caller keeps its own handle and buffers.
    internal static T RunReceiverQuery<T>(string instance, Func<T> operation, Func<string, T> failed,
        int timeoutMs = 3000)
    {
        Task<T> worker;
        lock (identityGate)
        {
            foreach (var key in new List<string>(identityPending.Keys))
                if (identityPending[key].IsCompleted) identityPending.Remove(key);
            if (identityPending.ContainsKey(instance) || identityPending.Count >= 4)
                return failed("已有接收器查询仍在等待原生接口，未重复发送命令。");
            worker = Task.Factory.StartNew(() =>
            {
                try { return operation(); }
                catch (Exception ex) { return failed("接收器查询失败: " + ex.Message); }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            identityPending[instance] = worker;
        }
        if (!worker.Wait(timeoutMs))
            return failed("接收器查询超时，未发布读数；工作线程保留原生资源直到调用结束，迟到结果不会采用。");
        lock (identityGate)
            if (identityPending.TryGetValue(instance, out var current) && ReferenceEquals(current, worker))
                identityPending.Remove(instance);
        return worker.Result;
    }

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    private static extern bool IdentityDeviceIoControl(IntPtr handle, uint code, IntPtr input,
        int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);

    private sealed class ReceiverIdentityLink : ReceiverIdentityProtocol.ILink
    {
        private IntPtr handle;
        public byte? FeatureReportId { get; }

        internal ReceiverIdentityLink(HidProbe.Node node, bool requireReportedProduct = false)
        {
            handle = CreateFile(node.Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                handle = IntPtr.Zero;
                throw new IOException("身份接口打开失败 Win32=" + Marshal.GetLastWin32Error());
            }
            try
            {
                var attr = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                var caps = Caps(handle);
                if (!HidD_GetAttributes(handle, ref attr) ||
                    !ReceiverIdentityProtocol.MatchesChannel(attr.VendorID, attr.ProductID,
                        caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength,
                        caps.FeatureReportByteLength))
                    throw new IOException("打开后的 VID/PID 或集合能力不符，未发送查询");
                if (requireReportedProduct && !M137FeatureProtocol.IsCandidate(attr.VendorID, attr.ProductID,
                    HidDiscovery.ReadHidProduct(handle, node.Path)))
                    throw new IOException("打开后的产品串不符，未发送查询");
                if (!HidD_GetPreparsedData(handle, out var prep))
                    throw new IOException("无法获取报告编号，未发送查询");
                try
                {
                    if (TryGetReportId(prep, 2, caps.NumberFeatureValueCaps, caps.NumberFeatureButtonCaps, out byte id))
                        FeatureReportId = id;
                }
                finally { HidD_FreePreparsedData(prep); }
            }
            catch { Dispose(); throw; }
        }

        private void Validate(byte[] frame)
        {
            if (FeatureReportId != 12 || frame == null || frame.Length != 33 ||
                frame[4] == 255 || !frame.AsSpan().SequenceEqual(ReceiverIdentityProtocol.Request(frame[4])))
                throw new InvalidOperationException("身份接口只允许官方 GetInfo 请求");
        }
        public bool SetFeature(byte[] frame)
        {
            Validate(frame);
            if (!HidD_SetFeature(handle, frame, frame.Length))
                throw new IOException("SET_FEATURE 失败 Win32=" + Marshal.GetLastWin32Error());
            return true;
        }
        public byte[] GetFeature(byte[] frame)
        {
            Validate(frame);
            // The official IOCTL uses the prepared request as both input and output.
            // Pin it through the synchronous call; the enclosing worker bounds UI wait.
            var bytes = (byte[])frame.Clone();
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                if (!IdentityDeviceIoControl(handle, 0x000B0192, pinned.AddrOfPinnedObject(), 33,
                    pinned.AddrOfPinnedObject(), 33, out int returned, IntPtr.Zero))
                    throw new IOException("GET_FEATURE 失败 Win32=" + Marshal.GetLastWin32Error());
                if (returned < 0 || returned > bytes.Length) throw new IOException("GET_FEATURE 返回长度无效");
                return bytes.AsSpan(0, returned).ToArray();
            }
            finally { pinned.Free(); }
        }
        public void Delay(int milliseconds) => Thread.Sleep(milliseconds);
        public void Dispose()
        {
            if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; }
        }
    }
}
