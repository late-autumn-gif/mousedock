using MouseDock.Services.Detection;

internal static class MchoseRapooRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        Check(VendorProtocol.IsMchoseV9Pro(0x291D, 0x385D, "MCHOSE V9 PRO"), "V9 exact official identity");
        Check(!VendorProtocol.IsMchoseV9Pro(0x291D, 0x385D, "MCHOSE G9 PRO"), "Shared audio PID requires model name");
        Check(!VendorProtocol.IsMchoseV9Pro(0x291D, 0x385D, "MCHOSE V9"), "Non-Pro V9 is a different SKU");
        var v9 = VendorProtocol.BuildMchoseV9BatteryRequest();
        Check(v9.Length == 64 && v9[0] == 0x55 && v9[1] == 0x65 && v9[2] == 1, "Official BLK query");
        Check(!VendorProtocol.TryParseMchoseV9Battery(v9, out _, out _), "Request echo is not V9 battery");
        v9[2] = 0; v9[3] = 2;
        Check(VendorProtocol.TryParseMchoseV9Battery(v9, out int level, out bool charging) && level == 0 && !charging, "V9 real zero");
        v9[2] = 50; v9[3] = 3;
        Check(VendorProtocol.TryParseMchoseV9Battery(v9, out level, out charging) && level == 50 && charging, "V9 charging status3");
        v9[3] = 26;
        Check(!VendorProtocol.TryParseMchoseV9Battery(v9, out _, out _), "V9 sleep is not fresh");
        v9[2] = 101; v9[3] = 2;
        Check(!VendorProtocol.TryParseMchoseV9Battery(v9, out _, out _), "V9 range");
        Check(VendorProtocol.IsMchoseG7(0x3837, 0x1018, "MCHOSE G7") &&
              VendorProtocol.IsMchoseG7(0xA8A4, 0x2255, "MCHOSE G7 Pro") &&
              !VendorProtocol.IsMchoseG7(0x3837, 0x1018, "MCHOSE G7 MAX") &&
              !VendorProtocol.IsMchoseG7(0x1234, 0x1018, "MCHOSE G7"), "G7 exact model/vendor");
        var g7 = VendorProtocol.BuildMchoseG7BatteryRequest();
        Check(g7.Length == 65 && g7[1] == 0x55 && g7[2] == 0x30 && g7[5] == 0x2E, "Official G7 query");
        var g7Raw = VendorProtocol.BuildMchoseG7BatteryRequest(64);
        Check(g7Raw.Length == 64 && g7Raw[0] == 0x55 && g7Raw[1] == 0x30 && g7Raw[4] == 0x2E, "G7 unnumbered query");
        Check(!VendorProtocol.TryParseMchoseG7Battery(g7, out _, out _), "G7 request echo");
        var g7Reply = new byte[65]; g7Reply[0] = 0xAA; g7Reply[1] = 0x30; g7Reply[8] = 62; g7Reply[9] = 1;
        Check(VendorProtocol.TryParseMchoseG7Battery(g7Reply, out level, out charging) && level == 62 && charging, "G7 65-byte unnumbered reply");
        g7Reply[1] = 0xFA;
        Check(!VendorProtocol.TryParseMchoseG7Battery(g7Reply, out _, out _), "G7 status notification is not battery");
        var g7Id = new byte[65]; g7Id[0] = 0; g7Id[1] = 0xAA; g7Id[2] = 0x30; g7Id[9] = 62; g7Id[10] = 1;
        Check(VendorProtocol.TryParseMchoseG7Battery(g7Id, out level, out charging) && level == 62 && charging, "G7 Windows report-ID reply");
        var g7RawReply = new byte[64]; g7RawReply[0] = 0xAA; g7RawReply[1] = 0x30; g7RawReply[8] = 62; g7RawReply[9] = 1;
        Check(VendorProtocol.TryParseMchoseG7Battery(g7RawReply, out level, out charging) && level == 62 && charging, "G7 64-byte unnumbered reply");
        Check(VendorProtocol.IsRapooVt7(0x24AE, 0x1413) && !VendorProtocol.IsRapooVt7(0x24AE, 0x1416), "VT7 exact receiver pair");
        var vt7 = new byte[19]; vt7[0] = 7; vt7[7] = 1; vt7[8] = 0;
        Check(VendorProtocol.TryParseRapooVt7Battery(vt7, out level, out charging) && level == 0 && !charging, "VT7 report7 includes real zero");
        vt7[7] = 2; vt7[8] = 80;
        Check(VendorProtocol.TryParseRapooVt7Battery(vt7, out level, out charging) && level == 80 && charging, "VT7 battery fields");
        vt7[7] = 0;
        Check(!VendorProtocol.TryParseRapooVt7Battery(vt7, out _, out _), "VT7 disconnected is not zero battery");
        vt7[7] = 1; vt7[8] = 101;
        Check(!VendorProtocol.TryParseRapooVt7Battery(vt7, out _, out _), "VT7 range");
        return checks;
    }
}
