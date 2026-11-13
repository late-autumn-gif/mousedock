using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MouseDock.Models;
using MouseDock.Services.Detection;

namespace MouseDock.Services;

internal interface IDpiBackend
{
    DpiSnapshot Read(string instance,ushort vid,ushort pid,Action guard);
    DpiSnapshot Apply(DpiSnapshot before,int stage,int? value,Action guard);
}

internal sealed class WindowsDpiBackend : IDpiBackend
{
    private static DpiConnection Resolve(string instance,ushort vid,ushort pid) =>
        DpiConnection.Resolve(instance,vid,pid,HidDiscovery.EnumerateDpiChannels(vid,pid));
    public DpiSnapshot Read(string instance,ushort vid,ushort pid,Action guard) => HidDiscovery.RunDpiOperation(instance,()=>
    {
        guard();var connection=Resolve(instance,vid,pid);
        using var link=new DpiNativeLink(connection,guard);
        var state=DpiProtocol.Read(connection.Profile,link);guard();
        if(Resolve(instance,vid,pid).Key!=connection.Key)throw new IOException("DPI 接口在读取时变化，请重新读取");
        return state.Bind(instance,connection.Key);
    });
    public DpiSnapshot Apply(DpiSnapshot before,int stage,int? value,Action guard) => HidDiscovery.RunDpiOperation(before.InstanceId,()=>
    {
        guard();var connection=Resolve(before.InstanceId,before.Profile.Vid,before.Profile.Pid);
        if(connection.Key!=before.ConnectionKey)throw new IOException("接收器或控制接口已变化，请重新读取");
        using var link=new DpiNativeLink(connection,guard);
        var state=DpiProtocol.Apply(connection.Profile,link,before,stage,value,guard);guard();
        if(Resolve(before.InstanceId,before.Profile.Vid,before.Profile.Pid).Key!=connection.Key)
            throw new IOException("设置后的连接已变化，未确认生效；请重新读取");
        return state.Bind(before.InstanceId,connection.Key);
    });
}

/// <summary>Explicit background DPI operations, bound to an enabled physical device and a fresh snapshot.</summary>
public sealed class DpiService
{
    private static readonly object pendingGate=new();
    private static readonly Dictionary<string,Task> pending=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,(DeviceInfo Device,DpiSnapshot Snapshot)> latest=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,long> generations=new(StringComparer.OrdinalIgnoreCase);
    private readonly IDpiBackend backend;
    private readonly Func<bool> safeMode;
    private readonly Func<DevicePreferences> preferences;
    private readonly int timeoutMs;
    public DpiService(Func<bool> safeMode,Func<DevicePreferences> preferences)
        :this(new WindowsDpiBackend(),safeMode,preferences,30000) { }
    internal DpiService(IDpiBackend backend,Func<bool> safeMode,Func<DevicePreferences> preferences,int timeoutMs)
    {this.backend=backend;this.safeMode=safeMode;this.preferences=preferences;this.timeoutMs=timeoutMs;}

    private static bool Identity(DeviceInfo device,out string instance,out ushort vid,out ushort pid)
    {
        instance=device?.Id;vid=pid=0;
        if(device==null||device.Kind!=DeviceKind.Mouse||instance==null||!instance.StartsWith("instance-",StringComparison.OrdinalIgnoreCase))return false;
        string[] pair=(device.Detail??"").Split(':');
        return pair.Length==2&&pair[0].Length==4&&pair[1].Length==4&&
            ushort.TryParse(pair[0],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out vid)&&
            ushort.TryParse(pair[1],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out pid);
    }
    private static bool FreshScan(DateTimeOffset capturedAt) =>
        capturedAt!=default&&capturedAt<=DateTimeOffset.UtcNow&&DateTimeOffset.UtcNow-capturedAt<=TimeSpan.FromSeconds(90);
    private static bool ReadOnlyMchose(DeviceInfo device,ushort vid,ushort pid) =>
        device.Dpi is >=50 and <=32000&&FreshScan(device.DpiCapturedAt)&&
        (VendorProtocol.IsMchoseA7V2(vid,pid)||VendorProtocol.IsMchoseG7(vid,pid,device.RawName));
    public bool Supports(DeviceInfo device) => Identity(device,out _,out ushort vid,out ushort pid)&&
        (DpiProfiles.Native(vid,pid)!=null||ReadOnlyMchose(device,vid,pid));
    public void Invalidate(string instanceId)
    {
        instanceId??="";
        lock(latest){latest.Remove(instanceId);generations[instanceId]=generations.GetValueOrDefault(instanceId)+1;}
    }

    public Task<DpiSnapshot> ReadAsync(DeviceInfo device,CancellationToken cancellationToken=default)
    {
        if(!Identity(device,out string instance,out ushort vid,out ushort pid)||
            DpiProfiles.Native(vid,pid)==null&&!ReadOnlyMchose(device,vid,pid))
            throw new InvalidOperationException("此设备或连接方式尚未适配 DPI");
        if(DpiProfiles.Native(vid,pid)==null)return Run(device,instance,vid,pid,guard=>
        {
            guard();
            if(!ReadOnlyMchose(device,vid,pid))throw new InvalidOperationException("最近扫描未提供有效的只读 DPI");
            int value=device.Dpi;var capturedAt=device.DpiCapturedAt;
            if(!FreshScan(capturedAt)||value is <50 or >32000)throw new InvalidOperationException("扫描 DPI 已失效，请等待新的电量扫描");
            return new DpiSnapshot(new DpiProfile(vid,pid,0,0,device.Name,DpiFamily.MchoseReadOnly,WriteApproved:false),
                0,new[]{new DpiStage(value,value)},"current-only",50,32000,1,false,
                "电量扫描提供的只读 DPI；尚未取得可修改的档位配置",instanceId:instance,
                connectionKey:"scan-only",capturedAt:capturedAt);
        },cancellationToken);
        return Run(device,instance,vid,pid,guard=>backend.Read(instance,vid,pid,guard),cancellationToken);
    }
    public Task<DpiSnapshot> ApplyAsync(DpiSnapshot before,int stage,int? value,CancellationToken cancellationToken=default)
    {
        DeviceInfo device;
        lock(latest)
        {
            if(before==null||!latest.TryGetValue(before.InstanceId,out var saved)||!ReferenceEquals(saved.Snapshot,before))
                throw new InvalidOperationException("DPI 读数已失效，请重新读取");
            device=saved.Device;
        }
        if(DateTimeOffset.UtcNow-before.CapturedAt>TimeSpan.FromSeconds(90))throw new InvalidOperationException("DPI 读数已过期，请重新读取");
        before.Target(stage,value);
        return Run(device,before.InstanceId,before.Profile.Vid,before.Profile.Pid,
            guard=>backend.Apply(before,stage,value,guard),cancellationToken,before);
    }
    private async Task<DpiSnapshot> Run(DeviceInfo device,string instance,ushort vid,ushort pid,
        Func<Action,DpiSnapshot> operation,CancellationToken cancellationToken,DpiSnapshot required=null)
    {
        var stop=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);var token=stop.Token;
        var prefs=preferences();int policyGeneration=prefs?.Generation??0;
        long generation=0;
        string[] keys=DevicePreferences.Keys(device).Append($"hid-{vid:X4}-{pid:X4}").ToArray();
        void Guard()
        {
            token.ThrowIfCancellationRequested();
            lock(latest)if(generations.GetValueOrDefault(instance)!=generation)
                throw new InvalidOperationException("DPI 操作已失效，请重新读取");
            if(safeMode()||!device.Connected||!device.RecognitionEnabled||device.Id!=instance||
                preferences()!=prefs||(prefs?.Generation??0)!=policyGeneration||!(prefs?.CanRead(keys)??true))
                throw new InvalidOperationException("设备连接或识别设置已变化，DPI 操作已停止");
        }
        Task<DpiSnapshot> worker;
        lock(pendingGate)
        {
            if(pending.TryGetValue(instance,out var old)&&!old.IsCompleted)
            {stop.Dispose();throw new InvalidOperationException("上一项 DPI 通信尚未结束，请稍后重新读取");}
            foreach(string key in pending.Where(p=>p.Value.IsCompleted).Select(p=>p.Key).ToArray())pending.Remove(key);
            if(pending.Count>=4){stop.Dispose();throw new InvalidOperationException("DPI 通信正在等待，请稍后重新读取");}
            lock(latest)
            {
                if(required!=null&&(!latest.TryGetValue(instance,out var saved)||!ReferenceEquals(saved.Snapshot,required)))
                {stop.Dispose();throw new InvalidOperationException("DPI 读数已失效，请重新读取");}
                generation=generations.GetValueOrDefault(instance)+1;generations[instance]=generation;
                latest.Remove(instance);
            }
            worker=Task.Factory.StartNew(()=>{Guard();var result=operation(Guard);Guard();return result;},
                CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
            pending[instance]=worker;
        }
        _=worker.ContinueWith(done=>
        {
            _=done.Exception;
            lock(pendingGate)if(pending.TryGetValue(instance,out var current)&&ReferenceEquals(current,worker))pending.Remove(instance);
            stop.Dispose();
        },CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        try
        {
            if(await Task.WhenAny(worker,Task.Delay(timeoutMs,cancellationToken)).ConfigureAwait(false)!=worker)
            {
                try {stop.Cancel();}catch(ObjectDisposedException) { }
                throw new TimeoutException("DPI 通信超时或已取消；未确认设置，请稍后重新读取");
            }
            var result=await worker.ConfigureAwait(false);
            Guard();
            lock(latest)
            {
                if(generations.GetValueOrDefault(instance)!=generation)throw new InvalidOperationException("DPI 结果已失效，请重新读取");
                latest[instance]=(device,result);
            }
            return result;
        }
        catch
        {
            lock(latest)if(generations.GetValueOrDefault(instance)==generation)latest.Remove(instance);
            throw;
        }
    }
}
