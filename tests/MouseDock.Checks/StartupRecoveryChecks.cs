using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using MouseDock.Services;
using MouseDock.Services.Detection;

/// <summary>
/// 开机自启后的恢复能力。
///
/// 来源：合作者 PR #9 的 StartupRecoveryChecks，按当前实现重写。
/// BluetoothStageChecks 另行覆盖有界等待、迟到结果与识别偏好变化；这里保留原有保证：
/// 1. 蓝牙任何一个阶段抛异常都不能把整轮扫描带崩；
/// 2. 任务栏锚点在拿到和拿不到任务栏时都要安全返回，不能抛。
/// 这两条坏了，开机后任务栏卡片就恢复不出来——正是 KNOWN_ISSUES 里那条待验项。
/// </summary>
internal static class StartupRecoveryChecks
{
    internal static void Run()
    {
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        // 一、蓝牙阶段吞异常
        var safeAsync = typeof(BluetoothDiscovery).GetMethod("SafeAsync",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (safeAsync == null) throw new Exception("BluetoothDiscovery.SafeAsync 必须存在，否则无法验证蓝牙阶段的异常隔离。");

        bool threw = false;
        try
        {
            var faulted = (Task)safeAsync.Invoke(null, new object?[]
            {
                new Func<Task<List<DetectedDevice>>>(() => throw new InvalidOperationException("stage fixture")),
                "startup-fixture", 5000, null, false
            })!;
            faulted.GetAwaiter().GetResult();
        }
        catch { threw = true; }
        Check(!threw, "蓝牙阶段抛异常时不得冒泡，否则开机后整轮扫描会中断。");

        // 挂起但最终会完成的阶段必须正常收尾
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? stalling = null;
        try
        {
            stalling = (Task)safeAsync.Invoke(null, new object?[]
            {
                new Func<Task<List<DetectedDevice>>>(async () => { await release.Task; return []; }),
                "startup-fixture", 5000, null, false
            })!;
            Check(!stalling.IsCompleted, "未完成的阶段不应提前返回。");
        }
        finally
        {
            release.TrySetResult();
            stalling?.Wait(2000);
        }
        Check(stalling == null || stalling.IsCompletedSuccessfully,
            "阶段完成后必须正常结束，不能一直挂着。");

        // 二、任务栏锚点安全返回
        bool anchorOk;
        try
        {
            anchorOk = TaskbarAnchor.TryGet(out Rect bounds, out uint edge, out double dpi);
            if (anchorOk)
            {
                Check(bounds.Width > 0 && bounds.Height > 0,
                    "拿到任务栏时必须给出非零尺寸，否则卡片没有可放置的区域。");
                Check(dpi > 0, "DPI 必须为正数，否则窗口缩放会算错。");
            }
        }
        catch (Exception ex)
        {
            throw new Exception("TaskbarAnchor.TryGet 不得抛异常，实际抛出：" + ex.GetType().Name);
        }

        // 三、陈旧标记驱动变暗而不是写字
        var device = new MouseDock.Models.DeviceInfo
        {
            Id = "startup-fixture",
            Name = "回归用设备",
            Level = 42,
            Connected = true,
            IsStale = false
        };
        Check(Math.Abs(device.ValueOpacity - 1.0) < 0.001,
            "新鲜读数必须完全不透明。");
        device.IsStale = true;
        Check(device.ValueOpacity < 0.6,
            "陈旧读数必须明显压暗，让休眠状态一眼可见。");
        device.IsStale = false;
        Check(Math.Abs(device.ValueOpacity - 1.0) < 0.001,
            "刷新到新电量后必须恢复到完全不透明。");

        _ = anchorOk;
    }
}
