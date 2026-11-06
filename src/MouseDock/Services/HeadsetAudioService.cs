using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.CoreAudioApi;

namespace MouseDock.Services;

internal sealed record AudioEndpointInfo(string Id, string Name, bool Capture, Guid Container,
    int FormFactor, int? Volume, bool? Muted, string Error = "")
{
    // Windows ENDPOINT_FORM_FACTOR: Headphones=3, Headset=5.
    internal bool IsHeadset => !Capture && (FormFactor is 3 or 5 ||
        System.Text.RegularExpressions.Regex.IsMatch(Name ?? "", @"(?:^|\()MCHOSE\s+V9\s*PRO(?:\)|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
        new[] { "耳机", "headphone", "headset", "earphone", "earbud" }
            .Any(word => Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    public override string ToString() => Name;
}

internal sealed record AudioActionResult(bool Success, string Message, int? Volume = null, bool? Muted = null);

internal interface IHeadsetAudioBackend
{
    IReadOnlyList<AudioEndpointInfo> Enumerate();
    AudioActionResult SetVolume(string endpointId, int percent);
    AudioActionResult SetMicrophoneMute(string endpointId, bool muted);
}

internal static class HeadsetAudioPolicy
{
    internal static string MicrophoneFor(AudioEndpointInfo output, IReadOnlyList<AudioEndpointInfo> endpoints,
        IReadOnlyDictionary<string, string> bindings)
    {
        if (output == null || output.Capture) return null;
        if (bindings != null && bindings.TryGetValue(output.Id, out string saved))
            // A disconnected manual choice must never fall back to a different microphone.
            return endpoints.Any(e => e.Capture && e.Id == saved) ? saved : null;
        // A sound-card container can include a laptop microphone and a headphone jack.
        // Auto-pair only when Windows explicitly describes either endpoint as a headset.
        if (output.Container == Guid.Empty || output.Container == new Guid("00000000-0000-0000-ffff-ffffffffffff"))
            return null;
        var matches = endpoints.Where(e => e.Capture && e.Container == output.Container).ToArray();
        return matches.Length == 1 && (output.FormFactor == 5 || matches[0].FormFactor == 5) ? matches[0].Id : null;
    }
}

/// <summary>Windows names and endpoint controls only. No audio recording, battery or vendor queries.</summary>
internal sealed class WindowsHeadsetAudioBackend : IHeadsetAudioBackend
{
    private static readonly PropertyKey ContainerKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);

    public IReadOnlyList<AudioEndpointInfo> Enumerate()
    {
        var result = new List<AudioEndpointInfo>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                string id = device.ID, name;
                try { name = device.FriendlyName; } catch { name = "未命名音频设备"; }
                Guid container = Guid.Empty;
                int form = -1;
                try
                {
                    object value = device.Properties[ContainerKey]?.Value;
                    if (value is Guid guid) container = guid;
                    else if (value != null) Guid.TryParse(value.ToString(), out container);
                }
                catch { }
                try { form = Convert.ToInt32(device.Properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor]?.Value ?? -1); }
                catch { }
                int? volume = null;
                bool? muted = null;
                string error = "";
                if (flow == DataFlow.Render) try
                {
                    float scalar=device.AudioEndpointVolume.MasterVolumeLevelScalar;
                    if (!float.IsFinite(scalar) || scalar<0 || scalar>1) throw new InvalidOperationException();
                    volume = (int)Math.Round(scalar * 100);
                }
                catch { error = "Windows 暂未提供此设备的音量状态"; }
                try { muted=device.AudioEndpointVolume.Mute; }
                catch { error = "Windows 暂未提供此设备的静音状态"; }
                result.Add(new(id, name, flow == DataFlow.Capture, container, form, volume, muted, error));
                }
                catch { /* An endpoint may disappear while enumerating; keep other devices available. */ }
            }
        }
        return result;
    }

    public AudioActionResult SetVolume(string endpointId, int percent)
    {
        if (percent is < 0 or > 100 || string.IsNullOrWhiteSpace(endpointId))
            return new(false, "请选择设备，并使用 0–100 的音量");
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(endpointId);
            if (device.DataFlow != DataFlow.Render || device.State != DeviceState.Active)
                return new(false, "所选播放设备已断开，请刷新设备");
            device.AudioEndpointVolume.MasterVolumeLevelScalar = percent / 100f;
            float scalar=device.AudioEndpointVolume.MasterVolumeLevelScalar;
            if (!float.IsFinite(scalar) || scalar<0 || scalar>1) return new(false,"Windows 未提供有效的音量确认值，请刷新后重试");
            int actual = (int)Math.Round(scalar * 100);
            bool success = Math.Abs(actual - percent) <= 1;
            return new(success, success ? $"音量已设为 {actual}%" : "Windows 未确认音量变更，请刷新后重试", actual);
        }
        catch { return new(false, "音量调节失败：设备可能已断开或不允许控制"); }
    }

    public AudioActionResult SetMicrophoneMute(string endpointId, bool muted)
    {
        if (string.IsNullOrWhiteSpace(endpointId)) return new(false, "请先选择对应的麦克风");
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(endpointId);
            if (device.DataFlow != DataFlow.Capture || device.State != DeviceState.Active)
                return new(false, "所选麦克风已断开，请刷新设备");
            device.AudioEndpointVolume.Mute = muted;
            bool actual = device.AudioEndpointVolume.Mute;
            bool success = actual == muted;
            return new(success, success ? (actual ? "麦克风已静音" : "麦克风已恢复")
                : "Windows 未确认麦克风变更，请刷新后重试", Muted: actual);
        }
        catch { return new(false, "麦克风操作失败：设备可能已断开或不允许控制"); }
    }
}
