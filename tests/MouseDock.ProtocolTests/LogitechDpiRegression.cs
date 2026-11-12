using System.Text;
using MouseDock.Models;
using MouseDock.Services.Detection;

internal static class LogitechDpiRegression
{
    internal static int Run()
    {
        int count = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException("Logitech DPI: " + message);
            count++;
        }

        foreach (var (pid, name) in new (ushort, string)[]
        {
            (0xC088, "PRO Wireless Gaming Mouse"), (0xC539, "PRO Wireless Gaming Mouse"),
            (0xC547, "PRO X SUPERLIGHT"), (0xC539, "G603 Wireless Gaming Mouse")
        })
        {
            var link = new Link(name);
            var before = OriginalGpwProtocol.Read(link, pid, true);
            var state = before.Dpi;
            Check(before.Level >= 0 && state is { Writable: true, Protocol: MouseDpiProtocol.Hidpp } &&
                state.Values[state.Index] == 1600 && state.Values.Contains(3200), name + " reads host DPI");
            var result = OriginalGpwProtocol.Read(link, pid, true, new(state!, Array.IndexOf(state!.Values, 3200)));
            Check(result.Dpi is { } changed && changed.Values[changed.Index] == 3200 && link.DpiWrites == 1,
                name + " switches and confirms the requested DPI");
            Check(link.Requests.All(r => r.Length == 20 && r[0] == 0x11 && r[1] == 1 && (r[3] & 15) != 0),
                name + " uses only the identified slot and strict request IDs");
            var device = new AutoDetectSource().UpdateFromDetection([new()
            {
                Id = "mouse", Name = name, Kind = DeviceKind.Mouse, Dpi = 3200, DpiSettings = result.Dpi
            }]).Single();
            Check(device.CanChangeDpi && device.DpiLabel == "3200 DPI", "readback reaches main panel model");
        }

        var host = new Link();
        var expected = OriginalGpwProtocol.Read(host, 0xC539, true).Dpi!;
        var change = new MouseDpiChange(expected, Array.IndexOf(expected.Values, 3200));
        foreach (var link in new[]
        {
            new Link { Mode = 1 }, new Link { Mode = 3 }, new Link { DropMode = true },
            new Link { BadList = true }, new Link { WrongSensor = true },
            new Link("G603 Wireless Gaming Mouse")
        })
            Check(OriginalGpwProtocol.Read(link, 0xC539, true, change).Dpi == null && link.DpiWrites == 0,
                "unknown/changed capability, mode or identity must prevent writes");

        var onboard = new Link { Mode = 1 };
        Check(OriginalGpwProtocol.Read(onboard, 0xC539, true).Dpi is { Writable: false, Count: 1 } readOnly &&
            readOnly.Values[0] == 1600 && onboard.DpiWrites == 0, "onboard mode preserves readable DPI");
        var ignored = new Link { IgnoreWrite = true };
        Check(OriginalGpwProtocol.Read(ignored, 0xC539, true, change).Dpi == null && ignored.DpiWrites == 1,
            "acknowledgement without actual readback does not confirm write");
        var wrongClient = new Link { WrongReadbackClient = true };
        Check(OriginalGpwProtocol.Read(wrongClient, 0xC539, true, change).Dpi == null && wrongClient.DpiWrites == 1,
            "uncorrelated readback cannot confirm a write");
        var unknown = new Link("USB Receiver");
        Check(OriginalGpwProtocol.Read(unknown, 0xC539, true, change).Dpi == null &&
            !unknown.Requests.Any(r => r[2] == 0 && r[4] == 0x22), "receiver label cannot initiate DPI requests");
        var missing = new Link { HasDpi = false };
        var battery = OriginalGpwProtocol.Read(missing, 0xC539, true);
        Check(battery.Level >= 0 && battery.Dpi == null && missing.DpiWrites == 0, "missing DPI feature preserves battery");
        var zero = new Link { Current = 0 };
        Check(OriginalGpwProtocol.Read(zero, 0xC539, true).Dpi == null, "unknown current DPI stays unknown");
        return count;
    }

    private sealed class Link(string name = "PRO Wireless Gaming Mouse") : ReferenceBatteryProtocol.ILink
    {
        private readonly Queue<byte[]> input = new();
        internal readonly List<byte[]> Requests = new();
        internal int Mode = 2, Current = 1600, DpiWrites;
        internal bool DropMode, BadList, WrongSensor, IgnoreWrite, WrongReadbackClient;
        internal bool HasDpi = true;
        public long Now { get; private set; }
        public byte[]? Read(int timeoutMs)
        {
            Now += Math.Max(1, timeoutMs);
            return input.TryDequeue(out var frame) ? frame : null;
        }
        public bool Write(byte[] request, int timeoutMs)
        {
            Requests.Add(request.ToArray());
            var reply = new byte[20];
            request.AsSpan(0, 4).CopyTo(reply);
            int function = request[3] >> 4;
            switch (request[2])
            {
                case 0:
                    reply[4] = ((request[4] << 8) | request[5]) switch
                    {
                        5 => 2, 0x1001 => 3, 0x1000 => 4, 0x1004 => 5,
                        0x2201 when HasDpi => 6, 0x8100 => 7, _ => 0
                    };
                    break;
                case 2:
                    var text = Encoding.UTF8.GetBytes(name);
                    if (function == 0) reply[4] = (byte)text.Length;
                    else text.AsSpan(request[4], Math.Min(16, text.Length - request[4])).CopyTo(reply.AsSpan(4));
                    break;
                case 3: reply[4] = 0x10; break;
                case 4: reply[4] = 76; break;
                case 5:
                    if (function == 0) reply[5] = 2; else reply[4] = 76;
                    break;
                case 6 when function == 0: reply[4] = 1; break;
                case 6 when function == 1:
                    byte[] words = BadList ? [0x01, 0x90, 0xE0, 0x32] : [0x01, 0x90, 0xE0, 0x32, 0x19, 0x00];
                    words.CopyTo(reply, 5);
                    break;
                case 6 when function == 2:
                    reply[4] = WrongSensor ? (byte)1 : (byte)0;
                    reply[5] = (byte)(Current >> 8);
                    reply[6] = (byte)Current;
                    if (WrongReadbackClient && DpiWrites != 0) reply[3] ^= 1;
                    break;
                case 6 when function == 3:
                    if (request[4] != 0) throw new InvalidOperationException("Incorrect DPI sensor");
                    DpiWrites++;
                    if (!IgnoreWrite) Current = request[5] << 8 | request[6];
                    break;
                case 7 when function == 2:
                    if (DropMode) return true;
                    reply[4] = (byte)Mode;
                    break;
                default: throw new InvalidOperationException("Unexpected HID++ request");
            }
            input.Enqueue(reply);
            return true;
        }
        public bool SetFeature(byte[] request) => throw new InvalidOperationException();
        public byte[] GetFeature(byte reportId, int length) => throw new InvalidOperationException();
        public void Delay(int milliseconds) => throw new InvalidOperationException();
    }
}
