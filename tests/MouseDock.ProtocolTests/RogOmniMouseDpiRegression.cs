using System.Buffers.Binary;
using MouseDock.Services.Detection;

internal static class RogOmniMouseDpiRegression
{
    internal static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("ROG Keris II Ace DPI: " + reason);
            assertions++;
        }

        var link = new Link();
        var state = link.Run();
        Check(state is { Profile: 2, Index: 1, Count: 3, Writable: true } &&
            state.Values.SequenceEqual(new[] { 400, 800, 42000 }), "official profile count, 1-based stage and 50 DPI encoding");
        Check(link.Run(new(state!, 2)) is { Index: 2 } && link.Selected == 3, "selects the corresponding hardware stage and confirms");
        Check(link.Run(new(state!, 2)) != null && link.Writes == 1, "same stage does not write");
        Check(new Link { Stage = 0 }.Run() == null, "invalid current stage");
        Check(new Link { Count = 5 }.Run() == null, "invalid preset count");
        Check(new Link { WrongIndex = true }.Run() == null, "wrong command index");
        Check(new Link { Short = true }.Run() == null, "truncated report");
        Check(new Link { BadDpi = true }.Run() == null, "DPI outside the sensor range");
        Check(new Link { SwitchProfile = true }.Run() == null, "profile changes while reading the table");
        var split = new Link { Split = true };
        Check(split.Run() is { SeparateAxes: true, Writable: false } && split.Run(new(state!, 0)) == null && split.Writes == 0,
            "X/Y profile stays read-only");
        var wrong = new Link { LostPairingAt = 1 };
        Check(wrong.Run(new(state!, 0)) == null && wrong.Writes == 0, "pairing change prevents selection");
        Check(new Link { LostPairingAt = 2 }.Run(new(state!, 0)) == null, "pairing lost after selection");
        Check(new Link { IgnoreWrite = true }.Run(new(state!, 0)) == null, "write transport success without active-stage change");
        Check(new Link { ChangeTable = true }.Run(new(state!, 0)) == null, "a table change invalidates write confirmation");
        return assertions;
    }

    private sealed class Link
    {
        internal byte Count = 3, Stage = 2, Selected;
        internal int Writes, LostPairingAt = int.MaxValue;
        internal bool WrongIndex, Short, BadDpi, SwitchProfile, Split, IgnoreWrite, ChangeTable;
        private int profiles, identities;

        internal MouseDpiState? Run(MouseDpiChange? change = null) => RogOmniMouseDpi.ReadOrChange(change, Query, Select,
            () => ++identities < LostPairingAt);

        private byte[] Query(byte command, ushort index)
        {
            var response = new byte[64];
            response[0] = 3;
            response[1] = 0x12;
            response[2] = command;
            response[3] = (byte)index;
            if (command == 0)
            {
                response[11] = SwitchProfile && ++profiles > 1 ? (byte)3 : (byte)2;
                response[12] = Stage;
                response[19] = Count;
            }
            else
            {
                if (WrongIndex) response[3] = 1;
                ushort[] enc = [(ushort)(ChangeTable && Writes > 0 ? 9 : 7), 15, (ushort)(BadDpi ? 840 : 839)];
                for (int i = 0; i < enc.Length; i++)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(5 + 4 * i), enc[i]);
                    BinaryPrimitives.WriteUInt16LittleEndian(response.AsSpan(7 + 4 * i), (ushort)(Split && i == 0 ? enc[i] + 1 : enc[i]));
                }
            }
            return Short ? response[..20] : response;
        }

        private bool Select(byte stage)
        {
            Writes++;
            Selected = stage;
            if (!IgnoreWrite) Stage = stage;
            return true;
        }
    }
}
