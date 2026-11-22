using System;
using System.Text.RegularExpressions;

namespace MouseDock.Services.Detection;

internal static partial class LogitechG403Battery
{
    internal const string Model = "Logitech G403";

    // G HUB 2026.7.1011216 g403.depot, battery.xml powermodel 046D:C082;
    // manifest binds the resource to g403_wireless and g403_wired.
    // Depot SHA256 BA6477631CA732A9A35C2D98B174B0181098D0FAA9EFC78D7C88E61E5BB2742D.
    // Energy normalization and interpolation are ours, not G HUB's private filtering.
    internal static bool IsName(string name) =>
        Regex.Replace(name ?? "", "[^A-Za-z0-9]", "").ToUpperInvariant() is
            "G403PRODIGYWIREDWIRELESSGAMINGMOUSE" or "G403PRODIGYWIRELESSGAMINGMOUSE" or
            "G403WIRELESSGAMINGMOUSE";

    internal static bool TryEstimate(int millivolts, byte flags, out int level,
        out bool charging, out bool full)
    {
        level = -1; charging = full = false;
        if (millivolts is < 2800 or > 4500 || (flags & 0x40) != 0 ||
            ((flags & 0x80) != 0 && (flags & 7) > 2)) return false;
        if ((flags & 0x87) == 0x81) { level = 100; full = true; return true; }
        bool active = (flags & 0x87) == 0x80;
        double value;
        if (active)
        {
            // G403's 4197.6/4197.9 mV plateau spans 87..100%. Both round to
            // 4198 mV, so voltage alone cannot recover the charging percentage.
            if (millivolts < Charge[0].Mv || millivolts >= 4198) return false;
            value = Interpolate(Charge, millivolts) / 100.0;
        }
        else
            value = 100 * (1 - Interpolate(Discharge, millivolts) / 295526.0);
        level = Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 100);
        charging = active;
        return true;
    }

    private static double Interpolate((int Mv, int Value)[] points, int millivolts)
    {
        if (millivolts <= points[0].Mv) return points[0].Value;
        if (millivolts >= points[^1].Mv) return points[^1].Value;
        int low = 0, high = points.Length - 1;
        while (high - low > 1)
        {
            int middle = (low + high) / 2;
            if (points[middle].Mv <= millivolts) low = middle;
            else high = middle;
        }
        var first = points[low]; var second = points[high];
        return first.Value + (second.Value - first.Value) *
            (double)(millivolts - first.Mv) / (second.Mv - first.Mv);
    }
}
