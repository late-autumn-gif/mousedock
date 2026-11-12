using MouseDock.Services.Detection;

internal static class VgnF1DpiRegression
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("VGN F1 MOBA DPI: " + message);
            checks++;
        }

        var link = new Link();
        var reading = VgnF1Protocol.Read(link, true);
        var state = reading.Dpi;
        Check(reading.Level == 70 && state is { Profile: 1, Index: 1, Count: 3, Writable: true } &&
            state.Values.SequenceEqual(new[] { 400, 1600, 26000 }), "identity, active profile, table and 10-bit sensor encoding");
        Check(VgnF1Protocol.Read(link, true, new(state!, 2)).Dpi is { Index: 2 } && link.Writes == 1,
            "switch and verify without depending on write acknowledgment");
        var selection = link.Selection!;
        Check(selection.AsSpan(0, 8).SequenceEqual(new byte[] { 8, 7, 0, 0, 4, 2, 2, 0x53 }) && selection[16] == 0xEB &&
            selection.AsSpan(8, 8).IndexOfAnyExcept((byte)0) < 0, "official two-byte EEPROM record and both checksum layers");
        Check(VgnF1Protocol.Read(link, true, new(state!, 2)).Dpi is { Index: 2 } && link.Writes == 1, "no-op selection");
        Check(VgnF1Protocol.Read(new Link { Mid = 3 }, true).Dpi == null, "wrong paired model");
        var replaced = new Link { Address = 9 };
        Check(VgnF1Protocol.Read(replaced, true, new(state!, 0)).Dpi == null && replaced.Writes == 0, "stale paired address");
        Check(VgnF1Protocol.Read(new Link { BadChecksum = true }, true).Dpi == null, "bad outer checksum");
        Check(VgnF1Protocol.Read(new Link { BadRecord = true }, true).Dpi == null, "bad EEPROM checksum despite valid outer checksum");
        Check(VgnF1Protocol.Read(new Link { WrongOffset = true }, true).Dpi == null, "wrong EEPROM address");
        Check(VgnF1Protocol.Read(new Link { Short = true }, true).Dpi == null, "truncated table response");
        Check(VgnF1Protocol.Read(new Link { Stage = 4 }, true).Dpi == null, "out-of-range active stage");
        Check(VgnF1Protocol.Read(new Link { Split = true }, true).Dpi is { SeparateAxes: true, Writable: false }, "independent axes read X only");
        Check(VgnF1Protocol.Read(new Link { IgnoreWrite = true }, true, new(state!, 2)).Dpi == null, "unchanged stage");
        Check(VgnF1Protocol.Read(new Link { ChangeTable = true }, true, new(state!, 2)).Dpi == null, "changed stage table after write");
        Check(VgnF1Protocol.Read(new Link { ChangedPairing = true }, true, new(state!, 2)).Dpi == null, "pairing changed after write");
        var noDpi = new Link();
        Check(VgnF1Protocol.Read(noDpi).Level == 70 && noDpi.DpiRequests == 0, "battery-only calls do not send DPI commands");
        return checks;
    }

    private sealed class Link : ReferenceBatteryProtocol.ILink
    {
        internal byte Mid = 5, Address = 7, Stage = 1;
        internal bool BadChecksum, BadRecord, WrongOffset, Short, Split, IgnoreWrite, ChangeTable, ChangedPairing;
        internal byte[]? Selection;
        internal int Writes, DpiRequests;
        private byte[]? next;
        public long Now { get; private set; }

        public byte[]? Read(int timeoutMs)
        {
            Now += next == null ? timeoutMs : 1;
            var result = next;
            next = null;
            return result;
        }

        public bool Write(byte[] request, int timeoutMs)
        {
            if (request.Sum(value => (int)value) % 256 != 0x55) throw new InvalidOperationException("VGN checksum");
            var frame = new byte[17];
            request.AsSpan(0, 6).CopyTo(frame);
            bool table = false;
            switch (request[1])
            {
                case 1:
                    frame[10] = 2;
                    frame[11] = Mid;
                    break;
                case 3:
                    frame[6] = 1;
                    frame[7] = ChangedPairing && Writes > 0 ? (byte)(Address + 1) : Address;
                    break;
                case 4:
                    frame[6] = 70;
                    break;
                case 0x0E:
                    DpiRequests++;
                    frame[5] = 1;
                    frame[6] = 1;
                    break;
                case 8:
                    DpiRequests++;
                    if (request[4] == 2)
                    {
                        frame[6] = 3;
                        frame[7] = 0x52;
                        frame[8] = Stage;
                        frame[9] = (byte)(0x55 - Stage);
                    }
                    else
                    {
                        table = true;
                        if (request[4] == 12)
                        {
                            Record(frame.AsSpan(6), ChangeTable && Writes > 0 ? 8 : 7, Split ? 8 : 7);
                            Record(frame.AsSpan(10), 31, 31);
                        }
                        else Record(frame.AsSpan(6), 519, 519);
                        if (BadRecord) frame[9]++;
                        if (WrongOffset) frame[4]++;
                    }
                    break;
                case 7:
                    Writes++;
                    Selection = request;
                    if (!IgnoreWrite) Stage = request[6];
                    return true;
                default: throw new InvalidOperationException("Unexpected VGN command");
            }
            VgnF1Protocol.SetChecksum(frame);
            if (table && BadChecksum) frame[16]++;
            next = table && Short ? frame[..16] : frame;
            return true;
        }

        private static void Record(Span<byte> target, int x, int y)
        {
            target[0] = (byte)x;
            target[1] = (byte)y;
            target[2] = (byte)(((x >> 8) << 2) | ((y >> 8) << 6));
            target[3] = (byte)(0x55 - target[0] - target[1] - target[2]);
        }

        public bool SetFeature(byte[] request) => throw new NotSupportedException();
        public byte[] GetFeature(byte reportId, int length) => throw new NotSupportedException();
        public void Delay(int milliseconds) => Now += milliseconds;
    }
}
