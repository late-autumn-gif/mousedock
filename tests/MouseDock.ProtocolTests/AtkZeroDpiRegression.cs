using System.Buffers.Binary;
using MouseDock.Models;
using MouseDock.Services.Detection;

internal static class AtkZeroDpiRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("ATK ZERO DPI: " + message);
            count++;
        }

        foreach (ushort pid in new ushort[] { 0x1154, 0x1155, 0x124F })
        {
            bool wireless = pid != 0x1154;
            var link = new Link(wireless);
            var reading = ZeroRainyProtocol.Read(link, 0x373B, pid, wireless ? "Receiver" : "ATK ZERO", true);
            var state = reading.Dpi;
            Check(reading.Level == 72 && state is { Index: 1, Count: 4, Profile: 0, Writable: true } &&
                state.Values[state.Index] == 800, $"{pid:X4}: current DPI and battery");
            var changed = ZeroRainyProtocol.Read(link, 0x373B, pid, wireless ? "Receiver" : "ATK ZERO", true,
                new(state!, 2));
            Check(changed.Dpi is { Index: 2 } dpi && dpi.Values[2] == 1600 && link.Writes == 1,
                $"{pid:X4}: choose and confirm an existing stage");
            var command = link.LastSelection!;
            Check(command[8] == 2 && command.AsSpan(9, 7).SequenceEqual(link.Table.AsSpan(18, 7)) &&
                command[16] == 0 && command[17] == 1, "selection preserves both axes and BGR");
        }

        var readRequest = AtkZeroDpi.ReadRequest(true, 11);
        Check(readRequest.AsSpan(0, 11).SequenceEqual(new byte[] { 8, 0xB6, 0x72, 10, 0, 1, 0x17, 13, 11, 0, 10 }),
            "official 0x17 request layout and checksum");
        var source = new Link(true);
        var expected = ZeroRainyProtocol.Read(source, 0x373B, 0x1155, "Receiver", true).Dpi!;
        var change = new MouseDpiChange(expected, 2);
        foreach (var link in new[]
        {
            new Link(true) { WrongModel = true }, new Link(true) { WrongAddress = true },
            new Link(true) { WrongLength = true }, new Link(true) { Profile = 1 },
            new Link(true) { RepairedBeforeWrite = true }, new Link(true) { Split = true },
            new Link(true) { ChangedTable = true }, new Link(true) { NonzeroAlpha = true }
        })
            Check(ZeroRainyProtocol.Read(link, 0x373B, 0x1155, "Receiver", true, change).Dpi == null && link.Writes == 0,
                "wrong identity, frame, profile or table never writes");

        var ignored = new Link(true) { IgnoreWrite = true };
        Check(ZeroRainyProtocol.Read(ignored, 0x373B, 0x1155, "Receiver", true, change).Dpi == null && ignored.Writes == 1,
            "acknowledgement without matching table readback is not success");
        var badColor = new Link(true) { MutateColor = true };
        Check(ZeroRainyProtocol.Read(badColor, 0x373B, 0x1155, "Receiver", true, change).Dpi == null && badColor.Writes == 1,
            "readback must preserve stage color");
        var wrongProduct = new Link(false);
        Check(ZeroRainyProtocol.Read(wrongProduct, 0x373B, 0x1154, "Generic Mouse", true).Dpi == null &&
            !wrongProduct.Commands.Contains(0x17), "wired DPI requires exact product string");
        var readOnly = new Link(true) { Split = true };
        Check(ZeroRainyProtocol.Read(readOnly, 0x373B, 0x1155, "Receiver", true).Dpi is { SeparateAxes: true, Writable: false },
            "split-axis table remains read-only");
        var stateForUi = ZeroRainyProtocol.Read(new Link(true), 0x373B, 0x1155, "Receiver", true).Dpi!;
        var ui = new AutoDetectSource().UpdateFromDetection([new()
        {
            Id = "zero", Kind = DeviceKind.Mouse, Dpi = stateForUi.Values[stateForUi.Index], DpiSettings = stateForUi
        }]).Single();
        Check(ui.DpiLabel == "800 DPI" && ui.CanChangeDpi, "DPI reaches main-panel model");
        return count;
    }

    private sealed class Link(bool receiver) : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> input = new();
        internal readonly List<byte> Commands = new();
        internal readonly byte[] Table = MakeTable();
        internal byte[]? LastSelection;
        internal int Writes, IdReads;
        internal byte Profile;
        internal bool WrongModel, WrongAddress, WrongLength, RepairedBeforeWrite, Split,
            IgnoreWrite, ChangedTable, NonzeroAlpha, MutateColor;

        private static byte[] MakeTable()
        {
            byte[] data = new byte[70];
            data[0] = 1;
            data[1] = 8;
            int[] values = [400, 800, 1600, 3200];
            for (int i = 0; i < values.Length; i++)
            {
                int offset = 2 + i * 8;
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), (ushort)values[i]);
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset + 2), (ushort)values[i]);
                data[offset + 4] = (byte)(16 + i);
                data[offset + 5] = (byte)(32 + i);
                data[offset + 6] = (byte)(48 + i);
            }
            return data;
        }

        public long Now { get; private set; }
        public byte[]? Read(int timeout)
        {
            Now += Math.Max(1, timeout);
            return input.TryDequeue(out var frame) ? frame : null;
        }
        public bool Write(byte[] request, int timeout)
        {
            if (request.Length != 64 || request[0] != 8 || request[2] != 0x72 || request[5] != (receiver ? 1 : 0) ||
                request[1] != (byte)request.Skip(2).Sum(x => (int)x))
                throw new InvalidOperationException("Incorrect ATK request envelope");
            byte cmd = request[6];
            Commands.Add(cmd);
            var reply = new byte[64];
            reply[0] = 8;
            reply[1] = 0x72;
            reply[5] = cmd;
            switch (cmd)
            {
                case 0x89:
                    IdReads++;
                    reply[8] = 1;
                    reply[9] = WrongModel || (RepairedBeforeWrite && IdReads >= 3) ? (byte)2 : (byte)1;
                    break;
                case 7: reply[7] = 72; break;
                case 15: break;
                case 0x09:
                    if (request[3] != 19 || request[7] != 17) throw new InvalidOperationException("Invalid profile query");
                    reply[6] = 17;
                    reply[7] = Profile;
                    break;
                case 0x17:
                    if (request[3] != 10 || request[7] != 13 || request[10] != 10)
                        throw new InvalidOperationException("Invalid block query");
                    if (Split) BinaryPrimitives.WriteUInt16LittleEndian(Table.AsSpan(12), 900);
                    if (ChangedTable) BinaryPrimitives.WriteUInt16LittleEndian(Table.AsSpan(2), 450);
                    if (NonzeroAlpha) Table[25] = 1;
                    ushort address = BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(8));
                    reply[6] = 13;
                    BinaryPrimitives.WriteUInt16LittleEndian(reply.AsSpan(7), WrongAddress ? (ushort)(address + 10) : address);
                    reply[9] = WrongLength ? (byte)9 : (byte)10;
                    Table.AsSpan(address - 1, 10).CopyTo(reply.AsSpan(10));
                    break;
                case 2:
                    if (request[3] != 12 || request[7] != 10) throw new InvalidOperationException("Invalid selection");
                    Writes++;
                    LastSelection = request.ToArray();
                    if (!IgnoreWrite) Table[0] = request[8];
                    if (MutateColor) Table[22]++;
                    break;
                default: throw new InvalidOperationException("Unexpected ATK command");
            }
            input.Enqueue(reply);
            return true;
        }
        public bool SetFeature(byte[] request) => throw new NotSupportedException();
        public byte[] GetFeature(byte reportId, int length) => throw new NotSupportedException();
        public void Delay(int milliseconds) => throw new NotSupportedException();
    }
}
