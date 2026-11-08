using System;
using System.Windows;
using System.Windows.Media;
using MouseDock.Controls;
using MouseDock.Models;

/// <summary>
/// 任务栏与屏幕右侧共用同一套卡片，这里锁定它的几何约定。
///
/// 来源：合作者 PR #9 的 TaskbarRegressionChecks，按当前 TaskbarBatteryStrip 重写。
/// 旧测试断言的是带型号名的三行卡；现在型号名进了 ToolTip，卡片只剩图标与数字，
/// 所以改测「尺寸按方向切换」「竖排留出上下间隔」这两件事。
/// </summary>
internal static class TaskbarRegressionChecks
{
    internal static void Run()
    {
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        var horizontal = TaskbarBatteryStrip.CardSize(false);
        var vertical = TaskbarBatteryStrip.CardSize(true);

        Check(horizontal.Width > horizontal.Height,
            "横向卡片必须宽扁，否则图标和数字挤在一行里。");
        Check(vertical.Height > vertical.Width,
            "竖向卡片必须窄高，否则上下排列没有空间。");
        Check(vertical.Height >= 60,
            "竖向卡片至少 60 高：图标 24 加数字 22 再加间隔，低于这个数一定贴在一起。");
        Check(Math.Abs(horizontal.Height - vertical.Width) < 0.01,
            "两个方向的短边应当一致，换方向时视觉宽度才稳定。");

        // 样式名归一化：未知值必须回落到 ring，不能返回空串。
        Check(TaskbarBatteryStrip.NormalizeStyle("card") == "card", "已知样式必须原样返回。");
        Check(TaskbarBatteryStrip.NormalizeStyle("nonsense") == "ring", "未知样式必须回落到 ring。");
        Check(TaskbarBatteryStrip.NormalizeStyle(null) == "ring", "空样式必须回落到 ring。");

        // 每个方向都要能列出可用样式，且数量一致。
        var flat = System.Linq.Enumerable.ToList(TaskbarBatteryStrip.Styles(false));
        var tall = System.Linq.Enumerable.ToList(TaskbarBatteryStrip.Styles(true));
        Check(flat.Count == tall.Count, "两个方向的样式数量必须一致。");
        Check(flat.Count >= 6, "样式至少有六种。");
        Check(flat.Exists(s => s.Id == "pill") && tall.Exists(s => s.Id == "pill"),
            "胶囊样式在两个方向都要可选。");

        // 卡片容器能按设备数产出，编号从 1 起算。
        var device = new DeviceInfo
        {
            Id = "regression-card",
            Name = "ROG 游侠2 RX 98 无线版",
            Level = 65,
            Connected = true,
            Kind = DeviceKind.Keyboard
        };
        var strip = new TaskbarBatteryStrip(new[] { device }, "card", true, false,
            Brushes.White, 20, "", "#FFFFFF");
        Check(strip.DeviceCount == 1, "一台设备应当产出一张卡片。");
        Check(strip.StyleName == "card", "样式名必须反映传入值。");

        var two = new TaskbarBatteryStrip(new[] { device, device }, "pill", true, true,
            Brushes.White, 20, "", "#FFFFFF");
        Check(two.DeviceCount == 2, "两台设备应当产出两张卡片。");
        Check(two.StyleName == "pill", "样式名必须是 pill。");
    }
}

