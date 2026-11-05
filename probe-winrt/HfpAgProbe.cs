using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

/// <summary>
/// 以「音频网关（AG）」身份接管 HFP：先应答 HF 的 AT+BRSF，再做 Apple 扩展握手
/// （AT+XAPL / AT+IPHONEACCEV），等耳机回 +IPHONEACCEV: 1,&lt;电量&gt;,0。
/// </summary>
internal static class HfpAgProbe
{
    private static readonly Guid HfUuid = new Guid("0000111E-0000-1000-8000-00805F9B34FB");

    public static Task RunAsync(string hint, int seconds = 15) => Task.Run(async () =>
    {
        var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector()).AsTask().ConfigureAwait(false);
        var target = devices.FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) && d.Name.Contains(hint, StringComparison.OrdinalIgnoreCase));
        if (target == null) { Console.WriteLine("找不到设备"); return; }

        var bd = await BluetoothDevice.FromIdAsync(target.Id).AsTask().ConfigureAwait(false);
        var services = await bd.GetRfcommServicesAsync().AsTask().ConfigureAwait(false);
        var hf = services.Services.FirstOrDefault(s => s.ServiceId.Uuid == HfUuid);
        if (hf == null) { Console.WriteLine("没有 HFP 服务"); bd.Dispose(); return; }

        using var socket = new StreamSocket();
        try
        {
            await socket.ConnectAsync(hf.ConnectionHostName, hf.ConnectionServiceName,
                SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask().ConfigureAwait(false);
            Console.WriteLine("已连上 HFP（我们是 AG）");
        }
        catch (Exception ex)
        {
            Console.WriteLine("连接失败: " + ex.Message);
            bd.Dispose();
            return;
        }

        var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        var writer = new DataWriter(socket.OutputStream);
        var buffer = new StringBuilder();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        bool brsfDone = false, xaplDone = false, accevArmed = false;
        int battery = -1;

        async Task Send(string s)
        {
            writer.WriteString(s);
            await writer.StoreAsync().AsTask().ConfigureAwait(false);
            await writer.FlushAsync().AsTask().ConfigureAwait(false);
            Console.WriteLine("   > " + s.Replace("\r", "\\r"));
        }

        while (DateTime.UtcNow < deadline)
        {
            byte[] chunk = null;
            try
            {
                var load = reader.LoadAsync(128).AsTask();
                var done = await Task.WhenAny(load, Task.Delay(600)).ConfigureAwait(false);
                if (done == load)
                {
                    uint n = await load.ConfigureAwait(false);
                    if (n > 0)
                    {
                        chunk = new byte[n];
                        reader.ReadBytes(chunk);
                    }
                }
            }
            catch { break; }

            if (chunk == null) continue;
            buffer.Append(Encoding.ASCII.GetString(chunk));
            Console.WriteLine("   < " + Encoding.ASCII.GetString(chunk).Replace("\r", "\\r").Replace("\n", "\\n"));

            string all = buffer.ToString();
            if (!brsfDone && all.Contains("AT+BRSF"))
            {
                await Send("+BRSF: 1023\r\nOK\r\n");
                brsfDone = true;
            }

            if (brsfDone && !xaplDone)
            {
                await Send("AT+XAPL=0000-0000-0100,0\r");
                xaplDone = true;
            }

            if (xaplDone && !accevArmed)
            {
                await Send("AT+IPHONEACCEV=1,0\r");
                accevArmed = true;
            }

            // 任何其他 AT 命令都回 OK，避免耳机等应答卡住
            int idx;
            while ((idx = all.LastIndexOf("AT+", StringComparison.Ordinal)) >= 0 && idx == all.Length - 0)
                break;

            if (all.Contains("AT+CIND=?") && !all.Contains("+CIND:"))
            {
                await Send("+CIND: (\"batv\",(0-100)),(\"battchg\",(0-5))\r\nOK\r\n");
            }

            int m = System.Text.RegularExpressions.Regex.Match(all, @"\+IPHONEACCEV:\s*1\s*,\s*(\d+)\s*,\s*(\d+)").Value.Length;
            var mm = System.Text.RegularExpressions.Regex.Match(all, @"\+IPHONEACCEV:\s*1\s*,\s*(\d+)");
            if (mm.Success)
            {
                int v = int.Parse(mm.Groups[1].Value);
                if (v >= 0 && v <= 100)
                {
                    battery = v;
                    Console.WriteLine($"   ★ 电量 = {v}%");
                    break;
                }
            }
        }

        Console.WriteLine(battery >= 0 ? $"RESULT battery={battery}" : "RESULT none");
        try { socket.Dispose(); } catch { }
        bd.Dispose();
    });
}
