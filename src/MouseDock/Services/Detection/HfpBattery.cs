using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
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

        /// <summary>按蓝牙地址打开目标，避免同名耳机互相串读；字符串入口仅保留给命令行。</summary>
        public static int TryRead(ulong bluetoothAddress, int timeoutMs = 5000, Action<string> trace = null)
        {
            if (bluetoothAddress == 0 || bluetoothAddress > 0xFFFFFFFFFFFFUL) return -1;
            try
            {
                trace?.Invoke($"目标地址: {bluetoothAddress:X12}");
                return ReadDeviceAsync(() => BluetoothDevice.FromBluetoothAddressAsync(bluetoothAddress).AsTask(),
                    timeoutMs, trace).GetAwaiter().GetResult();
            }
            catch (Exception ex) { trace?.Invoke("异常: " + ex.GetType().Name + " " + ex.Message); return -1; }
        }

        private static async Task<int> TryReadAsync(string nameHint, int timeoutMs, Action<string> trace)
        {
            if (string.IsNullOrWhiteSpace(nameHint)) return -1;
            var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector()).AsTask()
                .WaitAsync(TimeSpan.FromMilliseconds(1200)).ConfigureAwait(false);

            var target = infos.FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) &&
                                                   d.Name.Contains(nameHint, StringComparison.OrdinalIgnoreCase));
            if (target == null) { trace?.Invoke("找不到设备 " + nameHint); return -1; }
            trace?.Invoke("目标: " + target.Name);

            return await ReadDeviceAsync(() => BluetoothDevice.FromIdAsync(target.Id).AsTask(),
                timeoutMs, trace).ConfigureAwait(false);
        }

        private static async Task<int> ReadDeviceAsync(Func<Task<BluetoothDevice>> openDevice,
            int timeoutMs, Action<string> trace)
        {
            BluetoothDevice device = null;
            try
            {
                device = await openDevice()
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
                catch (Exception ex)
                {
                    string message = ex.Message ?? "";
                    // RFCOMM 在同一时刻只能有一个客户端，Windows 音频栈会一直占着它。
                    // 这不是设备不支持，也不是本程序的错，直接说清楚，省得用户以为耳机坏了。
                    if (message.IndexOf("每个套接字地址", StringComparison.Ordinal) >= 0 ||
                        message.IndexOf("only one usage of each socket address", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        message.IndexOf("10048", StringComparison.Ordinal) >= 0)
                    {
                        trace?.Invoke("连接失败: RFCOMM 通道被 Windows 音频栈占用（系统占用，不是设备问题）");
                    }
                    else trace?.Invoke("连接失败: " + message);
                    return -1;
                }
                trace?.Invoke("已连接，开始握手");

                using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
                using var writer = new DataWriter(socket.OutputStream);
                using var readTimeout = new CancellationTokenSource(Math.Max(1, timeoutMs));
                var token = readTimeout.Token;
                var sb = new StringBuilder();
                bool brsfDone = false, slcDone = false;

                while (!token.IsCancellationRequested)
                {
                    // 同一时刻只有一次读取；超时取消该 WinRT 操作，而不是遗留读取再发起下一次。
                    uint n = await reader.LoadAsync(256).AsTask(token).ConfigureAwait(false);
                    if (n == 0) return -1;
                    var chunk = new byte[n];
                    reader.ReadBytes(chunk);
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
                        await writer.StoreAsync().AsTask(token).ConfigureAwait(false);
                        await writer.FlushAsync().AsTask(token).ConfigureAwait(false);
                        trace?.Invoke("已发送 AG 全套应答 + XAPL");
                        brsfDone = true;
                        slcDone = true;
                    }

                    if (!slcDone && all.Contains("AT+CIND=?"))
                    {
                        writer.WriteString("+CIND: (\"service\",(0-1)),(\"call\",(0-1)),(\"callsetup\",(0-3))," +
                                           "(\"callheld\",(0-2)),(\"signal\",(0-5)),(\"roam\",(0-1)),(\"battchg\",(0-5))\r\nOK\r\n");
                        await writer.StoreAsync().AsTask(token).ConfigureAwait(false);
                        writer.WriteString("AT+CMER=3,0,0,1\r\n");
                        await writer.StoreAsync().AsTask(token).ConfigureAwait(false);

                        // 关键：先由 AG 宣告 Apple 扩展，耳机才会回自己的 AT+XAPL / AT+IPHONEACCEV
                        writer.WriteString("AT+XAPL=0000-0000-0100,0\r");
                        await writer.StoreAsync().AsTask(token).ConfigureAwait(false);
                        writer.WriteString("AT+IPHONEACCEV=1,0\r");
                        await writer.StoreAsync().AsTask(token).ConfigureAwait(false);
                        await writer.FlushAsync().AsTask(token).ConfigureAwait(false);
                        slcDone = true;
                    }

                    if (TryParseBatteryResponse(all, out int level)) return level;
                }

                return -1;
            }
            catch (OperationCanceledException) { trace?.Invoke("HFP 电量读取超时"); return -1; }
            finally { device?.Dispose(); }
        }

        /// <summary>只解析 CR/LF 结束的完整行，避免把分包中的 10 误读成档位 1。</summary>
        internal static bool TryParseBatteryResponse(string transcript, out int level)
        {
            level = -1;
            if (string.IsNullOrEmpty(transcript)) return false;
            int start = 0;
            for (int i = 0; i < transcript.Length; i++)
            {
                if (transcript[i] != '\r' && transcript[i] != '\n') continue;
                string line = transcript.Substring(start, i - start).Trim();
                start = i + 1;
                const string command = "AT+IPHONEACCEV=";
                const string response = "+IPHONEACCEV:";
                if (line.StartsWith(command, StringComparison.OrdinalIgnoreCase))
                {
                    var fields = line.Substring(command.Length).Split(',');
                    if (fields.Length % 2 != 1 || !TryReadNumber(fields[0], out int count)
                        || count != (fields.Length - 1) / 2) continue;
                    int raw = -1;
                    bool valid = true;
                    for (int j = 1; j < fields.Length; j += 2)
                    {
                        if (!TryReadNumber(fields[j], out int key) || !TryReadNumber(fields[j + 1], out int value))
                        { valid = false; break; }
                        if (key != 1) continue;
                        if (value > 9 || raw >= 0) { valid = false; break; }
                        raw = value;
                    }
                    if (valid && raw >= 0)
                    {
                        // 保留已有档位换算；0 不再被伪造为 1%。
                        level = (int)Math.Round(raw / 9.0 * 100);
                        return true;
                    }
                }
                else if (line.StartsWith(response, StringComparison.OrdinalIgnoreCase))
                {
                    // 保留已有固件兼容格式，其 value 已是百分比。
                    var fields = line.Substring(response.Length).Split(',');
                    if (fields.Length == 2 && TryReadNumber(fields[0], out int key) && key == 1
                        && TryReadNumber(fields[1], out int value) && value <= 100)
                    { level = value; return true; }
                }
            }
            return false;
        }

        private static bool TryReadNumber(string text, out int value) =>
            int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
