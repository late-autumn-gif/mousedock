using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace MouseDock.Services.Detection
{
    /// <summary>
    /// 经典蓝牙耳机电量：走 HFP 的 Apple 扩展。
    ///
    /// 实测这台 Air Plus Pro3 会在 SLC 建立后主动发：
    ///     AT+XAPL=ABCD-1234-0100,10
    ///     AT+IPHONEACCEV=1,1,6      ← 第 3 个字段 = 电量档位 0–9
    ///
    /// 限制：HFP 的 RFCOMM 通道同一时刻只能有一个客户端。Windows 音频栈会一直占着它，
    /// 所以只有在「Windows 的免提节点被禁用」时这里才连得上；连不上就返回 -1，不影响其它设备。
    /// </summary>
    internal static class HfpBattery
    {
        private static readonly Guid HfUuid = new Guid("0000111E-0000-1000-8000-00805F9B34FB");

        public static int TryRead(string nameHint, int timeoutMs = 5000, Action<string> trace = null)
        {
            try { return TryReadAsync(nameHint, timeoutMs, trace).GetAwaiter().GetResult(); }
            catch (Exception ex) { trace?.Invoke("异常: " + ex.GetType().Name + " " + ex.Message); return -1; }
        }

        private static async Task<int> TryReadAsync(string nameHint, int timeoutMs, Action<string> trace)
        {
            var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector()).AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);

            var target = infos.FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) &&
                                                   d.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase));
            if (target == null) { trace?.Invoke("找不到设备 " + nameHint); return -1; }
            trace?.Invoke("目标: " + target.Name);

            BluetoothDevice device = null;
            try
            {
                device = await BluetoothDevice.FromIdAsync(target.Id).AsTask()
                    .WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);
                if (device == null) { trace?.Invoke("FromIdAsync 返回 null"); return -1; }
                trace?.Invoke("连接状态: " + device.ConnectionStatus);
                if (device.ConnectionStatus != BluetoothConnectionStatus.Connected) return -1;

                var services = await device.GetRfcommServicesAsync().AsTask()
                    .WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);
                var hf = services.Services.FirstOrDefault(s => s.ServiceId.Uuid == HfUuid);
                if (hf == null) { trace?.Invoke("没有 HFP 服务，服务数=" + services.Services.Count); return -1; }
                trace?.Invoke("找到 HF 服务，开始连接…");

                using var socket = new StreamSocket();
                try
                {
                    await socket.ConnectAsync(hf.ConnectionHostName, hf.ConnectionServiceName,
                        SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask()
                        .WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);
                }
                catch (Exception ex) { trace?.Invoke("连接失败: " + ex.Message); return -1; }
                trace?.Invoke("已连接，开始握手");

                var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
                var writer = new DataWriter(socket.OutputStream);
                var sb = new StringBuilder();
                var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                bool brsfDone = false, slcDone = false;

                while (DateTime.UtcNow < deadline)
                {
                    byte[] chunk = null;
                    try
                    {
                        var load = reader.LoadAsync(256).AsTask();
                        if (await Task.WhenAny(load, Task.Delay(300)).ConfigureAwait(false) == load)
                        {
                            uint n = await load.ConfigureAwait(false);
                            if (n > 0) { chunk = new byte[n]; reader.ReadBytes(chunk); }
                        }
                    }
                    catch { break; }

                    if (chunk == null) continue;
                    sb.Append(Encoding.ASCII.GetString(chunk));
                    string all = sb.ToString();

                    if (!brsfDone && all.Contains("AT+BRSF"))
                    {
                        // 一次性把 AG 侧的 SLC 应答和 Apple 扩展全部发出去，不依赖耳机的下一条命令
                        writer.WriteString("+BRSF: 1023\r\nOK\r\n");
                        writer.WriteString("+CIND: (\"service\",(0-1)),(\"call\",(0-1)),(\"callsetup\",(0-3))," +
                                           "(\"callheld\",(0-2)),(\"signal\",(0-5)),(\"roam\",(0-1)),(\"battchg\",(0-5))\r\nOK\r\n");
                        writer.WriteString("AT+CMER=3,0,0,1\r\n");
                        writer.WriteString("AT+XAPL=0000-0000-0100,0\r");
                        writer.WriteString("AT+IPHONEACCEV=1,0\r");
                        await writer.StoreAsync().AsTask().ConfigureAwait(false);
                        await writer.FlushAsync().AsTask().ConfigureAwait(false);
                        trace?.Invoke("已发送 AG 全套应答 + XAPL");
                        brsfDone = true;
                        slcDone = true;
                    }

                    if (!slcDone && all.Contains("AT+CIND=?"))
                    {
                        writer.WriteString("+CIND: (\"service\",(0-1)),(\"call\",(0-1)),(\"callsetup\",(0-3))," +
                                           "(\"callheld\",(0-2)),(\"signal\",(0-5)),(\"roam\",(0-1)),(\"battchg\",(0-5))\r\nOK\r\n");
                        await writer.StoreAsync().AsTask().ConfigureAwait(false);
                        writer.WriteString("AT+CMER=3,0,0,1\r\n");
                        await writer.StoreAsync().AsTask().ConfigureAwait(false);

                        // 关键：先由 AG 宣告 Apple 扩展，耳机才会回自己的 AT+XAPL / AT+IPHONEACCEV
                        writer.WriteString("AT+XAPL=0000-0000-0100,0\r");
                        await writer.StoreAsync().AsTask().ConfigureAwait(false);
                        writer.WriteString("AT+IPHONEACCEV=1,0\r");
                        await writer.StoreAsync().AsTask().ConfigureAwait(false);
                        await writer.FlushAsync().AsTask().ConfigureAwait(false);
                        slcDone = true;
                    }

                    // AT+IPHONEACCEV=<count>,<key>,<value>；key=1 是电量，value 0–9
                    var m = System.Text.RegularExpressions.Regex.Match(all, @"AT\+IPHONEACCEV=\d+\s*,\s*1\s*,\s*(\d)");
                    if (m.Success)
                    {
                        int raw = int.Parse(m.Groups[1].Value);
                        if (raw >= 0 && raw <= 9)
                        {
                            int pct = (int)Math.Round(raw / 9.0 * 100);
                            return Math.Clamp(pct, 1, 100);
                        }
                    }

                    // 个别固件回 +IPHONEACCEV: 1,<level>
                    var m2 = System.Text.RegularExpressions.Regex.Match(all, @"\+IPHONEACCEV:\s*1\s*,\s*(\d{1,3})");
                    if (m2.Success)
                    {
                        int pct = int.Parse(m2.Groups[1].Value);
                        if (pct >= 0 && pct <= 100) return Math.Clamp(pct, 1, 100);
                    }
                }

                try { socket.Dispose(); } catch { }
                return -1;
            }
            finally { device?.Dispose(); }
        }
    }
}
