using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace MouseDock.Services;

/// <summary>桌宠要跟随的输入类型：键盘按键与三种鼠标按键。</summary>
internal enum PetInputKind { Key, MouseLeft, MouseRight, MouseMiddle }

/// <summary>
/// 钩子线程投递给界面线程的单个输入事件。只存在于内存队列中，不写文件、不写日志。
/// 鼠标事件的 VirtualKey/ScanCode 没有意义（为 0），Label 为空；键盘事件带真实取值与可显示标签。
/// </summary>
internal readonly record struct PetInputCue(PetInputKind Kind, int VirtualKey, int ScanCode, string Label, long Timestamp);

/// <summary>
/// 桌前场景里那块可视化键盘的布局。Offset/Width 以键宽为单位，五行为 1-0 / QWERTYUIOP /
/// ASDFGHJKL / ZXCVBNM / 空格；行偏移固定为 0 / .5 / .75 / 1.25 / 2.5，空格宽 5。
/// 标准指法：每行左手负责前五个键，空格归左手侧。
/// </summary>
internal static class PetKeyboardLayout
{
    internal readonly record struct Key(int VirtualKey, string Label, int Row, double Offset, double Width, bool LeftHand);

    private static readonly Key[] Layout = BuildLayout();
    private static readonly int[] SlotByKey = BuildSlotIndex();

    [ThreadStatic] private static byte[] _keyState;
    [ThreadStatic] private static char[] _textBuffer;

    /// <summary>按渲染顺序排列的 37 个键（10 + 10 + 9 + 7 + 1），索引即桌宠场景的键槽位。</summary>
    internal static IReadOnlyList<Key> Keys => Layout;

    /// <summary>视觉键盘上的槽位；没有画出来的键返回 -1。</summary>
    internal static int IndexOf(int virtualKey) => (uint)virtualKey < 256u ? SlotByKey[virtualKey] : -1;

    /// <summary>该键由左手还是右手负责；未绘制的键返回 false。</summary>
    internal static bool IsLeftHand(int virtualKey)
    {
        int slot = IndexOf(virtualKey);
        return slot >= 0 && Layout[slot].LeftHand;
    }

    /// <summary>
    /// 按键的可显示标签。字母、数字直接给出字符；编辑/导航/功能键给出固定名称；
    /// 其余键用前台窗口键盘布局的 ToUnicodeEx 取值，失败再退回 MAPVK_VK_TO_CHAR，最后退回 0x 前缀。
    /// 这个函数可以被钩子线程直接调用，不含 IO、不写日志。
    /// </summary>
    internal static string LabelOf(int virtualKey, int scanCode)
    {
        if (virtualKey is (>= 'A' and <= 'Z') or (>= '0' and <= '9')) return ((char)virtualKey).ToString();
        switch (virtualKey)
        {
            case 0x20: return "空格";
            case 0x0D: return "回车";
            case 0x08: return "退格";
            case 0x09: return "Tab";
            case 0x1B: return "Esc";
            case 0x25: return "←";
            case 0x26: return "↑";
            case 0x27: return "→";
            case 0x28: return "↓";
            case 0x2E: return "Delete";
            case 0x24: return "Home";
            case 0x23: return "End";
            case 0x21: return "PgUp";
            case 0x22: return "PgDn";
        }
        if (virtualKey is >= 0x70 and <= 0x87) return "F" + (virtualKey - 0x6F);
        return UnicodeLabel(virtualKey, scanCode);
    }

    private static Key[] BuildLayout()
    {
        var keys = new List<Key>(37);
        AppendRow(keys, 0, 0, "1234567890");
        AppendRow(keys, 1, .5, "QWERTYUIOP");
        AppendRow(keys, 2, .75, "ASDFGHJKL");
        AppendRow(keys, 3, 1.25, "ZXCVBNM");
        keys.Add(new Key(0x20, "空格", 4, 2.5, 5, true));
        return keys.ToArray();
    }

    private static void AppendRow(List<Key> keys, int row, double offset, string labels)
    {
        for (int i = 0; i < labels.Length; i++)
            keys.Add(new Key(labels[i], labels[i].ToString(), row, offset + i, 1, i < 5));
    }

    private static int[] BuildSlotIndex()
    {
        var slots = new int[256];
        Array.Fill(slots, -1);
        for (int i = 0; i < Layout.Length; i++) slots[Layout[i].VirtualKey] = i;
        return slots;
    }

    private static string UnicodeLabel(int virtualKey, int scanCode)
    {
        try
        {
            byte[] state = _keyState ??= new byte[256];
            char[] text = _textBuffer ??= new char[8];
            if (GetKeyboardState(state))
            {
                // 前台窗口的键盘布局才是用户此刻实际在用的布局。
                IntPtr layout = GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), out _));
                int count = ToUnicodeEx((uint)virtualKey, (uint)scanCode, state, text, text.Length, 0x4, layout);
                if (count > 0)
                {
                    string label = new string(text, 0, count).TrimEnd('\0');
                    if (label.Length > 0) return label;
                }
            }
        }
        catch (Exception)
        {
            // 取标签失败只影响显示文字，绝不能让异常穿过钩子回调。
        }
        uint mapped = MapVirtualKeyW((uint)virtualKey, 2 /* MAPVK_VK_TO_CHAR */);
        if ((mapped & 0xFFFF) != 0) return char.ConvertFromUtf32((int)(mapped & 0xFFFF));
        return "0x" + virtualKey.ToString("X2");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetKeyboardState(byte[] keyState);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, byte[] keyState,
        [Out] char[] buffer, int bufferSize, uint flags, IntPtr layout);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint threadId);
}

/// <summary>
/// 界面上要呈现的输入状态：最近按键、连击节奏、按键轨迹与鼠标按键。
/// 由界面线程在每帧 Drain 之后喂入，只在界面线程使用，不做跨线程同步。
/// </summary>
internal sealed class DesktopPetInputState
{
    internal readonly record struct KeyPulse(string Label, int VirtualKey, int Slot, bool LeftHand, long At);
    internal readonly record struct MousePulse(PetInputKind Button, long At);

    private const int TrailLimit = 8;
    private const long TypingWindow = 800;  // 最近一次按键后 800ms 内算“正在打字”
    private const long BurstWindow = 700;   // 相邻按键间隔不超过 700ms 视为同一段连击
    private const long TrailWindow = 1500;  // 轨迹里的键帽保留 1.5s
    private const long StaleWindow = 1600;  // 最近按键/鼠标事件过期时间

    private readonly List<KeyPulse> _trail = new(TrailLimit);
    private long _lastKeyAt = -1;
    private int _burst;

    internal KeyPulse? LastKey { get; private set; }
    internal MousePulse? LastMouse { get; private set; }

    /// <summary>最近的按键序列，最旧→最新，最多 8 个；Slot 为 -1 表示该键不在可视化键盘上。</summary>
    internal IReadOnlyList<KeyPulse> Trail => _trail;

    /// <summary>当前连击段内的按键数；Prune 判定过期后归零。</summary>
    internal int Burst => _burst;

    /// <summary>写入一条输入事件。时间戳由调用方给出（Environment.TickCount64）。</summary>
    internal void Push(in PetInputCue cue)
    {
        if (cue.Kind == PetInputKind.Key)
        {
            int slot = PetKeyboardLayout.IndexOf(cue.VirtualKey);
            var pulse = new KeyPulse(cue.Label ?? "", cue.VirtualKey, slot,
                PetKeyboardLayout.IsLeftHand(cue.VirtualKey), cue.Timestamp);
            LastKey = pulse;
            _trail.Add(pulse);
            if (_trail.Count > TrailLimit) _trail.RemoveAt(0);
            _burst = _lastKeyAt >= 0 && cue.Timestamp - _lastKeyAt <= BurstWindow ? _burst + 1 : 1;
            _lastKeyAt = cue.Timestamp;
            return;
        }
        LastMouse = new MousePulse(cue.Kind, cue.Timestamp);
    }

    /// <summary>丢弃过期的按键/鼠标状态与轨迹键帽；每帧调用一次。</summary>
    internal void Prune(long now)
    {
        if (LastKey is { } key && now - key.At > StaleWindow)
        {
            LastKey = null;
            _lastKeyAt = -1;
            _burst = 0;
        }
        if (LastMouse is { } mouse && now - mouse.At > StaleWindow) LastMouse = null;
        for (int i = _trail.Count - 1; i >= 0; i--)
            if (now - _trail[i].At > TrailWindow) _trail.RemoveAt(i);
    }

    /// <summary>最近 800ms 内有按键按下。</summary>
    internal bool Typing(long now) => LastKey is { } key && now - key.At <= TypingWindow;

    internal void Reset()
    {
        _trail.Clear();
        LastKey = null;
        LastMouse = null;
        _lastKeyAt = -1;
        _burst = 0;
    }
}

/// <summary>
/// 全局低级键鼠钩子（WH_KEYBOARD_LL / WH_MOUSE_LL）。
/// 钩子安装在专用后台线程自带的消息泵里，不在 WPF 界面线程安装；回调只做取值与入队，
/// 不碰界面、不写磁盘、不写日志。Start/Stop/Dispose 遇到任何失败都只体现在
/// KeyboardHooked / MouseHooked / LastError 上，不向调用方抛异常。
/// </summary>
internal sealed class DesktopPetInputHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmRightButtonDown = 0x0204;
    private const int WmMiddleButtonDown = 0x0207;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0x0000;
    private const int MaxQueued = 256;
    private const long KeyThrottleMs = 30;
    private const int ErrorTimeout = 1460;

    private readonly ConcurrentQueue<PetInputCue> _queue = new();
    private readonly object _gate = new();
    private readonly long[] _lastKeyAt = new long[256];

    private volatile Thread _thread;
    private volatile int _threadId;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private LowLevelKeyboardProc _keyboardProc;
    private LowLevelMouseProc _mouseProc;
    private volatile bool _suppressMouse;
    private volatile bool _keyboardHooked;
    private volatile bool _mouseHooked;
    private volatile int _lastError;
    private volatile bool _disposed;

    internal bool KeyboardHooked => _keyboardHooked;
    internal bool MouseHooked => _mouseHooked;

    /// <summary>钩子线程是否还活着；Stop/Dispose 正常完成后为 false。</summary>
    internal bool Running => _thread is { IsAlive: true };

    /// <summary>最近一次钩子/线程失败的原生错误码（0 表示没有失败）。</summary>
    internal int LastError => _lastError;

    /// <summary>鼠标指针在桌宠自身上时为 true，让钩子忽略这次点击。</summary>
    internal bool SuppressMouse { get => _suppressMouse; set => _suppressMouse = value; }

    /// <summary>启动钩子线程并安装两个低级钩子。可重复调用；失败时保持降级状态而不抛异常。</summary>
    internal void Start()
    {
        if (_disposed) return;
        lock (_gate)
        {
            if (Running) return;
            _lastError = 0;
            _keyboardHooked = false;
            _mouseHooked = false;
            _keyboardHook = IntPtr.Zero;
            _mouseHook = IntPtr.Zero;
            _keyboardProc = KeyboardProc;
            _mouseProc = MouseProc;
            Array.Clear(_lastKeyAt, 0, _lastKeyAt.Length);

            var ready = new ManualResetEventSlim(false);
            Thread thread;
            try
            {
                thread = new Thread(() => Pump(ready))
                {
                    IsBackground = true,
                    Name = "MouseDock 桌宠输入钩子"
                };
                _thread = thread;
                thread.Start();
            }
            catch (Exception error)
            {
                _thread = null;
                _lastError = error.HResult;
                ready.Dispose();
                return;
            }

            if (!ready.Wait(2000))
            {
                // 线程没能开始装钩子：记录超时并尽力回收，绝不无限等待。
                _lastError = ErrorTimeout;
                PostQuit();
                thread.Join(500);
            }
            ready.Dispose();
        }
    }

    /// <summary>请求钩子线程退出并等待其结束（最多 2 秒）。可重复调用。</summary>
    internal void Stop()
    {
        lock (_gate)
        {
            Thread thread = _thread;
            if (thread == null)
            {
                ClearQueue();
                return;
            }
            PostQuit();
            if (!thread.Join(2000))
            {
                // 极少数情况下钩子线程没有及时退出：再请求一次并保留重试机会。
                // 队列必须清空，避免窗口隐藏后恢复显示时重放旧事件。
                PostQuit();
                _lastError = ErrorTimeout;
                ClearQueue();
                return;
            }
            _thread = null;
            _threadId = 0;
            _keyboardHooked = false;
            _mouseHooked = false;
            ClearQueue();
        }
    }

    private void ClearQueue()
    {
        while (_queue.TryDequeue(out _)) { }
        Array.Clear(_lastKeyAt, 0, _lastKeyAt.Length);
    }

    public void Dispose()
    {
        if (_disposed) return;
        try { Stop(); }
        catch (Exception) { /* 释放阶段同样不向调用方抛异常。 */ }
        _disposed = true;
    }

    /// <summary>取走队列里的输入事件（最旧优先），不阻塞；返回实际取出的条数。</summary>
    internal int Drain(Span<PetInputCue> buffer)
    {
        int count = 0;
        while (count < buffer.Length && _queue.TryDequeue(out PetInputCue cue))
            buffer[count++] = cue;
        return count;
    }

    /// <summary>测试注入：与钩子回调走同一条队列，用于离线校验取回顺序与容量上限。</summary>
    internal void PushForTest(PetInputCue cue) => Enqueue(cue);

    private void Pump(ManualResetEventSlim ready)
    {
        try
        {
            _threadId = GetCurrentThreadId();
            // 先创建线程消息队列，保证 Stop 的 PostThreadMessage 一定投递得到。
            PeekMessageW(out _, IntPtr.Zero, 0, 0, PmNoRemove);
            IntPtr module = GetModuleHandleW(null);
            _keyboardHook = SetWindowsHookExKeyboard(WhKeyboardLl, _keyboardProc, module, 0);
            if (_keyboardHook != IntPtr.Zero) _keyboardHooked = true;
            else _lastError = HookError();
            _mouseHook = SetWindowsHookExMouse(WhMouseLl, _mouseProc, module, 0);
            if (_mouseHook != IntPtr.Zero) _mouseHooked = true;
            else if (_lastError == 0) _lastError = HookError();
        }
        catch (Exception error)
        {
            _lastError = error.HResult;
        }
        finally
        {
            try { ready.Set(); } catch (ObjectDisposedException) { /* Start 已经超时放弃等待。 */ }
        }

        try
        {
            while (GetMessageW(out Msg message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }
        catch (Exception error)
        {
            _lastError = error.HResult;
        }
        finally
        {
            if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
            if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
            _keyboardHook = IntPtr.Zero;
            _mouseHook = IntPtr.Zero;
            _keyboardHooked = false;
            _mouseHooked = false;
            _threadId = 0;
        }
    }

    private static int HookError()
    {
        int error = Marshal.GetLastWin32Error();
        return error != 0 ? error : -1;
    }

    private void PostQuit()
    {
        int threadId = _threadId;
        if (threadId != 0) PostThreadMessageW((uint)threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            if (message == WmKeyDown || message == WmSysKeyDown) CaptureKey(lParam);
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private void CaptureKey(IntPtr lParam)
    {
        // 低级钩子回调绝不能向外抛异常：Windows 会直接摘掉钩子且不再回调。
        try
        {
            // 直接读 KBDLLHOOKSTRUCT 的前三个 DWORD：只取值，避免结构体封送分配。
            int virtualKey = Marshal.ReadInt32(lParam, 0);  // vkCode
            int scanCode = Marshal.ReadInt32(lParam, 4);    // scanCode
            if (IsModifier(virtualKey)) return;
            long now = Environment.TickCount64;
            if ((uint)virtualKey < 256u)
            {
                int slot = virtualKey & 0xFF;
                if (now - _lastKeyAt[slot] < KeyThrottleMs) return;
                _lastKeyAt[slot] = now;
            }
            Enqueue(new PetInputCue(PetInputKind.Key, virtualKey, scanCode,
                PetKeyboardLayout.LabelOf(virtualKey, scanCode), now));
        }
        catch (Exception)
        {
            // 标签换算等失败时丢弃这一条事件，保持钩子存活。
        }
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_suppressMouse)
        {
            PetInputKind kind;
            switch (wParam.ToInt32())
            {
                case WmLeftButtonDown: kind = PetInputKind.MouseLeft; break;
                case WmRightButtonDown: kind = PetInputKind.MouseRight; break;
                case WmMiddleButtonDown: kind = PetInputKind.MouseMiddle; break;
                default: return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
            }
            Enqueue(new PetInputCue(kind, 0, 0, "", Environment.TickCount64));
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private void Enqueue(in PetInputCue cue)
    {
        if (_queue.Count >= MaxQueued) _queue.TryDequeue(out _);
        _queue.Enqueue(cue);
    }

    private static bool IsModifier(int virtualKey) => virtualKey switch
    {
        0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or 0x5D or 0x90 or 0x91 => true, // Shift/Ctrl/Alt/CapsLock/Win/Menu/NumLock/ScrollLock
        >= 0xA0 and <= 0xA5 => true,                                                    // 左右分开的 Shift/Ctrl/Alt
        _ => false
    };

    // ── Win32 ──

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        internal IntPtr Window;
        internal uint Message;
        internal IntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExKeyboard(int idHook, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExMouse(int idHook, LowLevelMouseProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessageW(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetMessageW(out Msg message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DispatchMessageW(ref Msg message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PeekMessageW(out Msg message, IntPtr window, uint filterMin, uint filterMax, uint removeMsg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string moduleName);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();
}
