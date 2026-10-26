using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

/// <summary>
/// 绕过 Windows 电池 API，直接走 RFCOMM 连耳机的 Hands-Free 服务，
/// 以音频网关身份发 AT 指令，逼设备回报 +IPHONEACCEV 电量。
/// </summary>
internal static class HfpBatteryProbe
{
    private static readonly Guid HandsfreeUuid = new Guid("0000111E-0000-1000-8000-00805F9B34FB");
    private static readonly Guid AudioGatewayUuid = new Guid("00001112-0000-1000-8000-00805F9B34FB");

    public static Task RunAsync(string nameHint) => Task.Run(async () =>
    {
        Console.WriteLine("=== 直连 HFP 抢读电量 ===");

        var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector()).AsTask().ConfigureAwait(false);
        var target = devices.FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) &&
                                                 (string.IsNullOrEmpty(nameHint) || d.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase)));
        if (target == null)
        {
            Console.WriteLine("找不到目标设备，现有: " + string.Join(", ", devices.Select(d => d.Name)));
            return;
        }
        Console.WriteLine("目标: " + target.Name);
        // 先把 Windows 自己存的 AEP 属性全打出来，看有没有现成电量
        Console.WriteLine("Windows 侧属性:");
        foreach (var kv in target.Properties)
        {
            string v = kv.Value?.ToString() ?? "null";
            if (v.Length > 80) v = v.Substring(0, 80) + "…";
            Console.WriteLine($"   {kv.Key} = {v}");
        }

        var bd = await BluetoothDevice.FromIdAsync(target.Id).AsTask().ConfigureAwait(false);
        if (bd == null) { Console.WriteLine("打不开 BluetoothDevice"); return; }
        Console.WriteLine($"连接状态: {bd.ConnectionStatus}   地址: 0x{bd.BluetoothAddress:X}");

        RfcommDeviceServicesResult services = null;
        try { services = await bd.GetRfcommServicesAsync().AsTask().ConfigureAwait(false); }
        catch (Exception ex) { Console.WriteLine("取服务失败: " + ex.Message); }

        if (services == null || services.Services.Count == 0)
        {
            Console.WriteLine("这台设备没有暴露可访问的 RFCOMM 服务");
            bd.Dispose();
            return;
        }

        Console.WriteLine($"暴露 {services.Services.Count} 个 RFCOMM 服务：");
        foreach (var s in services.Services)
            Console.WriteLine("   · " + s.ServiceId.Uuid +
                              (s.ServiceId.Uuid == HandsfreeUuid ? "  ← Hands-Free" : "") +
                              (s.ServiceId.Uuid == AudioGatewayUuid ? "  ← Audio Gateway" : ""));

        foreach (var s in services.Services)
        {
            bool isAt = s.ServiceId.Uuid == HandsfreeUuid || s.ServiceId.Uuid == AudioGatewayUuid;
            Console.WriteLine();
            Console.WriteLine($"— 连接 {s.ServiceId.Uuid}（{(isAt ? "发 AT 指令" : "先被动监听")}）");
            await Ask(s, isAt).ConfigureAwait(false);
        }

        bd.Dispose();
    });

    private static async Task Ask(RfcommDeviceService service, bool sendAt)
    {
        try
        {
            using var socket = new StreamSocket();
            try
            {
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName,
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask().ConfigureAwait(false);
                Console.WriteLine("   已连上 RFCOMM");
            }
            catch (Exception ex)
            {
                Console.WriteLine("   连接被拒（多半被 Windows 自己的 HFP 链路占着）: " + ex.Message);
                return;
            }

            if (sendAt)
            {
                var writer = new DataWriter(socket.OutputStream);
                string[] commands = { "AT+XAPL=0000-0000-0100,0\r", "AT+IPHONEACCEV=1,0\r", "AT+CIND=?\r" };
                foreach (var cmd in commands)
                {
                    writer.WriteString(cmd);
                    await writer.StoreAsync().AsTask().ConfigureAwait(false);
                    await writer.FlushAsync().AsTask().ConfigureAwait(false);
                    Console.WriteLine("   > " + cmd.Trim());
                    await Task.Delay(250).ConfigureAwait(false);
                }
            }

            var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
            var deadline = DateTime.UtcNow.AddSeconds(4);
            bool hit = false;
            while (DateTime.UtcNow < deadline)
            {
                uint loaded;
                try { loaded = await reader.LoadAsync(256).AsTask().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                catch (TimeoutException) { continue; }
                catch (Exception ex) { Console.WriteLine("   读失败: " + ex.Message); break; }

                if (loaded == 0) continue;
                string text = reader.ReadString(loaded);
                Console.WriteLine("   < " + text.Replace("\r", "\\r").Replace("\n", "\\n"));
                if (text.IndexOf("IPHONEACCEV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("XAPL", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("BATT", StringComparison.OrdinalIgnoreCase) >= 0)
                    hit = true;
            }

            Console.WriteLine(hit ? "   ★ 收到电量相关响应" : "   4 秒内没有电量响应");
        }
        catch (Exception ex)
        {
            Console.WriteLine("   异常: " + ex.GetType().Name + " " + ex.Message);
        }
    }
}
