using System;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static class AulaSc630Dpi
{
    private const byte ReadCommand = 0x86;
    private const byte WriteCommand = 0x06;
    private const byte ReportId = 6;
    private const int ReportLength = 41;
    private const int PayloadOffset = 9;
    private const int PayloadLength = 20;

    private sealed record Table(byte[] Payload, MouseDpiState State);

    private static byte[] Request(byte command, byte sequence)
    {
        var frame = new byte[ReportLength];
        frame[0] = ReportId;
        frame[3] = command;
        frame[4] = sequence;
        frame[8] = PayloadLength;
        return frame;
    }

    internal static bool IsRequest(ReadOnlySpan<byte> frame, bool allowWrite)
    {
        if (frame.Length != ReportLength || frame[0] != ReportId || frame[1] != 0 || frame[2] != 0 ||
            frame[4] == 0 || frame[5..8].IndexOfAnyExcept((byte)0) >= 0 || frame[8] != PayloadLength) return false;
        return frame[3] switch
        {
            ReadCommand => frame[PayloadOffset..].IndexOfAnyExcept((byte)0) < 0,
            WriteCommand => allowWrite && IsEnabled(frame[PayloadOffset], frame[PayloadOffset + 1]) &&
                frame[(PayloadOffset + PayloadLength)..].IndexOfAnyExcept((byte)0) < 0,
            _ => false
        };
    }

    private static bool IsEnabled(byte mask, int stage) => stage is >= 1 and <= 8 && (mask & (1 << (stage - 1))) != 0;

    private static Table Parse(byte[] frame, byte sequence)
    {
        if (frame is not { Length: ReportLength } || frame[0] != ReportId || frame[3] != ReadCommand ||
            (frame[4] != 0 && frame[4] != sequence) || frame.AsSpan(5, 3).IndexOfAnyExcept((byte)0) >= 0 ||
            frame[8] != PayloadLength || !IsEnabled(frame[PayloadOffset], frame[PayloadOffset + 1])) return null;

        var payload = frame.AsSpan(PayloadOffset, PayloadLength).ToArray();
        var stages = Enumerable.Range(1, 8).Where(stage => IsEnabled(payload[0], stage)).ToArray();
        // Keep the settings data in memory as a comparison token. The driver has
        // multiple sensor tables, so none of these codes are shown as numeric DPI.
        string settings = $"{payload[0]:X2}{Convert.ToHexString(payload.AsSpan(2))}";
        return new(payload, new(0, (byte)Array.IndexOf(stages, (int)payload[1]), (byte)stages.Length, false,
            Enumerable.Repeat(-1, stages.Length).ToArray(), MouseDpiProtocol.AulaSc630, settings, HardwareStages: stages));
    }

    internal static MouseDpiState ReadOrChange(ReferenceBatteryProtocol.ILink link, Func<byte> sequence,
        MouseDpiChange change, Func<bool> online)
    {
        Table Read()
        {
            byte seq = sequence();
            if (seq == 0 || !link.SetFeature(Request(ReadCommand, seq))) return null;
            link.Delay(130);
            return Parse(link.GetFeature(ReportId, ReportLength), seq);
        }

        Table ReadStable()
        {
            var first = Read();
            if (first == null) return null;
            var second = Read();
            return second != null && first.Payload.AsSpan().SequenceEqual(second.Payload) ? second : null;
        }

        var before = ReadStable();
        if (before == null || !online()) return null;
        if (change == null) return before.State;
        if (change.Expected.Protocol != MouseDpiProtocol.AulaSc630 || change.Expected.Identity != before.State.Identity ||
            change.Index < 0 || change.Index >= before.State.Count) return null;
        if (change.Index == before.State.Index) return before.State;

        byte writeSequence = sequence();
        if (writeSequence == 0) return null;
        var request = Request(WriteCommand, writeSequence);
        before.Payload.CopyTo(request, PayloadOffset);
        request[PayloadOffset + 1] = (byte)before.State.StageNumber(change.Index);
        if (!link.SetFeature(request)) return null;
        link.Delay(130);
        var after = ReadStable();
        return after != null && after.State.Index == change.Index && after.State.Identity == before.State.Identity && online()
            ? after.State : null;
    }
}
