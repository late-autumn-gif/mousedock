using System;
using System.IO;
using System.Linq;

namespace MouseDock.Services.Detection;

internal static partial class VendorProtocol
{
    private static ReferenceBatteryProtocol.Reading MouseExpansionFreqRead(string family,string product,
        ReferenceBatteryProtocol.ILink link,byte inputId,byte outputId,int native)
    {
        bool mloong=family=="mloong",numbered=inputId!=0;
        string model=mloong?MouseExpansionMloong(product):null;
        string scheme=family=="freq-sc311"?"a":MouseExpansionFreq(product);
        bool cLayout=inputId==0xF0&&outputId==0xF0&&native==64;
        if(mloong)
        {
            if(model=="MX306"?!cLayout:inputId!=0||outputId!=0||native!=65)
                throw new IOException("Mloong report layout does not match model");
            byte[] Query(byte command)
            {
                var packet=model=="MX306"?
                    command==0xED?MouseExpansionPacket(64,0xF0,0xED,1):MouseExpansionPacket(64,0xF0,0x30,1,0,0x2E):
                    command==0xED?MouseExpansionPacket(65,0,0x55,0xED):
                        MouseExpansionPacket(65,0,0x55,0x30,0xA5,11,0x2E,1,1,1);
                return MouseExpansionExchange(link,packet,b=>b.Length==64&&
                    (model=="MX306"?b[0]==0xF0&&b[1]==command:
                        Enumerable.Range(0,2).Count(o=>(b[o] is 0x55 or 0xAA)&&b[o+1]==command)==1),1200);
            }
            int Offset(byte[] b)=>model=="MX306"?0:
                (b[0] is 0x55 or 0xAA)&&b[1] is 0x30 or 0xED?0:1;
            void Online()
            {
                var b=Query(0xED);
                if(b[Offset(b)+8]!=0)throw new IOException("Mloong mouse unavailable or unknown link state");
            }
            Online();
            int level=-1;
            for(int n=0;n<2;n++)
            {
                var b=Query(0x30);int o=Offset(b);
                if(b[o+8]>100||b[o+9]>1||model=="MX306"&&b[o+8]==0)
                    throw new IOException("invalid Mloong battery fields");
                level=b[o+8];
            }
            Online();
            return MouseExpansionValue(level,"Mloong reported percent; exact USB product and live-link bracket");
        }
        if(scheme=="m8")
        {
            if(inputId!=0||outputId!=0||native!=65)throw new IOException("M8 requires sole unnumbered reports");
            byte[] Query(byte command)=>MouseExpansionExchange(link,
                MouseExpansionPacket(65,0,0x55,command,0xA5,11,0x2E,1,1,1),
                b=>b.Length==64&&MouseExpansionPrefix(b,0xAA,command,0xA5)&&
                    b[4]==(command==0x30?10:46)&&b[5]==1&&b[6]==1&&b[7]==1,1200);
            void Configuration()
            {
                var b=Query(14);int count=b[11],index=b[12];
                if(count is <1 or >6||index<1||index>count)throw new IOException("M8 configuration unavailable");
                for(int n=0;n<count;n++)
                {
                    int dpi=b[13+2*n]|b[14+2*n]<<8;
                    if(dpi is <50 or >50000)throw new IOException("M8 configuration unavailable");
                }
            }
            Configuration();
            int level=-1;
            for(int n=0;n<2;n++){level=Query(0x30)[8];if(level>100)throw new IOException("invalid M8 battery");}
            Configuration();
            return MouseExpansionValue(level,"M8 reported percent; fresh configuration checked before and after");
        }
        if(scheme=="dual")
        {
            scheme="a";
            if(cLayout)
            {
                try
                {
                    var b=MouseExpansionExchange(link,MouseExpansionPacket(64,0xF0,0x52),
                        b=>b.Length==64&&b[0]==0xF0&&b[1]==0x52,600);
                    var name=b.AsSpan(9,24);
                    int zero=name.IndexOf((byte)0);
                    if(zero>=0)name=name[..zero];
                    if(name.Length>0&&name.ToArray().All(v=>v is >=32 and <=126))scheme="c";
                }
                catch(IOException) { }
            }
        }
        if(scheme=="c"&&!cLayout)throw new IOException("FREQ c requires report F0/64");
        byte[] query=scheme=="c"?MouseExpansionPacket(64,0xF0,0x30,0xA5,11,0x2E,1,1,1):
            family=="freq-sc311"?MouseExpansionPacket(65,0,0x55,0x30,0xA5,0x2E,0x2E):
                MouseExpansionPacket(native,outputId,0x55,0x30,1,11,0x2E,1,1,1);
        int percent=-1;
        for(int n=0;n<2;n++)
        {
            var b=MouseExpansionExchange(link,query,b=>
            {
                if(b.Length!=(numbered?native:native-1)||numbered&&b[0]!=inputId)return false;
                int start=numbered?1:0;
                return scheme=="c"?b[start]==0x30:
                    Enumerable.Range(start,2).Count(o=>(b[o] is 0xAA or 0xF0)&&b[o+1]==0x30)==1;
            },1200);
            int start=numbered?1:0,offset=start;
            if(scheme!="c"&&!(b[start] is 0xAA or 0xF0&&b[start+1]==0x30))offset++;
            percent=b[offset+(scheme=="c"?7:8)];
            int charge=b[offset+(scheme=="c"?8:9)];
            if(percent>100||charge>1)throw new IOException("invalid FREQ battery fields");
        }
        return MouseExpansionValue(percent,"FREQ reported percent; exact named or revision-scoped protocol");
    }
}
