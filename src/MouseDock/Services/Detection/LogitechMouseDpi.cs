using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static class LogitechMouseDpi
{
    private static readonly int[] MenuValues = [400, 600, 800, 1000, 1200, 1600, 2000, 2400, 3200, 4000, 6400];

    // The caller correlates HID++ slot, feature, function and software ID before returning a reply.
    internal static MouseDpiState ReadOrChange(string model, MouseDpiChange change,
        Func<byte, byte, byte, byte, byte, byte[]> call)
    {
        var root = call(0, 0, 0x22, 0x01, 0);
        if (root == null || root[4] is 0 or 255) return null;
        byte feature = root[4];
        if (call(feature, 0, 0, 0, 0) is not { } sensors || sensors[4] != 1) return null;

        int Current()
        {
            var frame = call(feature, 2, 0, 0, 0);
            if (frame == null || frame[4] != 0) return -1;
            int dpi = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(5));
            return dpi is > 0 and < 0xE000 ? dpi : -1;
        }

        int current = Current();
        if (current < 0) return null;
        var onboard = call(0, 0, 0x81, 0, 0);
        int mode = onboard == null || onboard[4] == 255 ? -1 : onboard[4] == 0 ? 0 :
            call(onboard[4], 2, 0, 0, 0)?[4] ?? -1;
        int[] values = mode is 0 or 2 ? ReadMenuValues(call(feature, 1, 0, 0, 0), current) : null;
        if (values == null)
        {
            if (change != null) return null;
            return new(0, 0, 1, false, [current], MouseDpiProtocol.Hidpp, model, Writable: false);
        }

        MouseDpiState State(int value) => new((byte)mode, (byte)Array.IndexOf(values, value),
            (byte)values.Length, false, values, MouseDpiProtocol.Hidpp, model);
        if (change == null) return State(current);
        var expected = change.Expected;
        if (expected.Protocol != MouseDpiProtocol.Hidpp || expected.Identity != model ||
            expected.Profile != mode || !expected.Writable || !expected.Values.SequenceEqual(values) ||
            change.Index < 0 || change.Index >= values.Length) return null;
        int target = values[change.Index];
        if (target == current) return State(current);

        call(feature, 3, 0, (byte)(target >> 8), (byte)target);
        return Current() == target ? State(target) : null;
    }

    private static int[] ReadMenuValues(byte[] frame, int current)
    {
        if (frame?.Length != 20 || frame[4] != 0) return null;
        var values = new SortedSet<int>();
        int previous = 0, step = 0;
        bool currentAllowed = false;
        for (int offset = 5; offset + 1 < frame.Length; offset += 2)
        {
            int word = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(offset));
            if (word == 0) break;
            if (word >= 0xE000)
            {
                if (previous == 0 || step != 0 || word == 0xE000) return null;
                step = word & 0x1FFF;
                continue;
            }
            if (word <= previous) return null;
            values.Add(word);
            currentAllowed |= word == current;
            if (step != 0)
            {
                bool InRange(int dpi) => dpi > previous && dpi < word && (dpi - previous) % step == 0;
                foreach (int dpi in MenuValues)
                    if (InRange(dpi)) values.Add(dpi);
                currentAllowed |= InRange(current);
                step = 0;
            }
            previous = word;
        }
        if (step != 0 || !currentAllowed) return null;
        values.Add(current);
        return values.ToArray();
    }
}
