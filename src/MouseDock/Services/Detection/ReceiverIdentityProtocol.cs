using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MouseDock.Services.Detection;

/// <summary>
/// 248A:5B2F 的身份诊断。
///
/// 帧与 M137FeatureProtocol 同源（0C 01 20 00 seq，PR 原文写的十进制 12），区别是走 FF00/0001 的 Feature 33 通道：
/// 先 SetFeature 一次，再 GetFeature 两次取身份串。只读身份，不碰电量。
/// 依据 AJAZZ Driver (X) 1.0.5.3 的 GetInfo。
/// </summary>
internal static class ReceiverIdentityProtocol
{
    internal const ushort Vid = 0x248A;
    internal const ushort Pid = 0x5B2F;

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagOverlapped = 0x40000000;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    internal static bool IsCandidate(ushort vid, ushort pid) => vid == Vid && pid == Pid;

    internal static bool MatchesChannel(ushort vid, ushort pid, ushort page, ushort usage,
        int input, int output, int feature) =>
        IsCandidate(vid, pid) && page == 0xFF00 && usage == 1 &&
        input == 0 && output == 0 && feature == 33;

    internal static byte[] Request(byte sequence)
    {
        var frame = new byte[33];
        frame[0] = 12; frame[1] = 1; frame[2] = 32; frame[4] = sequence;
        return frame;
    }

    /// <summary>
    /// 只读身份采集。返回的 Text 进报告，Jsonl 进 probe-events。
    /// </summary>
    internal static DeviceProbeResult Capture(string instance, bool safeMode,
        IEnumerable<HidProbe.Node> nodes, Action<DeviceProbeResult> progress = null)
    {
        var result = new DeviceProbeResult();
        var text = new StringBuilder("MouseDock 248A:5B2F 身份诊断\n");
        var events = new StringBuilder();
        var clock = Stopwatch.StartNew();
        int exchanges = 0;
        var responses = new List<string>();

        void Publish()
        {
            result.Text = text.ToString();
            result.Jsonl = events.ToString();
            progress?.Invoke(new DeviceProbeResult { Text = result.Text, Jsonl = result.Jsonl });
        }

        void Record(string direction, string label, byte[] bytes, string note = "")
        {
            string hex = bytes == null ? "" : BitConverter.ToString(bytes);
            text.AppendLine($"{direction} {label}: {hex} {note}".TrimEnd());
            try
            {
                events.AppendLine(JsonSerializer.Serialize(new ProbeEvent
                {
                    Ms = clock.ElapsedMilliseconds, Dir = direction, Label = label, Hex = hex, Note = note
                }));
            }
            catch { }
            Publish();
        }

        DeviceProbeResult Finish(string message)
        {
            text.AppendLine(message);
            Publish();
            return result;
        }

        text.AppendLine("依据: AJAZZ Driver (X) 1.0.5.3 的 GetInfo；只收集身份，不发布电量或充电状态。");
        text.AppendLine("物理实例: " + instance);

        if (safeMode) return Finish("安全模式开启，未发送身份查询。");
        if (string.IsNullOrWhiteSpace(instance) || !instance.StartsWith("instance-", StringComparison.Ordinal))
            return Finish("缺少精确物理实例，未发送身份查询。");

        try
        {
            var candidates = (nodes ?? Enumerable.Empty<HidProbe.Node>())
                .Where(n => string.Equals(InstanceScope.ForPath(n.Path, ""), instance, StringComparison.OrdinalIgnoreCase) &&
                            MatchesChannel(n.Vid, n.Pid, n.Caps.UsagePage, n.Caps.Usage,
                                n.Caps.InputReportByteLength, n.Caps.OutputReportByteLength,
                                n.Caps.FeatureReportByteLength))
                .ToArray();
            if (candidates.Length != 1)
                return Finish($"匹配接口数={candidates.Length}，需要唯一 FF00/0001 Feature33 接口；未发送查询。");

            var node = candidates[0];
            text.AppendLine("路径: " + node.Path);
            text.AppendLine($"USB 产品串: {node.Product}；版本: {node.Version:X4}");
            Publish();

            using var handle = CreateFile(node.Path, GenericRead | GenericWrite,
                ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
            if (handle.IsInvalid)
                return Finish("打开接口失败 Win32=" + Marshal.GetLastWin32Error());

            byte sequence = 0;
            var request = Request(sequence++);
            exchanges++;
            Record("TX", "SET_FEATURE", request);
            if (!HidD_SetFeature(handle, request, request.Length))
                return Finish("身份查询 SET_FEATURE 失败 Win32=" + Marshal.GetLastWin32Error());

            for (int attempt = 0; attempt < 2; attempt++)
            {
                Thread.Sleep(16);
                var getRequest = Request(sequence++);
                exchanges++;
                Record("TX", "GET_FEATURE", getRequest);
                var buffer = new byte[33];
                bool ok;
                try { ok = HidD_GetFeature(handle, buffer, buffer.Length); }
                catch (Exception ex) { return Finish("GET_FEATURE 异常：" + ex.GetType().Name); }
                if (!ok)
                {
                    text.AppendLine("GET_FEATURE 失败 Win32=" + Marshal.GetLastWin32Error());
                    continue;
                }
                Record("RX", "GET_FEATURE", buffer);
                responses.Add(Convert.ToHexString(buffer));
                string decoded = DecodeIdentity(buffer);
                if (!string.IsNullOrEmpty(decoded))
                {
                    text.AppendLine("身份串: " + decoded);
                    Finish($"已取得身份串，共 {exchanges} 次交换。");
                    result.Exchanges = exchanges;
                    return result;
                }
            }

            Finish($"未能从 {exchanges} 次交换中解析出身份串。");
            result.Exchanges = exchanges;
            return result;
        }
        catch (Exception ex)
        {
            return Finish("身份诊断异常：" + ex.GetType().Name + " " + ex.Message);
        }
    }

    /// <summary>
    /// 身份串通常是从偏移 5 开始的 ASCII。取第一段可打印内容，长度上限 24。
    /// </summary>
    internal static string DecodeIdentity(byte[] buffer)
    {
        if (buffer == null || buffer.Length <= 5) return null;
        var text = new StringBuilder();
        for (int i = 5; i < buffer.Length && text.Length < 24; i++)
        {
            byte b = buffer[i];
            if (b == 0) break;
            if (b < 0x20 || b > 0x7E) { if (text.Length > 0) break; continue; }
            text.Append((char)b);
        }
        return text.Length >= 3 ? text.ToString() : null;
    }

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int length);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, int length);
}


