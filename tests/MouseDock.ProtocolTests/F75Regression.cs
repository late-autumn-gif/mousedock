using System;
using MouseDock.Models;
using MouseDock.Services.Detection;

/// <summary>
/// 狼蛛 AULA F75（3554:FA09）协议回归。
/// 依据是用户提供的 1.1.1+bac923f 构建里 F75Protocol 的反编译结果。
/// 官方驱动界面对这个型号是 ShowPower=0，走的是固件留的 20 字节报告通道。
/// </summary>
internal static class F75Regression
{
    internal static int Run()
    {
        int n = 0;
        Check(F75Protocol.IsCandidate(0x3554, 0xFA09, "2.4G Wireless Receiver"), "Exact receiver product admits"); n++;
        Check(!F75Protocol.IsCandidate(0x3554, 0xFA09, "无线键盘 3554:FA09"), "Localized display string rejected"); n++;
        Check(!F75Protocol.IsCandidate(0x3554, 0xF503, "2.4G Wireless Receiver"), "VGN pid rejected"); n++;
        Check(!F75Protocol.IsCandidate(0x089D, 0x062F, "2.4G Wireless Receiver"), "Aula mouse vid rejected"); n++;

        Check(F75Protocol.MatchesSignature(0xFF02, 2, 20, 20, 0), "FF02/0002 20/20 is the channel"); n++;
        Check(!F75Protocol.MatchesSignature(0xFF02, 2, 17, 17, 0), "17-byte channel rejected"); n++;
        Check(!F75Protocol.MatchesSignature(0xFF01, 2, 20, 20, 0), "Other usage page rejected"); n++;

        var request = Request(5);
        Check(request.Length == 20 && request[0] == 19 && request[1] == 5 && request[2] == 1, "Identity request header"); n++;
        Check(request[19] == Sum(request), "Identity request checksum"); n++;
        var battery = Request(74);
        Check(battery[1] == 74 && battery[2] == 0, "Battery request header"); n++;
        Check(battery[19] == Sum(battery), "Battery request checksum"); n++;

        var identity = new byte[20];
        identity[0] = 19; identity[1] = 5; identity[2] = 1; identity[4] = 6;
        new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0xCD }.CopyTo(identity, 5);
        identity[19] = (byte)Sum(identity);
        Check(Valid(identity, 5, 6), "Identity reply accepted"); n++;
        Check(Convert.ToHexString(identity.AsSpan(5, 6)) == F75Protocol.Identity, "Identity bytes match the constant"); n++;

        var batteryReply = new byte[20];
        batteryReply[0] = 19; batteryReply[1] = 74; batteryReply[2] = 1; batteryReply[4] = 2; batteryReply[5] = 63;
        batteryReply[19] = (byte)Sum(batteryReply);
        Check(Valid(batteryReply, 74, 2), "Battery reply accepted"); n++;

        var wrongCommand = (byte[])batteryReply.Clone();
        wrongCommand[1] = 5; wrongCommand[19] = (byte)Sum(wrongCommand);
        Check(!Valid(wrongCommand, 74, 2), "Wrong command reply rejected"); n++;
        var brokenSum = (byte[])batteryReply.Clone();
        brokenSum[5] = 90;
        Check(!Valid(brokenSum, 74, 2), "Broken checksum rejected"); n++;
        var wrongDataLength = (byte[])batteryReply.Clone();
        wrongDataLength[4] = 3; wrongDataLength[19] = (byte)Sum(wrongDataLength);
        Check(!Valid(wrongDataLength, 74, 2), "Wrong data length rejected"); n++;

        Check(F75Protocol.Model == "AULA F75", "Display name is AULA F75"); n++;
        Check(DeviceRoutes.IsKnown(0x3554, 0xFA09, "2.4G Wireless Receiver"), "Policy admits AULA F75"); n++;
        Check(DeviceRoutes.Find(0x3554, 0xFA09, "2.4G Wireless Receiver")?.Reader == "AulaF75", "Routes to F75 reader"); n++;
        Check(DeviceRoutes.Find(0x3554, 0xFA09, "2.4G Wireless Receiver")?.Kind == DeviceKind.Keyboard,
            "AULA F75 is a keyboard"); n++;
        Check(!DeviceRoutes.IsKnown(0x3554, 0xFA09, "无线键盘 3554:FA09"), "Localized product string stays unclaimed"); n++;
        return n;
    }

    private static byte[] Request(byte command)
    {
        var method = typeof(F75Protocol).GetMethod("Request",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (byte[])method.Invoke(null, new object[] { (byte)command });
    }

    private static bool Valid(byte[] packet, byte command, int dataLength)
    {
        return F75Protocol.ValidReply(packet, command, dataLength);
    }

    private static int Sum(byte[] frame)
    {
        int sum = 0;
        for (int i = 0; i < 19; i++) sum += frame[i];
        return sum & 0xFF;
    }

    private static void Check(bool ok, string what)
    {
        if (!ok) throw new InvalidOperationException("F75: " + what);
    }
}


