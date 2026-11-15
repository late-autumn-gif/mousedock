using static MouseDock.Services.Detection.KeyboardExpansionProtocol;

namespace MouseDock.Services.Detection;

internal static class KeyboardExpansionRazer
{
    private const byte Transaction = 0x9f;

    internal static byte[] Request() =>
        RazerBatteryFeature.Request(Transaction, RazerBatteryFeature.BatteryLevel);

    internal static ReferenceBatteryProtocol.Reading Decode(byte[] report)
    {
        Require(RazerBatteryFeature.TryRead(report, Transaction, RazerBatteryFeature.BatteryLevel, out byte scale) &&
            report[^1] == 0, "Invalid Razer keyboard response");
        return Value(-1) with { Diagnostic = $"device-scale={scale}/255; exact percentage unavailable" };
    }

    internal static ReferenceBatteryProtocol.Reading Read(KeyboardExpansionWire wire)
    {
        ReferenceBatteryProtocol.Reading value = null;
        for (int i = 0; i < 2; i++) value = Decode(wire.FeatureRead(Request(),100));
        return value;
    }
}
