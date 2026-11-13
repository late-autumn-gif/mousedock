using System;
using System.Buffers.Binary;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private sealed class Zg4BatteryInput
    {
        private readonly byte[] buffer = new byte[1024];
        private int expected, count, nextIndex;
        private long lastPacket;

        internal int Read(ReadOnlySpan<byte> report, byte reportId, byte sequence, long now,
            out bool charging, out byte[] replay)
        {
            charging = false;
            replay = null;
            if (report.Length is < 2 or > 65 || report[0] != reportId) return -1;
            if (now - lastPacket > 2000) Reset();
            var frame = report.Slice(1);
            if (frame[0] is 0xEE or 0xEA)
            {
                if (frame.Length < 6 || frame[2] != 0) { Reset(); return -1; }
                int index = frame[1] & 0x7F;
                int total = BinaryPrimitives.ReadUInt16LittleEndian(frame.Slice(3));
                if (index == 0)
                {
                    Reset();
                    if (total is < 1 or > 1024) return -1;
                    expected = total;
                }
                else if (expected == 0 || index != nextIndex || total != 0)
                { Reset(); return -1; }
                int size = Math.Min(58, Math.Min(expected - count, frame.Length - 5));
                frame.Slice(5, size).CopyTo(buffer.AsSpan(count));
                count += size;
                nextIndex = index + 1;
                lastPacket = now;
                if ((frame[1] & 0x80) == 0 && count < expected) return -1;
                if (count != expected) { Reset(); return -1; }
                frame = buffer.AsSpan(0, count);
                Reset();
            }

            // A property batch can yield ACKs and several FF frames in one transfer.
            for (int offset = 0; offset + 4 <= frame.Length;)
            {
                if (frame[offset] != 0xFF) { offset++; continue; }
                int length = frame[offset + 2];
                int end = offset + length + 4;
                if (length < 1 || end > frame.Length) break;
                if (frame[end - 1] != 0xAA) { offset++; continue; }
                var payload = frame.Slice(offset + 3, length);
                int batteryOffset = payload[0] == Zg4BatteryProperty && length >= 4 ? 1 :
                    payload[0] == 0xFE && length >= 5 && payload[1] == Zg4BatteryProperty ? 2 : -1;
                if (frame[offset + 1] == sequence && batteryOffset >= 0)
                {
                    byte value = payload[batteryOffset];
                    int level = value & 0x7F;
                    if (level <= 100)
                    {
                        charging = (value & 0x80) != 0;
                        // Keep only the battery frame, excluding other properties and HID padding.
                        if (length == batteryOffset + 3)
                        {
                            replay = new byte[length + 5];
                            replay[0] = reportId;
                            frame.Slice(offset, length + 4).CopyTo(replay.AsSpan(1));
                        }
                        return level;
                    }
                }
                offset = end;
            }
            return -1;
        }

        private void Reset() => expected = count = nextIndex = 0;
    }
}
