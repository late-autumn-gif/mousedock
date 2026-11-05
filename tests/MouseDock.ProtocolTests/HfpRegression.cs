#nullable disable
using MouseDock.Services.Detection;

internal static class HfpRegression
{
    public static int Run()
    {
        int checks = 0;
        void Expect(string text, int expected)
        {
            checks++;
            bool parsed = HfpBattery.TryParseBatteryResponse(text, out int level);
            if (parsed != (expected >= 0) || level != expected)
                throw new Exception($"FAIL #{checks}: expected {expected}, got ({parsed}, {level}); text={text.Replace("\r", "<CR>").Replace("\n", "<LF>")}");
        }
        Expect("AT+IPHONEACCEV=1,1,6\r", 67);
        Expect("AT+IPHONEACCEV=1,1,0\r", 0);
        Expect("AT+IPHONEACCEV=1,1,9\r\n", 100);
        Expect("AT+IPHONEACCEV=2,2,1,1,6\r", 67);
        Expect("AT+IPHONEACCEV=2,1,6,2,1\n", 67);
        Expect(" at+iphoneaccev= 2 , 2 , 1 , 1 , 6 \r", 67);
        Expect("AT+BRSF=127\r\nAT+XAPL=ABCD-1234-0100,10\r\nAT+IPHONEACCEV=1,1,6\r", 67);
        Expect("AT+IPHONEACCEV=1,1,1", -1); // split packet: no terminator yet
        Expect("AT+IPHONEACCEV=1,1,10\r", -1); // the continuation must not become 11%
        Expect("AT+IPHONEACCEV=1,1,100\r", -1);
        Expect("AT+IPHONEACCEV=1,1,-1\r", -1);
        Expect("AT+IPHONEACCEV=1,1,+1\r", -1);
        Expect("AT+IPHONEACCEV=1,1,0x6\r", -1);
        Expect("AT+IPHONEACCEV=1,1,2147483648\r", -1);
        Expect("AT+IPHONEACCEV=2147483647,1,6\r", -1);
        Expect("AT+IPHONEACCEV=1,1,6trailing\r", -1);
        Expect("AT+IPHONEACCEV=2,1,6\r", -1);
        Expect("AT+IPHONEACCEV=1,1,6,2,1\r", -1);
        Expect("AT+IPHONEACCEV=0\r", -1);
        Expect("AT+IPHONEACCEV=1,2,6\r", -1);
        Expect("AT+IPHONEACCEV=2,1,6,1,7\r", -1);
        Expect("AT+IPHONEACCEV=2,1,6,2,invalid\r", -1);
        Expect("AT+IPHONEACCEV=1,1,\r", -1);
        Expect("noiseAT+IPHONEACCEV=1,1,6\r", -1);
        Expect("AT+IPHONEACCEV=1,1,10\r\nAT+IPHONEACCEV=1,1,6\r", 67);
        Expect("+IPHONEACCEV: 1,65\r", 65);
        Expect("+IPHONEACCEV: 1,0\r", 0);
        Expect("+IPHONEACCEV: 1,100\n", 100);
        Expect("+IPHONEACCEV: 1,101\r", -1);
        Expect("+IPHONEACCEV: 1,65,9\r", -1);
        Expect("+IPHONEACCEV: 2,65\r", -1);
        Expect("+IPHONEACCEV: 1,65x\r", -1);
        Expect("+IPHONEACCEV: 1,65", -1);
        Expect("", -1);
        checks++;
        if (HfpBattery.TryParseBatteryResponse(null, out _)) throw new Exception("null parsed");
        return checks;
    }
}
