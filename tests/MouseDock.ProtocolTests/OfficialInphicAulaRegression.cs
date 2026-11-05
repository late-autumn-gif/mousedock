using MouseDock.Services.Detection;
internal static class OfficialInphicAulaRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
        foreach (string model in new[] { "M909", "M910", "IN90", "M900" })
        {
            var legacy = new byte[32]; legacy[0] = 0x10; legacy[2] = 1;
            System.Text.Encoding.ASCII.GetBytes(model).CopyTo(legacy, 4);
            legacy[12] = 1; legacy[13] = 67; legacy[14] = 1;
            Check(VendorProtocol.TryParseInphicLegacy(legacy, out int level, out bool charging, out string actual) &&
                level == 67 && charging && actual == model, "IN9 official model and battery fields");
            Check(VendorProtocol.TryParseInphicLegacy(new byte[] { 0 }.Concat(legacy).ToArray(), out level, out _, out _) && level == 67,
                "IN9 Windows report ID alignment");
            legacy[13] = 0;
            Check(VendorProtocol.TryParseInphicLegacy(legacy, out level, out _, out _) && level == 0, "IN9 genuine zero");
            legacy[14] = 0;
            Check(!VendorProtocol.TryParseInphicLegacy(legacy, out _, out _, out _), "IN9 offline cache is not fresh");
        }
        Check(!VendorProtocol.TryParseInphicLegacy(new byte[33], out _, out _, out _), "IN9 empty rejected");
        Check(VendorProtocol.IsAulaSc630("AULA-SC630Pro Gaming Mouse") && !VendorProtocol.IsAulaSc630("SC630"), "SC630 exact product gate");
        var request = VendorProtocol.BuildAulaSc630BatteryRequest(12);
        Check(request.Length == 41 && request[0] == 6 && request[3] == 0x81 && request[4] == 12 && request[8] == 3,
            "SC630 official Feature request");
        Check(!VendorProtocol.TryParseAulaSc630Battery(request, 12, out _, out _), "SC630 request echo rejected");
        request[9] = 1; request[10] = 0x80 | 72;
        Check(VendorProtocol.TryParseAulaSc630Battery(request, 12, out int scLevel, out bool scCharge) && scLevel == 72 && scCharge,
            "SC630 official packed percent and charging");
        Check(!VendorProtocol.TryParseAulaSc630Battery(request, 13, out _, out _), "SC630 stale sequence rejected");
        request[10] = 0;
        Check(VendorProtocol.TryParseAulaSc630Battery(request, 12, out scLevel, out _) && scLevel == 0, "SC630 zero percent");
        request[10] = 101;
        Check(!VendorProtocol.TryParseAulaSc630Battery(request, 12, out _, out _), "SC630 invalid percent rejected");
        var empty = new byte[520]; empty[0] = empty[2] = 9;
        Check(!VendorProtocol.TryParseAulaF99Battery(empty, out _), "F99 near-empty response rejected");
        return checks;
    }
}
