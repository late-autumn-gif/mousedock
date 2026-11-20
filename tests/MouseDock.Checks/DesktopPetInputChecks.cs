using System;
using System.Collections.Generic;
using System.Diagnostics;
using MouseDock.Services;

/// <summary>
/// 桌前键盘桌宠的输入引擎回归检查：可视化键盘布局、输入状态模型、全局低级钩子生命周期。
/// 只注入虚构标签的离线事件；键盘内容不写日志、不落盘。
/// </summary>
internal static class DesktopPetInputChecks
{
    private const double LayoutWidth = 10.5; // QWERTYUIOP 行右端：.5 + 10

    // Q B D K U X：标准指法里分属左右手的六个键。
    private static readonly int[] TypedKeys = [0x51, 0x42, 0x44, 0x4B, 0x55, 0x58];

    internal static void Layout()
    {
        var keys = PetKeyboardLayout.Keys;
        Require(keys.Count == 37, $"可视化键盘应有 37 个键，实际 {keys.Count}。");

        var rowCounts = new Dictionary<int, int>();
        for (int i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            Require(key.Row is >= 0 and <= 4, $"键 {key.Label} 的行号 {key.Row} 超出五行键盘。");
            Require(key.Width > 0, $"键 {key.Label} 的宽度必须为正。");
            Require(key.Offset >= 0 && key.Offset + key.Width <= LayoutWidth + 1e-9,
                $"键 {key.Label} 的 offset+width 越出键盘宽度（{key.Offset}+{key.Width}>{LayoutWidth}）。");
            Require(!string.IsNullOrEmpty(key.Label), "可视化键盘上的每个键都必须有标签。");
            Require(PetKeyboardLayout.IndexOf(key.VirtualKey) == i,
                $"键 {key.Label} 的 IndexOf 结果与槽位 {i} 不一致。");
            rowCounts[key.Row] = rowCounts.GetValueOrDefault(key.Row) + 1;
        }
        Require(rowCounts.Count == 5, $"可视化键盘应正好 5 行，实际 {rowCounts.Count} 行。");
        int[] expected = [10, 10, 9, 7, 1];
        for (int row = 0; row < expected.Length; row++)
            Require(rowCounts[row] == expected[row], $"第 {row} 行应有 {expected[row]} 个键，实际 {rowCounts[row]}。");

        Require(keys[0].Offset == 0, "数字行行偏移应为 0。");
        Require(keys[10].Offset == .5, "Q 行行偏移应为 .5。");
        Require(keys[20].Offset == .75, "A 行行偏移应为 .75。");
        Require(keys[29].Offset == 1.25, "Z 行行偏移应为 1.25。");
        Require(keys[36].Offset == 2.5 && keys[36].Width == 5, "空格应位于 2.5 且宽 5。");

        CheckKey(0x51, "Q", 1, .5, leftHand: true);
        CheckKey(0x42, "B", 3, 5.25, leftHand: true);
        CheckKey(0x44, "D", 2, 2.75, leftHand: true);
        CheckKey(0x4B, "K", 2, 7.75, leftHand: false);
        CheckKey(0x55, "U", 1, 6.5, leftHand: false);
        CheckKey(0x58, "X", 3, 2.25, leftHand: true);

        Require(PetKeyboardLayout.IndexOf(0x70) == -1, "没有绘制的 F1 应返回槽位 -1。");
        Require(PetKeyboardLayout.IndexOf(0x09) == -1, "没有绘制的 Tab 应返回槽位 -1。");
        Require(!PetKeyboardLayout.IsLeftHand(0x70), "未绘制的键不应被当成左手键。");

        Require(PetKeyboardLayout.LabelOf(0x51, 0x10) == "Q", "字母 Q 的标签应为 Q。");
        Require(PetKeyboardLayout.LabelOf(0x31, 0x02) == "1", "数字 1 的标签应为 1。");
        Require(PetKeyboardLayout.LabelOf(0x20, 0x39) == "空格", "空格的标签应为“空格”。");
        Require(PetKeyboardLayout.LabelOf(0x0D, 0x1C) == "回车", "回车的标签应为“回车”。");
        Require(PetKeyboardLayout.LabelOf(0x08, 0x0E) == "退格", "退格的标签应为“退格”。");
        Require(PetKeyboardLayout.LabelOf(0x70, 0x3B) == "F1", "F1 的标签应为 F1。");
        Require(PetKeyboardLayout.LabelOf(0x87, 0x64) == "F24", "F24 的标签应为 F24。");
    }

    internal static void Tracker()
    {
        long now = Environment.TickCount64;
        var state = new DesktopPetInputState();

        // 依次按下 Q B D K U X：轨迹保持最旧→最新。
        for (int i = 0; i < TypedKeys.Length; i++) state.Push(KeyCue(TypedKeys[i], now + i * 10));
        Require(state.Trail.Count == 6, $"轨迹应收集 6 个按键，实际 {state.Trail.Count}。");
        string order = "";
        foreach (var pulse in state.Trail) order += pulse.Label;
        Require(order == "QBDKUX", $"轨迹顺序应为 QBDKUX，实际 {order}。");
        Require(state.LastKey is { } last && last.Label == "X" && last.VirtualKey == 0x58 &&
            last.Slot == PetKeyboardLayout.IndexOf(0x58) && last.LeftHand,
            "最近按键应为左手 X，且槽位与布局一致。");
        Require(state.Burst > 1, "连续快速按键应累计连击数。");
        Require(state.Typing(now + 500), "最近按键 500ms 内应判定为正在打字。");
        Require(!state.Typing(now + 1500), "最近按键 1500ms 后不应再判定为正在打字。");

        // 轨迹上限 8：只保留最新的 8 个按键。
        state.Reset();
        for (int i = 0; i < 10; i++) state.Push(LabelCue("键" + i, 0x41, now + i * 10));
        Require(state.Trail.Count == 8, $"轨迹上限应为 8，实际 {state.Trail.Count}。");
        Require(state.Trail[0].Label == "键2" && state.Trail[7].Label == "键9",
            "轨迹超过上限时应丢弃最旧的按键。");
        Require(state.LastKey is { } newest && newest.Label == "键9", "最近按键应为最后注入的那个。");

        // Prune：未过期的保留，过期的清除。
        state.Reset();
        state.Push(KeyCue(0x51, now));
        state.Prune(now + 300);
        Require(state.Trail.Count == 1 && state.LastKey != null, "未过期的按键不应被 Prune 清除。");
        state.Prune(now + 2000);
        Require(state.Trail.Count == 0 && state.LastKey == null && state.Burst == 0,
            "过期的按键轨迹、最近按键与连击数都应被 Prune 清除。");
        Require(!state.Typing(now + 2000), "Prune 之后不应再判定为正在打字。");

        // 鼠标左/右键状态与过期。
        state.Reset();
        state.Push(MouseCue(PetInputKind.MouseLeft, now));
        Require(state.LastMouse is { } left && left.Button == PetInputKind.MouseLeft,
            "鼠标左键应更新最近鼠标状态。");
        state.Push(MouseCue(PetInputKind.MouseRight, now + 10));
        Require(state.LastMouse is { } right && right.Button == PetInputKind.MouseRight,
            "鼠标右键应覆盖最近鼠标状态。");
        state.Prune(now + 10 + 2000);
        Require(state.LastMouse == null, "过期的鼠标按键应被 Prune 清除。");
        Require(state.Trail.Count == 0, "鼠标事件不应进入键盘轨迹。");

        // 按住/松开：按下保持、松开清除，松开事件丢失时 30s 自愈。
        state.Reset();
        state.Push(MouseCue(PetInputKind.MouseLeft, now));
        Require(state.HeldButton == PetInputKind.MouseLeft, "按下鼠标后应保持按住状态。");
        Require(state.InputActive(now), "按住鼠标应让输入保持活跃。");
        state.Prune(now + 31000);
        Require(state.HeldButton == null, "松开事件丢失超过 30s 时应自愈释放鼠标。");
        state.Push(MouseCue(PetInputKind.MouseRight, now + 32000));
        state.Push(MouseCue(PetInputKind.MouseRightUp, now + 32010));
        Require(state.HeldButton == null && state.MouseReleaseAt == now + 32010,
            "松开鼠标应清除按住状态并记录松开时间。");
        Require(state.InputActive(now + 32020), "刚松开后的鼠标点击仍应算作输入活跃。");

        // Reset 清空全部状态。
        state.Push(KeyCue(0x51, now));
        state.Push(MouseCue(PetInputKind.MouseMiddle, now));
        state.Reset();
        Require(state.Trail.Count == 0 && state.LastKey == null && state.LastMouse == null && state.Burst == 0,
            "Reset 必须清空轨迹、最近按键、最近鼠标与连击数。");
        Require(state.HeldButton == null && state.Trail.Count == 0, "Reset 必须清空鼠标按住状态。");
        Require(!state.Typing(now), "Reset 之后不应判定为正在打字。");
    }

    /// <summary>
    /// 返回 null 表示钩子真实安装成功并通过全部生命周期校验；返回字符串表示当前桌面会话
    /// 无法安装全局低级钩子（检查记为 SKIP，绝不假通过）。
    /// </summary>
    internal static string? HookLifecycle()
    {
        using var hook = new DesktopPetInputHook();
        var buffer = new PetInputCue[64];
        long stamp = Environment.TickCount64;

        // 未启动时队列也必须可用：最旧优先、非阻塞、上限 256 丢最旧。
        Require(!hook.Running, "新建的钩子不应处于运行状态。");
        hook.PushForTest(LabelCue("虚构甲", 0x41, stamp));
        hook.PushForTest(LabelCue("虚构乙", 0x42, stamp + 1));
        int drained = hook.Drain(buffer);
        Require(drained == 2 && buffer[0].Label == "虚构甲" && buffer[1].Label == "虚构乙",
            "Drain 必须最旧优先地取回注入事件。");
        Require(hook.Drain(buffer) == 0, "空队列的 Drain 必须立即返回 0。");

        for (int i = 0; i < 300; i++) hook.PushForTest(LabelCue("队列" + i, 0x41, stamp + i));
        int total = 0;
        string firstLabel = "";
        bool captured = false;
        while (true)
        {
            int count = hook.Drain(buffer);
            if (count == 0) break;
            if (!captured) { firstLabel = buffer[0].Label; captured = true; }
            total += count;
        }
        Require(total == 256, $"输入队列上限应为 256，实际取回 {total} 条。");
        Require(firstLabel == "队列44", $"队列溢出时应丢弃最旧事件，实际最旧为 {firstLabel}。");

        hook.Start();
        Require(hook.Running, "Start() 之后 Running 必须为 true。");
        bool keyboardHooked = hook.KeyboardHooked;
        bool mouseHooked = hook.MouseHooked;
        int hookError = hook.LastError;

        hook.SuppressMouse = true;
        Require(hook.SuppressMouse, "SuppressMouse 必须可写可读。");
        hook.SuppressMouse = false;

        hook.PushForTest(LabelCue("注入键", 0x51, Environment.TickCount64));
        int injected = hook.Drain(buffer);
        bool found = false;
        for (int i = 0; i < injected; i++)
            if (buffer[i].Label == "注入键") found = true;
        Require(found, "运行中的钩子必须能通过同一条队列取回注入事件。");

        // 连续 Start/Stop 三次不得挂起（Stop 内部 Join 上限 2s）。
        var watch = Stopwatch.StartNew();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            hook.Stop();
            Require(!hook.Running, $"第 {cycle + 1} 次 Stop 后 Running 必须为 false。");
            Require(!hook.KeyboardHooked && !hook.MouseHooked,
                $"第 {cycle + 1} 次 Stop 后钩子标记必须清零。");
            hook.Start();
            Require(hook.Running, $"第 {cycle + 1} 次重新 Start 后 Running 必须为 true。");
        }
        watch.Stop();
        Require(watch.ElapsedMilliseconds < 4000,
            $"三次 Start/Stop 用时 {watch.ElapsedMilliseconds}ms，超过 4s 上限。");

        hook.Stop();
        hook.Dispose();
        Require(!hook.Running, "Dispose 之后 Running 必须为 false。");
        Require(!hook.KeyboardHooked && !hook.MouseHooked, "Dispose 之后两个钩子都必须已卸载。");
        hook.Dispose(); // 重复释放不得抛异常。
        Require(hook.Drain(buffer) == 0, "释放之后 Drain 必须立即返回 0。");

        if (keyboardHooked && mouseHooked) return null;
        return $"当前桌面会话无法安装全局低级钩子（KeyboardHooked={keyboardHooked}, MouseHooked={mouseHooked}, LastError={hookError}），真实钩子校验跳过。";
    }

    private static void CheckKey(int virtualKey, string label, int row, double offset, bool leftHand)
    {
        int slot = PetKeyboardLayout.IndexOf(virtualKey);
        Require(slot >= 0, $"键 {label} 不在可视化键盘上。");
        var key = PetKeyboardLayout.Keys[slot];
        Require(key.VirtualKey == virtualKey && key.Label == label && key.Row == row &&
            Math.Abs(key.Offset - offset) < 1e-9 && Math.Abs(key.Width - 1) < 1e-9 && key.LeftHand == leftHand,
            $"键 {label} 的位置或左右手不符合标准指法：slot={slot}, row={key.Row}, offset={key.Offset}, width={key.Width}, left={key.LeftHand}。");
        Require(PetKeyboardLayout.IsLeftHand(virtualKey) == leftHand, $"键 {label} 的 IsLeftHand 与布局不一致。");
    }

    private static PetInputCue KeyCue(int virtualKey, long at) =>
        new(PetInputKind.Key, virtualKey, 0, PetKeyboardLayout.LabelOf(virtualKey, 0), at);

    private static PetInputCue LabelCue(string label, int virtualKey, long at) =>
        new(PetInputKind.Key, virtualKey, 0, label, at);

    private static PetInputCue MouseCue(PetInputKind kind, long at) => new(kind, 0, 0, "", at);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
