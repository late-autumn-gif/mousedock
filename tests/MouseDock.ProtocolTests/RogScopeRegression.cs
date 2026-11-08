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
        paired[9] = paired[10] = 0;
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
        trace.Add("omni-paired-after", true, raw: paired, keep: true);
        var query = new BatteryReplay.Query("receiver-1", 2, "RogScopeRxOmni", "Charging", null, null, trace.Snapshot());
        var battery = new BatteryReplay.Collection("receiver-1", 2, 0x0B05, 0x1ACE, "ROG OMNI RECEIVER", 0xFF00, 1, 64, 64, 0, false, true, 2);
        var pairing = battery with { Interface = 1, OmniReport = 1, Page = 0xFF02 };
        Check(BatteryReplay.Replay(query, new[] { pairing, battery }) is { Percent: 73, Charging: true },
            "Replay requires the paired keyboard and both collections");
        Check(BatteryReplay.Replay(query, new[] { pairing with { Receiver = "receiver-2" }, battery }).Percent == null,
            "Never borrow pairing data from another receiver");
        var changed = paired.ToArray();
        changed[9] = 0x63;
        changed[10] = 0x1B;
        Check(!VendorProtocol.RogOmniPairingUnchanged(paired, changed), "Reject changes in paired models");
        var changedSamples = trace.Snapshot();
        changedSamples[^1] = changedSamples[^1] with { Hex = Convert.ToHexString(changed) };
        Check(BatteryReplay.Replay(query with { Samples = changedSamples }, new[] { pairing, battery }).Percent == null,
            "A changed pairing list invalidates the readout");
        paired[9] = 0x7A;
        paired[10] = 0x1B;
        Check(!VendorProtocol.RogOmniHasScopeRx(paired), "Do not accept duplicate target slots");
        reply[6] = 101;
        Check(!VendorProtocol.TryParseRogScopeBattery(reply, out _, out _), "Reject out-of-range percent");
        reply[6] = 0;
        Check(VendorProtocol.TryParseRogScopeBattery(reply, out percent, out _) && percent == 0, "Accept genuine zero while charging");
        reply[9] = 0;
        Check(!VendorProtocol.TryParseRogScopeBattery(reply, out _, out _), "Reject zero-padded query echo");
        reply[2] = 7;
        Check(!VendorProtocol.TryParseRogScopeBattery(reply, out _, out _), "Reject the mouse battery command");

        // 本机 0B05:1ACE 实测：配对表唯一型号 0x1C25，电量应答 02 12 01 → [6]=53%。
        // 型号依据 ASUS 官方 Armoury Crate 的 AZOTH-X 插件设备表（D:\ROG\AZOTH-X\index.html
        // 的 loaderData 列出设备 ID 7205 = 0x1C25），本机键盘是 ROG 夜魔 X。
        var rx2 = new byte[64];
        rx2[0] = 1; rx2[1] = 0xA0;
        rx2[5] = 0x25; rx2[6] = 0x1C;
        rx2[9] = 0x02; rx2[10] = 0x04;
        Check(VendorProtocol.RogOmniHasScopeRx(rx2), "Omni receiver with paired 1C25 is a known ROG keyboard");
        Check(VendorProtocol.RogPairedModel(rx2, out var rx2Pids) == VendorProtocol.RogAzothX &&
            rx2Pids.Count == 2 && rx2Pids[0] == 0x1C25, "1C25 maps to ROG 夜魔 X, not a Scope RX SKU");
        Check(VendorProtocol.RogAzothX == "ROG 夜魔 X", "Azoth X display name");
        var unknownPairing = (byte[])rx2.Clone();
        unknownPairing[5] = 0x5F; unknownPairing[6] = 0x1B;
        Check(!VendorProtocol.RogOmniHasScopeRx(unknownPairing), "Unknown paired model must not be read");
        var duplicateRx2 = (byte[])rx2.Clone();
        duplicateRx2[9] = 0x25; duplicateRx2[10] = 0x1C;
        Check(!VendorProtocol.RogOmniHasScopeRx(duplicateRx2), "Duplicate RX2 slots must not be accepted");
        Check(VendorProtocol.RogOmniPairingUnchanged(rx2, (byte[])rx2.Clone()), "1C25 pairing list compares by content");
        var measuredBattery = new byte[64];
        new byte[] { 2, 0x12, 1, 0, 0, 0, 0x35, 0x02, 0x02, 0, 0x14, 0x35, 0xDC, 0x0E }.CopyTo(measuredBattery, 0);
        Check(VendorProtocol.TryParseRogScopeBattery(measuredBattery, out percent, out charging) &&
            percent == 53 && !charging, "Measured 1C25 battery reply decodes to 53%");
        return count;
    }
}
