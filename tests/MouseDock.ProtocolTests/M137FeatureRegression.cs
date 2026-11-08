using System.Text;
using MouseDock.Models;
using MouseDock.Services.Detection;

/// <summary>
/// 248A:5B2F 的官方 GetInfo 协议（PR #8，AJAZZ Driver X 1.0.5.3）。
/// 全部离线断言，不打开任何接口。
/// </summary>
internal static class M137FeatureRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string why)
        {
            count++;
            if (!ok) throw new Exception("M137: " + why);
        }

        // 只认 248A:5B2F + Wireless-Receiver 产品串
        Check(M137FeatureProtocol.IsCandidate(0x248A, 0x5B2F, "Wireless-Receiver"), "exact receiver admitted");
        Check(!M137FeatureProtocol.IsCandidate(0x248A, 0x5B2F, "USB Receiver"), "other product string rejected");
        Check(!M137FeatureProtocol.IsCandidate(0x248A, 0x5C2F, "Wireless-Receiver"), "5C2F is not the M137 route");
        Check(!M137FeatureProtocol.IsCandidate(0x249A, 0x5B2F, "Wireless-Receiver"), "249A is not the M137 route");

        // 官方集合：FF00/0001，无输入无输出，Feature 33
        Check(M137FeatureProtocol.MatchesChannel(0x248A, 0x5B2F, 0xFF00, 1, 0, 0, 33),
            "official FF00/0001 Feature33 channel");
        Check(!M137FeatureProtocol.MatchesChannel(0x248A, 0x5B2F, 0xFF00, 1, 0, 0, 64),
            "different feature length rejected");
        Check(!M137FeatureProtocol.MatchesChannel(0x248A, 0x5B2F, 0xFF01, 1, 0, 0, 33),
            "different usage page rejected");
        Check(!M137FeatureProtocol.MatchesChannel(0x248A, 0x5B2F, 0xFF00, 2, 0, 0, 33),
            "different usage rejected");

        // 请求帧：12 01 20 00 seq
        var request = M137FeatureProtocol.Request(7);
        Check(request.Length == 33 && request[0] == 12 && request[1] == 1 && request[2] == 32 &&
              request[3] == 0 && request[4] == 7, "official GetInfo header");
        Check(request.Skip(5).All(x => x == 0), "GetInfo request carries no payload");
        Check(M137FeatureProtocol.IsRequest(request, 7), "request shape accepted");
        Check(!M137FeatureProtocol.IsRequest(request, 8), "sequence must match");
        var tampered = (byte[])request.Clone();
        tampered[20] = 1;
        Check(!M137FeatureProtocol.IsRequest(tampered, 7), "payload bytes rejected");

        byte[] Frame(string identity, int level)
        {
            var frame = new byte[33];
            frame[0] = 12; frame[1] = 1; frame[2] = 32; frame[3] = 0;
            Encoding.ASCII.GetBytes(identity).CopyTo(frame, 8);
            frame[18] = (byte)level;
            return frame;
        }

        // 身份与电量
        Check(M137FeatureProtocol.TryIdentity(Frame("M137", 82), out var identity) && identity == "M137",
            "M137 identity parsed from bytes 8..11");
        Check(M137FeatureProtocol.TryPercent(Frame("M137", 82), out var level, out _) && level == 82,
            "percent read from byte 18");
        Check(!M137FeatureProtocol.TryIdentity(new byte[33], out _), "empty echo is not an identity");
        Check(!M137FeatureProtocol.TryIdentity(Frame("X137", 50), out _), "non-M prefix rejected");
        Check(!M137FeatureProtocol.TryIdentity(Frame("M13X", 50), out _), "non-digit suffix rejected");
        Check(!M137FeatureProtocol.TryPercent(Frame("M137", 101), out _, out _), "percent above 100 rejected");
        Check(M137FeatureProtocol.TryPercent(Frame("M620", 0), out var zero, out _) && zero == 0,
            "zero percent is a real reading");

        // 身份到型号
        Check(M137FeatureProtocol.ModelForIdentity("M137") == "狼蛛 SC580", "M137 maps to AULA SC580");
        Check(M137FeatureProtocol.ModelForIdentity("M139") == "黑爵 AJ139 Pro", "M139 maps to AJ139 Pro");
        Check(M137FeatureProtocol.ModelForIdentity("M620") == "黑爵 AJ159", "M620 maps to AJ159");
        Check(M137FeatureProtocol.ModelForIdentity("M184") == "黑爵 AJ179", "M184 maps to AJ179");
        Check(M137FeatureProtocol.ModelForIdentity("M999") == null, "unknown identity is not guessed");
        Check(M137FeatureProtocol.DisplayForIdentity("M999").Contains("M999"), "unknown identity stays visible");

        // 路由与显示
        var route = DeviceRoutes.Find(0x248A, 0x5B2F, "Wireless-Receiver");
        Check(route != null && route.Reader == "M137" && route.Kind == DeviceKind.Mouse &&
              route.Transport == "2.4G", "route table sends 248A:5B2F to M137 as a 2.4G mouse");

        var bucket = new HidDiscovery.Bucket
        {
            Vid = 0x248A, Pid = 0x5B2F, Product = "Wireless-Receiver",
            Kind = DeviceKind.Mouse, HandledBy = HandledBy.M137,
            M137Model = "狼蛛 SC580", Battery = 82
        };
        var device = bucket.ToDevice();
        Check(device != null && device.Name == "狼蛛 SC580" && device.Kind == DeviceKind.Mouse &&
              device.Transport == "2.4G" && device.ConfirmedModel == "狼蛛 SC580",
            "M137 handling produces the confirmed model");
        Check(!device!.PeripheralConfirmed && device.ReceiverDiscovered,
            "receiver battery is not peripheral confirmation");

        var unknown = new HidDiscovery.Bucket
        {
            Vid = 0x248A, Pid = 0x5B2F, Product = "Wireless-Receiver",
            Kind = DeviceKind.Mouse, HandledBy = HandledBy.M137
        }.ToDevice();
        Check(unknown != null && unknown.Name == "248A:5B2F 接收器" && unknown.ConfirmedModel == "",
            "no identity keeps the receiver unnamed");

        return count;
    }
}
