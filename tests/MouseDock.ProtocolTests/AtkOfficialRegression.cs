using MouseDock.Services.Detection;

internal static class AtkOfficialRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        // Official BitMouse receiver target is 1 even for identity command 0x89.
        var idRequest = VendorProtocol.BuildAtkZeroRequest(0x89, true);
        Check(idRequest.Take(8).SequenceEqual(new byte[] { 8, 10, 0x72, 8, 0, 1, 0x89, 6 }), "Official ZERO receiver identity routing");
        var zero = new byte[64]; zero[0] = 8; zero[5] = 7; zero[7] = 0;
        Check(VendorProtocol.TryParseAtkZeroBattery(zero, out int pct) && pct == 0, "ZERO native report ID alignment and real zero");
        zero[0] = 0;
        Check(!VendorProtocol.TryParseAtkZeroBattery(zero, out _), "Wrong native report ID is not ZERO");
        zero[0] = 8; zero[5] = 0; zero[4] = 7; zero[6] = 80;
        Check(!VendorProtocol.TryParseAtkZeroBattery(zero, out _), "WebHID offset must not be reapplied to Windows reports");
        Check(VendorProtocol.IsAtkV75X(0x320F, 0x5088, "ATK V75X 2.4G Dongle"), "Official V75X product name");
        Check(VendorProtocol.IsAtkV75X(0x320F, 0x5055, "ATK V75X"), "Official V75X wired port");
        Check(!VendorProtocol.IsAtkV75X(0x320F, 0x5088, "Wireless Gaming Keyboard"), "Shared PID is not model identity");
        Check(!VendorProtocol.IsAtkV75X(0x1234, 0x5088, "ATK V75X"), "Vendor must match");
        foreach (int size in new[] { 32, 64 })
        {
            var request = VendorProtocol.BuildAtkV75XBatteryRequest(0x1234, size);
            Check(request.Take(8).SequenceEqual(new byte[] { 4, 0x34, 0x12, 0x1A, 0, 0, 0, 0 }), "Official EVISION nonce and zero payload length");
            Check(!VendorProtocol.TryParseAtkV75XBattery(request, request, out _, out _), "EVISION request echo rejected");
            var response = (byte[])request.Clone(); response[8] = 0; response[9] = 2;
            Check(VendorProtocol.TryParseAtkV75XBattery(response, request, out pct, out bool charging) && pct == 0 && charging, "EVISION real zero and official charging enum 2");
            response[8] = 99; response[9] = 1;
            Check(VendorProtocol.TryParseAtkV75XBattery(response, request, out pct, out charging) && pct == 99 && !charging, "EVISION enum 1 is not charging");
            response[1]++;
            Check(!VendorProtocol.TryParseAtkV75XBattery(response, request, out _, out _), "Unrelated EVISION transaction rejected");
            response[1]--; response[3] = 0xFF;
            Check(!VendorProtocol.TryParseAtkV75XBattery(response, request, out _, out _), "EVISION error rejected");
        }
        var rainy = VendorProtocol.BuildRainy75BatteryRequest();
        Check(!VendorProtocol.TryParseRainy75OfficialBattery(rainy, out _, out _), "Rainy native query echo rejected");
        rainy[8] = 0; rainy[9] = 1;
        Check(VendorProtocol.TryParseRainy75OfficialBattery(rainy, out pct, out bool rainyCharging) && pct == 0 && rainyCharging, "Rainy official charge state is 1");
        rainy[7] = 0xFE;
        Check(!VendorProtocol.TryParseRainy75OfficialBattery(rainy, out _, out _), "Rainy native FE error rejected");
        rainy[7] = 0xFF;
        Check(!VendorProtocol.TryParseRainy75OfficialBattery(rainy, out _, out _), "Rainy native FF error rejected");
        rainy[7] = 0; rainy[5] = 1;
        Check(!VendorProtocol.TryParseRainy75OfficialBattery(rainy, out _, out _), "Rainy unrelated configuration offset rejected");
        return checks;
    }
}
