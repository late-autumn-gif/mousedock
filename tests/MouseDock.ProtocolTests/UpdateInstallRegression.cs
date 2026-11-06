using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using MouseDock.Services;
internal static class UpdateInstallRegression
{
    private sealed class Handler : HttpMessageHandler
    {
        internal readonly List<string> Requests=new();
        internal Func<int,HttpResponseMessage> Reply=null!;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        { Requests.Add(request.RequestUri!.AbsoluteUri);return Task.FromResult(Reply(Requests.Count)); }
    }
    internal static int Run()
    {
        int count=0;void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
        string work=Path.Combine(Path.GetTempPath(),"MouseDock-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
        byte[] good=Encoding.UTF8.GetBytes("verified package");
        var info=new UpdateService.UpdateInfo {AssetName="MouseDock.exe",AssetUrl="https://github.com/late-autumn-gif/mousedock/releases/download/v9.0.0/MouseDock.exe",AssetSize=good.Length,Sha256=Convert.ToHexString(SHA256.HashData(good))};
        HttpResponseMessage Bytes(byte[] bytes)=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};
        bool Download(Handler handler,string name)
        { using var client=new HttpClient(handler);return UpdateService.DownloadPackageAsync(info,Path.Combine(work,name),null,null,CancellationToken.None,client).GetAwaiter().GetResult(); }
        var direct=new Handler{Reply=_=>Bytes(good)};
        Check(Download(direct,"direct")&&direct.Requests.Count==1,"Successful direct download never contacts mirror");
        var fallback=new Handler{Reply=n=>n==1?new(HttpStatusCode.ServiceUnavailable):Bytes(good)};
        Check(Download(fallback,"fallback")&&fallback.Requests.Count==2&&fallback.Requests[1].StartsWith("https://gh-proxy.org/https://github.com/"),"Direct failure tries documented mirror");
        var corrupt=new Handler{Reply=n=>Bytes(n==1?new byte[good.Length]:good)};
        Check(Download(corrupt,"corrupt")&&File.ReadAllBytes(Path.Combine(work,"corrupt")).SequenceEqual(good),"Corrupt direct data is rejected before verified mirror succeeds");
        var bad=new Handler{Reply=_=>Bytes(new byte[good.Length])};
        Check(!Download(bad,"bad"),"Both hash mismatches cannot become an install");
        var shortBody=new Handler{Reply=_=>Bytes(new byte[1])};
        Check(!Download(shortBody,"short"),"Incomplete asset rejected");
        info.Sha256=null!;
        var noDigest=new Handler{Reply=_=>new(HttpStatusCode.ServiceUnavailable)};
        Check(!Download(noDigest,"no-digest")&&noDigest.Requests.Count==1,"Mirror unavailable without trusted GitHub digest");
        info.AssetUrl="https://example.com/MouseDock.exe";
        var wrong=new Handler{Reply=_=>Bytes(good)};
        Check(!Download(wrong,"wrong")&&wrong.Requests.Count==0,"Caller cannot bypass release URL boundary");

        string zip=Path.Combine(work,"nested.zip");
        using(var z=ZipFile.Open(zip,ZipArchiveMode.Create))
        {
            using(var w=new StreamWriter(z.CreateEntry("payload/MouseDock.exe").Open()))w.Write("new-exe");
            using(var w=new StreamWriter(z.CreateEntry("payload/Assets/icon.txt").Open()))w.Write("new-asset");
        }
        string prepared=UpdateService.PreparePackage(zip,Path.Combine(work,"x"));
        Check(File.Exists(prepared)&&File.ReadAllText(Path.Combine(Path.GetDirectoryName(prepared)!,"Assets/icon.txt"))=="new-asset","Nested ZIP preserves companion assets");
        string duplicate=Path.Combine(work,"duplicate.zip");
        using(var z=ZipFile.Open(duplicate,ZipArchiveMode.Create)){z.CreateEntry("a/MouseDock.exe");z.CreateEntry("b/MouseDock.exe");}
        bool rejected=false;try{UpdateService.PreparePackage(duplicate,Path.Combine(work,"duplicate"));}catch(InvalidDataException){rejected=true;}
        Check(rejected,"Ambiguous multiple executables rejected");
        string traversal=Path.Combine(work,"traversal.zip");
        using(var z=ZipFile.Open(traversal,ZipArchiveMode.Create))z.CreateEntry("../escape.txt");
        rejected=false;try{UpdateService.PreparePackage(traversal,Path.Combine(work,"safe"));}catch(IOException){rejected=true;}
        Check(rejected&&!File.Exists(Path.Combine(work,"escape.txt")),"ZIP traversal cannot escape extraction root");

        // Real Windows PowerShell helper, dummy files and restart=false only.
        // All test staging is intentionally retained per the user's instruction.
        foreach(bool fail in new[]{false,true})
        {
            string stage=Path.Combine(Path.GetTempPath(),"MouseDock-update-"+Guid.NewGuid().ToString("N"));
            string source=Path.Combine(stage,"x");Directory.CreateDirectory(source);
            string install=Path.Combine(Path.GetTempPath(),"MouseDock-install-test-"+Guid.NewGuid().ToString("N"),"带空格 [test] & user's");Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(source,"MouseDock.exe"),"new-program");
            File.WriteAllText(Path.Combine(source,"a.txt"),"new-companion");
            Directory.CreateDirectory(Path.Combine(source,"Assets"));File.WriteAllText(Path.Combine(source,"Assets/icon.txt"),"new-icon");
            File.WriteAllText(Path.Combine(install,"MouseDock.exe"),"old-program");File.WriteAllText(Path.Combine(install,"a.txt"),"old-companion");
            File.WriteAllText(Path.Combine(install,"personal.txt"),"keep-me");
            if(fail){File.WriteAllText(Path.Combine(source,"zblocked.dll"),"blocked");Directory.CreateDirectory(Path.Combine(install,"zblocked.dll"));}
            var start=UpdateService.CreateApplyStartInfo(stage,Path.Combine(source,"MouseDock.exe"),Path.Combine(install,"MouseDock.exe"),0,restart:false);
            Check(start.CreateNoWindow&&start.WindowStyle==ProcessWindowStyle.Hidden,"Installer helper does not open a console");
            using var process=Process.Start(start)!;
            Check(process.WaitForExit(30000),"Installer helper finishes within test deadline");
            Check(process.ExitCode==(fail?1:0),"Installer success/failure exit status");
            Check(File.ReadAllText(Path.Combine(install,"MouseDock.exe"))==(fail?"old-program":"new-program"),"Program replaced or old program retained on failure");
            Check(File.ReadAllText(Path.Combine(install,"a.txt"))==(fail?"old-companion":"new-companion"),"Companion file replaced or rolled back");
            Check(File.ReadAllText(Path.Combine(install,"personal.txt"))=="keep-me","Unshipped personal files preserved");
            Check(Directory.Exists(stage)&&File.Exists(Path.Combine(stage,"backup/a.txt")),"Staging and original backups retained; rollback actually exercised");
            if(!fail)Check(File.ReadAllText(Path.Combine(install,"Assets/icon.txt"))=="new-icon","Nested package assets installed");
            else Check(File.Exists(Path.Combine(stage,"apply.log")),"Failed install retains diagnostic log");
        }
        return count;
    }
}
