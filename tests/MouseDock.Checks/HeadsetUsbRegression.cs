using System;
using System.Collections.Generic;
using System.Linq;
using MouseDock.Services;

/// <summary>
/// 2.4G 耳机的音频端点识别回归。
///
/// 现场：K01S 走 USB Audio Class（wdma_usb.inf），Windows 给它报 FormFactor=1（Other），
/// 名字只有型号，三条文字判定一条都不满足，耳机控制里就看不见它。
/// 判据改成「USB 总线上的播放端点也算耳机」，主板声卡和显卡音频是 HDAUDIO，不受影响。
/// </summary>
internal static class HeadsetUsbRegression
{
    internal static void Run()
    {
        // 2.4G 接收器：USB、FormFactor=1、名字无耳机字样 —— 必须认出来。
        Headset("USB UAC 端点即使 FormFactor=Other 也算耳机",
            new AudioEndpointInfo("usb", "K01S", false, Guid.NewGuid(), 1, 50, false, "", Usb: true));

        // 同一个端点如果不是 USB，就不该靠这条进来。
        NotHeadset("非 USB 且 FormFactor=Other 不误判为耳机",
            new AudioEndpointInfo("pci", "K01S", false, Guid.NewGuid(), 1, 50, false, "", Usb: false));

        // 主板声卡：HDAUDIO，FormFactor 常见 1。
        NotHeadset("主板声卡不被 USB 规则带进来",
            new AudioEndpointInfo("hda", "Realtek(R) Audio", false, Guid.NewGuid(), 1, 50, false, "", Usb: false));

        // 显卡音频：FormFactor=9（Digital），就算总线报了 USB 也不该当耳机。
        Headset("FormFactor=5 的 USB 耳麦",
            new AudioEndpointInfo("usb2", "Air Plus Pro3 Hands-Free", false, Guid.NewGuid(), 5, 50, false, "", Usb: true));

        // 采集端点永远不算耳机。
        NotHeadset("采集端点不算耳机",
            new AudioEndpointInfo("mic", "K01S", true, Guid.NewGuid(), 5, 50, false, "", Usb: true));

        // 原有文字判据不能退化。
        Headset("关键词耳机仍然认得出",
            new AudioEndpointInfo("kw", "某品牌 headset", false, Guid.NewGuid(), 1, 50, false, "", Usb: false));

        // USB 耳机的麦克风自动配对：同 Container、FormFactor=5。
        var output = new AudioEndpointInfo("out", "K01S", false, Guid.NewGuid(), 1, 50, false, "", Usb: true);
        var mic = new AudioEndpointInfo("mic2", "麦克风 (K01S)", true, output.Container, 5, null, false, "", Usb: true);
        var endpoints = new List<AudioEndpointInfo> { output, mic };
        var paired = HeadsetAudioPolicy.MicrophoneFor(output, endpoints, new Dictionary<string, string>());
        Check(paired == "mic2", "USB 耳机按 Container 自动配对麦克风");
    }

    private static void Headset(string what, AudioEndpointInfo endpoint)
        => Check(endpoint.IsHeadset, what);

    private static void NotHeadset(string what, AudioEndpointInfo endpoint)
        => Check(!endpoint.IsHeadset, what);

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("headset endpoint rule regression: " + what);
        Console.WriteLine("PASS " + what);
    }
}
