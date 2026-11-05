using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

/// <summary>
/// 直连蓝牙耳机的厂商私有 RFCOMM 服务（fe010000-…），先被动听，再发常见 TWS 查询帧，
/// 看能不能逼出电量。全程只发极短的探测帧，不改变耳机状态。
/// </summary>
internal static class RfcommProbe
{
    private static readonly Guid VendorUuid = new Guid("fe010000-1234-5678-abcd-00805f9b34fb");
    private static readonly Guid SppUuid = new Guid("00001101-0000-1000-8000-00805f9b34fb");

    public static Task RunAsync(string hint) => Task.Run(async () =>
    {
        var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector()).AsTask().ConfigureAwait(false);
        var target = devices.FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) && d.Name.Contains(hint, StringComparison.OrdinalIgnoreCase));
        if (target == null) { Console.WriteLine("找不到设备"); return; }

        var bd = await BluetoothDevice.FromIdAsync(target.Id).AsTask().ConfigureAwait(false);
        Console.WriteLine($"目标 {target.Name}  状态={bd.ConnectionStatus}");

        var result = await bd.GetRfcommServicesAsync().AsTask().ConfigureAwait(false);
        foreach (var svc in result.Services)
        {
            if (svc.ServiceId.Uuid != VendorUuid && svc.ServiceId.Uuid != SppUuid) continue;
            Console.WriteLine($"\n--- 连接 {svc.ServiceId.Uuid} ---");
            await Talk(svc).ConfigureAwait(false);
        }
        bd.Dispose();
    });

    private static async Task Talk(RfcommDeviceService svc)
    {
        StreamSocket socket = null;
        try
        {
            socket = new StreamSocket();
            await socket.ConnectAsync(svc.ConnectionHostName, svc.ConnectionServiceName,
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask().ConfigureAwait(false);
            Console.WriteLine("   已连接");

            var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };

            // 1) 先被动听 3 秒，看设备是否主动推状态
            Console.WriteLine("   被动监听 3 秒…");
            var got = await ReadFor(reader, 3000).ConfigureAwait(false);
            if (got != null) Console.WriteLine("   < " + Show(got));

            // 2) 发几个极短的探测帧（TWS 私有服务常见形态）
            var probes = new (string name, byte[] data)[]
            {
                ("01", new byte[]{0x01}),
                ("01 01", new byte[]{0x01,0x01}),
                ("01 00 00", new byte[]{0x01,0x00,0x00}),
                ("AA 00 00", new byte[]{0xAA,0x00,0x00}),
                ("0x05", new byte[]{0x05}),
                ("0x06", new byte[]{0x06}),
                ("AT", new byte[]{0x41,0x54,0x0D}),
            };

            foreach (var p in probes)
            {
                var w = new DataWriter(socket.OutputStream);
                w.WriteBytes(p.data);
                await w.StoreAsync().AsTask().ConfigureAwait(false);
                await w.FlushAsync().AsTask().ConfigureAwait(false);
                Console.WriteLine($"   > {p.name}");

                var r = await ReadFor(reader, 700).ConfigureAwait(false);
                if (r != null) Console.WriteLine("   < " + Show(r));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("   异常: " + ex.GetType().Name + " " + ex.Message);
        }
        finally
        {
            try { socket?.Dispose(); } catch { }
        }
    }

    private static async Task<byte[]> ReadFor(DataReader reader, int ms)
    {
        try
        {
            var load = reader.LoadAsync(256).AsTask();
            var done = await Task.WhenAny(load, Task.Delay(ms)).ConfigureAwait(false);
            if (done != load) return null;
            uint n = await load.ConfigureAwait(false);
            if (n == 0) return null;
            var buf = new byte[n];
            reader.ReadBytes(buf);
            return buf;
        }
        catch { return null; }
    }

    private static string Show(byte[] b)
    {
        var hex = string.Join(" ", b.Take(24).Select(x => x.ToString("X2")));
        var ascii = new string(b.Take(24).Select(x => x >= 32 && x < 127 ? (char)x : '.').ToArray());
        return $"{b.Length}B  {hex}   |{ascii}|";
    }
}
