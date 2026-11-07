using System.Text.Json;
using MouseDock.Services;
using MouseDock.Models;
internal static class UpdateReleaseRegression
{
    internal static int Run()
    {
        int count=0;void Check(bool value,string why){count++;if(!value)throw new Exception(why);}
        object Asset(string name,string url="",int size=123)=>new {name,browser_download_url=url==""?"https://github.com/late-autumn-gif/mousedock/releases/download/v1.1.1/"+name:url,size,digest="sha256:"+new string('a',64)};
        string Release(string tag,object[] assets,bool draft=false,bool prerelease=false)=>JsonSerializer.Serialize(new {tag_name=tag,assets,draft,prerelease,body="notes"});
        string json=Release("v1.1.1",new[]{Asset("MouseDock-v1.1.1-source.zip"),Asset("MouseDock.exe"),Asset("MouseDock-v1.1.1-win-x64.zip"),Asset("MouseDock-audit.zip")});
        Check(UpdateService.ParseRelease(json,"1.1.0")?.AssetName=="MouseDock-v1.1.1-win-x64.zip","Prefer exact x64 program package, never first source ZIP");
        Check(UpdateService.ParseRelease(json,"1.1.0")?.Sha256==new string('a',64),"Trusted GitHub digest propagated to downloader");
        Check(UpdateService.ParseRelease(json,"1.1.1")==null,"Do not update same version");
        Check(UpdateService.ParseRelease(json,"1.4.4")==null,"Do not silently downgrade old users");
        Check(UpdateService.ParseRelease(Release("v1.2.0",new[]{Asset("MouseDock.exe")}),"1.1.1")!=null,"Future stable versions are no longer blocked at1.2");
        Check(UpdateService.ParseRelease(Release("v2.0.0",new[]{Asset("MouseDock.exe")}),"1.1.1")!=null,"Future major versions compare normally");
        foreach(var name in new[]{"MouseDock-source.zip","MouseDock-v1.1.1-source.zip","MouseDock-v1.1.1-win-arm64.zip","MouseDock-audit.zip"})
            Check(UpdateService.ParseRelease(Release("v1.1.1",new[]{Asset(name)}),"1.1.0")==null,"Non-program package is rejected");
        foreach(var url in new[]{"http://github.com/late-autumn-gif/mousedock/releases/download/v1.1.1/MouseDock.exe","https://example.com/MouseDock.exe","https://github.com/another/repo/releases/download/v1/MouseDock.exe"})
            Check(UpdateService.ParseRelease(Release("v1.1.1",new[]{Asset("MouseDock.exe",url)}),"1.1.0")==null,"Only this repository HTTPS release assets");
        Check(UpdateService.ParseRelease(Release("v1.1.1",new[]{Asset("MouseDock.exe",size:0)}),"1.1.0")==null,"Empty asset rejected");
        Check(UpdateService.ParseRelease(Release("v1.1.1",new[]{Asset("MouseDock.exe")},draft:true),"1.1.0")==null,"Draft ignored");
        Check(UpdateService.ParseRelease(Release("v1.1.1",new[]{Asset("MouseDock.exe")},prerelease:true),"1.1.0")==null,"Prerelease ignored");
        foreach(var bad in new[]{"{}","garbage",Release("vunknown",new[]{Asset("MouseDock.exe")})})Check(UpdateService.ParseRelease(bad,"1.1.0")==null,"Malformed data never crashes startup");
        Check(UpdateService.CurrentVersionText=="1.1.2","Use application assembly version, not test/host process");
        var settings=new AppSettings{Version=6,AutoUpdate=false};
        Check(SettingsStore.RestoreStartupUpdates(settings)&&settings.AutoUpdate&&settings.Version==7,"Renumbered old settings re-enable startup checks");
        settings.AutoUpdate=false;
        Check(!SettingsStore.RestoreStartupUpdates(settings)&&!settings.AutoUpdate,"Respect a later explicit opt-out");
        return count;
    }
}
