using System.IO.Pipes;
using System.Runtime.InteropServices;
using MouseDock.Services.Detection;

internal static class NativeIoRegression
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] private static extern IntPtr CreateEvent(IntPtr security, bool manual, bool initial, IntPtr name);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        string name = "MouseDock-IoRegression-" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var connected = server.WaitForConnectionAsync();
        IntPtr client = CreateFile(@"\\.\pipe\" + name, 0xC0000000, 0, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
        Check(client != new IntPtr(-1) && client != IntPtr.Zero, "Open real overlapped test pipe");
        IntPtr ev = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
        try
        {
            connected.GetAwaiter().GetResult();
            for (int i = 0; i < 40; i++)
            {
                var buffer = new byte[64];
                Check(!VendorProtocol.Transfer(client, ev, buffer, false, 1, out int count) && count == 0,
                    "An empty pipe times out and drains cancellation before releasing native memory");
                GC.Collect(); GC.WaitForPendingFinalizers();
                var send = server.WriteAsync(new byte[] { (byte)i }.AsMemory()).AsTask();
                Check(VendorProtocol.Transfer(client, ev, buffer, false, 1000, out count) && count == 1 && buffer[0] == i,
                    "The next read receives data; no abandoned read steals it");
                send.GetAwaiter().GetResult();
                var receive = new byte[64];
                var read = server.ReadAsync(receive).AsTask();
                buffer[0] = (byte)(i + 40);
                Check(VendorProtocol.Transfer(client, ev, buffer, true, 1000, out count) && count == 64,
                    "Overlapped write completes fully before memory is released");
                Check(read.GetAwaiter().GetResult() == 64 && receive[0] == i + 40, "Exact write payload reaches peer");
            }
            var zg4 = new VendorProtocol.Zg4HidLink(client, ev, 64);
            for (int i = 0; i < 40; i++)
            {
                Check(zg4.Read(1) == null, "ZG4 empty input cancels before reusing native buffers");
                byte value = (byte)(i + 80);
                var send = server.WriteAsync(new byte[] { value }.AsMemory()).AsTask();
                var bytes = zg4.Read(0) ?? zg4.Read(1000);
                Check(bytes is { Length: 1 } && bytes[0] == value,
                    "ZG4 retains a completed read even with an immediate cancellation boundary");
                send.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                var buffer = new byte[64];
                buffer[0] = value;
                var receive = new byte[64];
                var read = server.ReadAsync(receive).AsTask();
                Check(zg4.Write(buffer, 1000), "ZG4 read and write safely reuse the completed event");
                Check(read.GetAwaiter().GetResult() == 64 && receive[0] == value,
                    "ZG4 writes remain on the primary native link");
            }
            Check(!VendorProtocol.Transfer(new IntPtr(-1), ev, new byte[32], false, 1, out _), "Invalid handle fails safely");
            Check(new VendorProtocol.Zg4HidLink(new IntPtr(-1), ev, 64).Read(1) == null,
                "ZG4 invalid input handle fails without abandoning memory");
        }
        finally { CloseHandle(ev); CloseHandle(client); }
        return checks;
    }
}
