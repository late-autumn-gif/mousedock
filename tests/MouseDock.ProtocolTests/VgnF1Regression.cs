using System;
using MouseDock.Models;
using MouseDock.Services.Detection;

/// <summary>
/// VGN 蜻蜓 F1 MOBA（3554:F503）协议回归。
/// 依据是用户提供的 1.1.1+bac923f 构建里 VgnF1Protocol 的反编译结果，
/// 该构建已在真机上跑通这款鼠标。这里锁定帧格式与判据，防止后续改动走样。
/// </summary>
internal static class VgnF1Regression
{
    internal static int Run()
    {
        int n = 0;
        Check(VgnF1Protocol.IsCandidate(0x3554, 0xF503, "VGN Mouse 2.4G Receiver"), "Exact receiver product admits"); n++;
        Check(!VgnF1Protocol.IsCandidate(0x3554, 0xF503, "Wireless-Receiver"), "Generic product string rejected"); n++;
        Check(!VgnF1Protocol.IsCandidate(0x3554, 0xFA09, "VGN Mouse 2.4G Receiver"), "F75 receiver pid rejected"); n++;
        Check(!VgnF1Protocol.IsCandidate(0x391D, 0xF503, "VGN Mouse 2.4G Receiver"), "Foreign vid rejected"); n++;

        Check(VgnF1Protocol.MatchesSignature(0xFF02, 2, 17, 17, 0), "FF02/0002 17/17 is the channel"); n++;
        Check(!VgnF1Protocol.MatchesSignature(0xFF02, 2, 20, 20, 0), "20-byte channel rejected"); n++;
        Check(!VgnF1Protocol.MatchesSignature(0xFF02, 2, 17, 17, 8), "Feature channel rejected"); n++;

        foreach (string id in new[] { "2,5", "2,6", "2,33" })
        {
            Check(VgnF1Protocol.IsMobaIdentity(id), "MOBA identity " + id); n++;
        }
        foreach (string id in new[] { "2,7", "1,5", "", null, "2,34" })
        {
            Check(!VgnF1Protocol.IsMobaIdentity(id), "Non-MOBA identity rejected: " + (id ?? "null")); n++;
        }

        var identity = Invoke("IdentityCommand", null) as byte[];
        Check(identity != null && identity.Length == 17, "Identity frame is 17 bytes"); n++;
        Check(identity[0] == 8 && identity[1] == 1 && identity[5] == 8, "Identity header 08/01/../08"); n++;
        Check(Total(identity) == 85, "Identity checksum is 55 - sum"); n++;

        var online = Invoke("Command", 3) as byte[];
        Check(online[0] == 8 && online[1] == 3, "Online command frame"); n++;
        Check(Total(online) == 85, "Online checksum is 55 - sum"); n++;
        var battery = Invoke("Command", 4) as byte[];
        Check(battery[1] == 4, "Battery command frame"); n++;
        Check(Total(battery) == 85, "Battery checksum is 55 - sum"); n++;

        var reply = new byte[17];
        reply[0] = 8; reply[1] = 4; reply[6] = 82; reply[7] = 1;
        reply[16] = (byte)(85 - Sum(reply));
        Check(Valid(reply, battery), "Valid battery reply accepted"); n++;
        var wrongCommand = (byte[])reply.Clone();
        wrongCommand[1] = 3; wrongCommand[16] = (byte)(85 - Sum(wrongCommand));
        Check(!Valid(wrongCommand, battery), "Wrong command reply rejected"); n++;
        var badChecksum = (byte[])reply.Clone();
        badChecksum[6] = 50;
        Check(!Valid(badChecksum, battery), "Broken checksum rejected"); n++;
        Check(!Valid((byte[])battery.Clone(), battery), "Echo of the request rejected"); n++;

        Check(VgnF1Protocol.Model == "VGN F1 MOBA", "Display name is VGN F1 MOBA"); n++;
        Check(DeviceRoutes.IsKnown(0x3554, 0xF503, "VGN Mouse 2.4G Receiver"), "Policy admits VGN F1 MOBA"); n++;
        Check(DeviceRoutes.Find(0x3554, 0xF503, "VGN Mouse 2.4G Receiver")?.Reader == "VgnF1", "Routes to VgnF1 reader"); n++;
        Check(DeviceRoutes.Find(0x3554, 0xF503, "VGN Mouse 2.4G Receiver")?.Kind == DeviceKind.Mouse,
            "VGN F1 MOBA is a mouse"); n++;
        return n;
    }

    private static object Invoke(string name, object arg)
    {
        var method = typeof(VgnF1Protocol).GetMethod(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return method.Invoke(null, arg == null ? null : new object[] { (byte)(int)arg });
    }

    private static bool Valid(byte[] reply, byte[] request)
    {
        return VgnF1Protocol.ValidReply(reply, request);
    }

    private static int Sum(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < 16; i++) sum += frame[i];
        return sum & 0xFF;
    }

    private static int Total(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < frame.Length; i++) sum += frame[i];
        return sum & 0xFF;
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new InvalidOperationException("VGN F1: " + what);
    }
}



