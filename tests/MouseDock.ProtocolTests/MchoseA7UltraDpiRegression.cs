using System.Buffers.Binary;
using MouseDock.Services.Detection;

internal static class MchoseA7UltraDpiRegression
{
    internal static int Run()
    {
        int assertions = 0;
        void Check(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("A7 Ultra DPI: " + reason);
            assertions++;
        }

        var request = MchoseA7UltraDpi.ReadRequest();
        Check(request.Length == 65 && request[0] == 0x12 && request[1] == 0x98 && request.Skip(2).All(x => x == 0xFF),
            "official request uses complemented Feature payload");
        var link = new Link();
        var state = link.Run();
        Check(state is { Profile: 1, Index: 1, Count: 3, Writable: true } && state.Values.SequenceEqual(new[] { 800, 1600, 42000 }),
            "driver bit-field order distinguishes wired and wireless indexes and accepts 42000 DPI");
        Check(link.Run(new(state!, 2)) is { Index: 2 } && link.Writes == 1, "cutover and readback");
        var write = link.LastWrite!;
        Check(write.AsSpan(0, 7).SequenceEqual(new byte[] { 0x12, 0xBF, 0xFD, 0xFD, 0xFF, 0xDF, 0xFC }) &&
            write[17] == 0xFC && write[18] == 0 && write.AsSpan(5, 12).SequenceEqual(write.AsSpan(19, 12)) &&
            write.Skip(31).All(x => x == 0xFF), "selects both connection indexes and preserves the table");
        Check(link.Run(new(state!, 2)) != null && link.Writes == 1, "already selected needs no write");
        Check(new Link { Report = request }.Run() == null, "request echo");
        Check(new Link { Report = link.Report[..64] }.Run() == null, "wrong report length");
        Check(new Link { InvalidValue = true }.Run() == null, "impossible DPI");
        Check(new Link { OfflineAt = 1 }.Run(new(state!, 0)) == null, "offline or wrong model");
        Check(new Link { OfflineAt = 2 }.Run(new(state!, 0)) == null, "identity lost after write");
        Check(new Link { IgnoreWrite = true }.Run(new(state!, 0)) == null, "no write confirmation from feature success alone");
        Check(new Link { ChangeTableAfterWrite = true }.Run(new(state!, 0)) == null, "readback table changed");
        var separate = new Link { Separate = true };
        Check(separate.Run() is { SeparateAxes: true, Writable: false } && separate.Run(new(state!, 0)) == null &&
            separate.Writes == 0, "independent axes remain read-only");
        var differentProfile = new Link { Profile = 2 };
        Check(differentProfile.Run(new(state!, 0)) == null && differentProfile.Writes == 0, "stale profile selection");
        return assertions;
    }

    private sealed class Link
    {
        internal byte[] Report = new byte[65];
        internal byte[]? LastWrite;
        internal byte Profile = 1;
        internal bool InvalidValue, IgnoreWrite, Separate, ChangeTableAfterWrite;
        internal int OfflineAt = int.MaxValue, Writes;
        private int identityQueries;
        private byte wirelessIndex = 1, usbIndex;

        internal MouseDpiState? Run(MouseDpiChange? change = null) => MchoseA7UltraDpi.ReadOrChange(change,
            _ => Reply(), Write, () => ++identityQueries < OfflineAt);

        private byte[] Reply()
        {
            if (Report.Length != 65 || Report[0] != 0) return Report;
            var result = new byte[65];
            result[0] = 0x12;
            result[1] = 0x67;
            result[2] = Profile;
            result[3] = (byte)(4 << 4 | usbIndex);
            result[4] = (byte)(5 << 4 | wirelessIndex);
            result[18] = 3;
            result[52] = 0xFF;
            ushort[] values = [(ushort)(InvalidValue ? 42001 : ChangeTableAfterWrite && Writes > 0 ? 850 : 800), 1600, 42000];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6 + i * 2), values[i]);
                BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(53 + i * 2), (ushort)(Separate && i == 1 ? 1800 : values[i]));
            }
            for (int i = 1; i < result.Length; i++) result[i] ^= 0xFF;
            return result;
        }

        private bool Write(byte[] request)
        {
            Writes++;
            LastWrite = request;
            if (!IgnoreWrite)
            {
                usbIndex = (byte)(request[2] ^ 0xFF);
                wirelessIndex = (byte)(request[3] ^ 0xFF);
            }
            return true;
        }
    }
}
