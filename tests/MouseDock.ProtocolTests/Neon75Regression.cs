using MouseDock.Services.Detection;

internal static class Neon75Regression
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool value,string message) { checks++; if(!value) throw new Exception(message); }
        var request=VendorProtocol.BuildNeon75StatusRequest();
        Check(request.Length==65 && request[0]==0 && request[1]==0xF7 && request[8]==8,"Exact official F7 Feature request with report ID");
        Check(request.Skip(1).Sum(b=>(int)b)==255 && request.Skip(9).All(b=>b==0),"Official seven-byte command checksum and zero padding");
        Check(VendorProtocol.IsNeon75StatusChannel(0x391D,0x2A05,0xFFFF,2,65),"Exact live receiver and Feature collection");
        Check(!VendorProtocol.IsNeon75StatusChannel(0x391D,0x2A05,0xFFFF,1,65),"Never send this query to the input collection");
        Check(!VendorProtocol.IsNeon75StatusChannel(0x391D,0x2A05,0xFFFF,2,64),"Require Windows report-ID-inclusive length");
        Check(!VendorProtocol.IsNeon75StatusChannel(0x391D,0x2A06,0xFFFF,2,65),"Do not generalize the protocol to another PID");
        var frame=Convert.FromHexString("0000520000010101000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
        Check(VendorProtocol.TryParseNeon75Status(frame,out int level,out bool charging) && level==82 && !charging,"Captured physical Neon75 response gives 82 percent");
        frame[3]=1;
        Check(VendorProtocol.TryParseNeon75Status(frame,out level,out charging) && !charging,"Do not mistake payload byte2 for the official charging byte9");
        frame[10]=1;
        Check(VendorProtocol.TryParseNeon75Status(frame,out level,out charging) && charging,"Official charging byte9 is propagated");
        frame[4]=1;
        Check(!VendorProtocol.TryParseNeon75Status(frame,out _,out _),"A sleeping receiver is not a fresh reading");
        frame[4]=0;
        foreach(byte percent in new byte[]{1,82,100})
        {
            frame[2]=percent;
            Check(VendorProtocol.TryParseNeon75Status(frame,out level,out _) && level==percent,"Valid percentage "+percent);
        }
        foreach(byte percent in new byte[]{0,101,255})
        {
            frame[2]=percent;
            Check(!VendorProtocol.TryParseNeon75Status(frame,out _,out _),"Do not default an empty/invalid field to 100");
        }
        Check(!VendorProtocol.TryParseNeon75Status(new byte[65],out _,out _),"All-zero Feature buffer is not a real percentage");
        Check(!VendorProtocol.TryParseNeon75Status(request,out _,out _),"An echoed query is not a status reading");
        foreach(int length in new[]{0,4,64,66})
            Check(!VendorProtocol.TryParseNeon75Status(new byte[length],out _,out _),"Reject wrong length "+length);
        frame[2]=82;frame[10]=2;
        Check(!VendorProtocol.TryParseNeon75Status(frame,out _,out _),"Reject unknown charging flag");
        return checks;
    }
}
