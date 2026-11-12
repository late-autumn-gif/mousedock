using System;

namespace MouseDock.Services.Detection;

internal static class AulaSc580Dpi
{
    internal static byte[] Request()
    {
        var report = new byte[33];
        report[1] = 0x13;
        report[3] = 1;
        return report;
    }

    internal static MouseDpiState Read(ReferenceBatteryProtocol.ILink link, ReferenceBatteryProtocol.Reading identity)
    {
        if (identity.Level < 0 || identity.Identity != "M137") return null;
        byte[] Exchange(byte[] request, Func<byte[], bool> match)
        {
            for (int i = 0; i < 32; i++)
            {
                if (link.Read(1) is { Length: > 0 }) continue;
                if (!link.Write(request, 150)) return null;
                long deadline = link.Now + 400;
                while (link.Now < deadline)
                {
                    var frame = link.Read((int)Math.Min(75, deadline - link.Now));
                    if (frame is { Length: 33 } && frame[0] == 0 && match(frame)) return frame;
                }
                break;
            }
            return null;
        }

        var response = Exchange(Request(), frame => frame[1] == 0x13 && frame[2] == 0 && frame[3] == 1 && frame[4] == 25);
        if (response == null) return null;
        byte count = (byte)(response[5] & 0x0F), index = (byte)(response[5] >> 4);
        if (count is < 1 or > 6 || index >= count) return null;
        var values = new int[count];
        for (int i = 0; i < count; i++)
        {
            int code = response[6 + i];
            // Both official M137 sensor tables share this range. Higher codes
            // require a separately confirmed sensor variant.
            if (code is < 1 or > 220) return null;
            values[i] = code <= 200 ? code * 50 : 10000 + (code - 200) * 100;
        }
        var confirmed = Exchange(AulaBatteryProtocol.IdentifyRequest(), frame =>
            AulaBatteryProtocol.TryDecodeIdentify(frame, out var current) && current.Identity == identity.Identity && current.Level >= 0);
        return confirmed == null ? null : new(0, index, count, false, values, MouseDpiProtocol.AulaSc580, "M137", Writable: false);
    }
}
