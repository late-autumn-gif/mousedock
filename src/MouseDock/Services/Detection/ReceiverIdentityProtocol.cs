using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace MouseDock.Services.Detection;

internal static class ReceiverIdentityProtocol
{
    internal interface ILink : IDisposable
    {
        byte? FeatureReportId { get; }
        bool SetFeature(byte[] frame);
        byte[] GetFeature(byte[] frame);
        void Delay(int milliseconds);
    }

    internal static bool IsCandidate(ushort vid, ushort pid) => vid == 0x248A && pid == 0x5B2F;

    internal static bool MatchesChannel(ushort vid, ushort pid, ushort page, ushort usage,
        int input, int output, int feature) =>
        IsCandidate(vid, pid) && page == 0xFF00 && usage == 1 && input == 0 && output == 0 && feature == 33;

    internal static byte[] Request(byte sequence)
    {
        var frame = new byte[33];
        frame[0] = 12; frame[1] = 1; frame[2] = 32; frame[4] = sequence;
        return frame;
    }

    internal static DeviceProbeResult Capture(string instance, bool safeMode,
        IEnumerable<HidProbe.Node> nodes, Func<HidProbe.Node, ILink> open,
        Action<DeviceProbeResult> progress = null)
    {
        var result = new DeviceProbeResult();
        var text = new StringBuilder("MouseDock 248A:5B2F 身份诊断\n");
        var events = new StringBuilder();
        var clock = Stopwatch.StartNew();
        void Publish() => progress?.Invoke(new DeviceProbeResult
        {
            Text = text.ToString(), Jsonl = events.ToString(),
            Exchanges = result.Exchanges, Responses = result.Responses
        });
        void Record(string direction, string label, byte[] bytes, string note = "")
        {
            string hex = bytes == null ? "" : BitConverter.ToString(bytes);
            text.AppendLine($"{direction} {label}: {hex} {note}");
            events.AppendLine(JsonSerializer.Serialize(new ProbeEvent
                { Ms = clock.ElapsedMilliseconds, Dir = direction, Label = label, Hex = hex, Note = note }));
            Publish();
        }
        DeviceProbeResult Finish(string message)
        {
            text.AppendLine(message);
            result.Text = text.ToString();
            result.Jsonl = events.ToString();
            Publish();
            return result;
        }

        text.AppendLine("依据: AJAZZ Driver (X) 1.0.5.3 的 GetInfo；只收集身份，不发布电量或充电状态。");
        text.AppendLine("物理实例: " + instance);
        if (safeMode) return Finish("安全模式开启，未发送身份查询。");
        if (instance == null || !instance.StartsWith("instance-", StringComparison.Ordinal) ||
            instance.Length != 33 || !instance.AsSpan(9).ToString().All(Uri.IsHexDigit))
            return Finish("缺少精确物理实例，未发送身份查询。");
        try
        {
            var candidates = nodes.Where(n => string.Equals(n.InstanceId, instance, StringComparison.OrdinalIgnoreCase) &&
                MatchesChannel(n.Vid, n.Pid, n.Caps.UsagePage, n.Caps.Usage, n.Caps.InputReportByteLength,
                    n.Caps.OutputReportByteLength, n.Caps.FeatureReportByteLength)).ToArray();
            if (candidates.Length != 1)
                return Finish($"匹配接口数={candidates.Length}，需要唯一 FF00/0001 Feature33 接口；未发送查询。");
            var node = candidates[0];
            text.AppendLine("路径: " + node.Path);
            text.AppendLine($"USB 产品串: {node.Product}；版本: {node.Version:X4}");
            Publish();
            using var link = open(node);
            text.AppendLine("实际 Feature 报告编号: " + (link.FeatureReportId?.ToString("X2") ?? "未知或不唯一"));
            if (link.FeatureReportId != 12) return Finish("接口未确认唯一 report12，未发送查询。");

            // Official GetInfo at 0x44B250: SET33, wait16, then up to two
            // GET33 calls with a freshly populated header and a new sequence.
            byte sequence = 0;
            var request = Request(sequence++);
            result.Exchanges++;
            Record("TX", "SET_FEATURE", request);
            if (!link.SetFeature(request)) return Finish("身份查询 SET_FEATURE 失败，未继续读取。");
            for (int attempt = 0; attempt < 2; attempt++)
            {
                link.Delay(16);
                var getRequest = Request(sequence++);
                result.Exchanges++;
                Record("TX", "GET_FEATURE", getRequest);
                byte[] response;
                try { response = link.GetFeature(getRequest); }
                catch (Exception ex)
                {
                    Record("ERROR", "GET_FEATURE", null, ex.Message);
                    continue;
                }
                if (response is { Length: > 0 }) result.Responses++;
                Record("RX", "GET_FEATURE", response);
                if (!TryIdentity(response, out string identity)) continue;
                text.AppendLine("身份串: " + identity);
                string candidate = identity switch
                {
                    "M137" => "狼蛛 SC580（SC580 官方驱动身份配置）",
                    "M139" => "AJ139 Pro",
                    "M620" => "AJ159 / AJ159 MC",
                    "M184" => "AJ179",
                    _ => null
                };
                if (candidate != null) text.AppendLine("候选型号: " + candidate + "（官方配置映射，尚待实物核对）");
                return Finish(candidate == null
                    ? "官方配置未收录此身份串，具体型号保持未知。"
                    : "此结果是接收器返回的身份线索；不能单凭公共编号确认零售型号。");
            }
            return Finish("未收到有效身份响应，具体型号保持未知。原始回包已保留。");
        }
        catch (Exception ex)
        {
            Record("ERROR", "IDENTITY", null, ex.Message);
            return Finish("身份读取失败，具体型号保持未知。");
        }
    }

    internal static bool TryIdentity(byte[] frame, out string identity)
    {
        identity = null;
        if (frame == null || frame.Length != 33 || frame[0] != 12 ||
            frame[1] != 1 || frame[2] != 32 || frame[3] != 0) return false;
        // The official lookup reads precisely bytes 8..11. Keep unknown Mxxx
        // values as evidence; an echo has zeroes here and is never an identity.
        if (frame[8] != (byte)'M') return false;
        for (int i = 9; i < 12; i++) if (frame[i] < '0' || frame[i] > '9') return false;
        identity = Encoding.ASCII.GetString(frame, 8, 4);
        return true;
    }
}
