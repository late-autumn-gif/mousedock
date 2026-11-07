using MouseDock.Services.Detection;

internal static class RogScopeRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string reason)
        {
            if (!condition) throw new Exception("ROG Scope RX: " + reason);
            count++;
        }

        Check(VendorProtocol.RogScopeName("ROG STRIX SCOPE II RX") == VendorProtocol.RogScopeRxWired &&
            VendorProtocol.RogScopeName("ROG STRIX SCOPE II 96 RX WIRELESS") == VendorProtocol.RogScopeRxWireless &&
            VendorProtocol.RogScopeName("ROG STRIX SCOPE II 96 WIRELESS") == null, "Match only the RX models");
        var paired = new byte[64];
        paired[0] = 1;
        paired[1] = 0xA0;
        paired[5] = 0x63;
        paired[6] = 0x1B;
        paired[9] = 0x7A;
        paired[10] = 0x1B;
        Check(VendorProtocol.RogOmniHasScopeRx(paired), "Keyboard can be in the second pairing slot");
        paired[5] = paired[6] = 0;
        Check(!VendorProtocol.RogOmniHasScopeRx(paired), "Do not search past the pairing terminator");
        paired[5] = 0x7A;
        paired[6] = 0x1B;
        var reply = new byte[64];
        reply[0] = 2;
        reply[1] = 0x12;
        reply[2] = 1;
        reply[6] = 73;
        reply[9] = 1;
        Check(VendorProtocol.TryParseRogScopeBattery(reply, out int percent, out bool charging) &&
            percent == 73 && charging, "Keyboard percent and charging fields");
        var trace = new BatteryReplay.Trace();
        trace.Add("omni-paired", true, raw: paired, keep: true);
        trace.Add("omni-battery", true, raw: reply, keep: true);
        var query = new BatteryReplay.Query("receiver-1", 2, "RogScopeRxOmni", "Charging", null, null, trace.Snapshot());
        var battery = new BatteryReplay.Collection("receiver-1", 2, 0x0B05, 0x1ACE, "ROG OMNI RECEIVER", 0xFF00, 1, 64, 64, 0, false, true, 2);
        var pairing = battery with { Interface = 1, OmniReport = 1, Page = 0xFF02 };
        Check(BatteryReplay.Replay(query, new[] { pairing, battery }) is { Percent: 73, Charging: true },
            "Replay requires the paired keyboard and both collections");
        Check(BatteryReplay.Replay(query, new[] { pairing with { Receiver = "receiver-2" }, battery }).Percent == null,
            "Never borrow pairing data from another receiver");
        reply[6] = 101;
        Check(!VendorProtocol.TryParseRogScopeBattery(reply, out _, out _), "Reject out-of-range percent");
        reply[6] = 0;
        Check(VendorProtocol.TryParseRogScopeBattery(reply, out percent, out _) && percent == 0, "Accept genuine zero while charging");
        reply[9] = 0;
        Check(!VendorProtocol.TryParseRogScopeBattery(reply, out _, out _), "Reject zero-padded query echo");
        reply[2] = 7;
        Check(!VendorProtocol.TryParseRogScopeBattery(reply, out _, out _), "Reject the mouse battery command");
        return count;
    }
}
