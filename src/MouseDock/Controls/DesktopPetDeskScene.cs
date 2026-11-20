using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MouseDock.Services;

namespace MouseDock.Controls;

/// <summary>
/// 桌前键盘桌宠场景：椅背与背景 → 角色图集 → 桌面、显示器、键盘、鼠标与双手。
/// 角色图集和星星画布由桌宠窗口持有，本控件只负责桌面场景与输入跟随动画。
/// 所有几何使用 DIP，图层顺序固定，避免逐帧重建可视化树。
/// </summary>
internal sealed class DesktopPetDeskScene : Canvas
{
    /// <summary>内部设计画布尺寸；实际显示按 <see cref="LayoutScale"/> 缩到桌面小宠尺寸。</summary>
    internal const double DesignWidth = 300;
    internal const double DesignHeight = 236;

    /// <summary>场景输出比例：300×236 设计画布缩小到约 216×170 DIP，接近常见键鼠同步小猫桌宠的占用。</summary>
    internal const double LayoutScale = .72;
    internal const double LayoutWidth = DesignWidth * LayoutScale;
    internal const double LayoutHeight = DesignHeight * LayoutScale;

    private const double DeskLeft = 6;
    private const double DeskRight = 294;
    private const double DeskTop = 146;
    private const double DeskNear = 184;
    private const double DeskBottom = 224;

    private const double KeyboardLeft = 66;
    private const double KeyboardTop = 150;
    private const double KeyboardWidth = 168;
    private const double KeyboardHeight = 30;
    private const double KeyboardPadding = 2;
    private const double KeyboardRowGap = 1;
    internal const double KeyboardUnits = 11.25;
    private const int KeyboardRows = 5;

    private const double MouseLeft = 240;
    private const double MouseTop = 152;
    private const double MouseWidth = 40;
    private const double MouseHeight = 26;

    private const double MonitorLeft = 8;
    private const double MonitorTop = 86;
    private const double MonitorWidth = 62;
    private const double MonitorHeight = 48;

    private const double CharacterLeft = 46;
    private const double CharacterTop = 0;
    private const double CharacterSize = 208;

    private static readonly int HomeLeftSlot = PetKeyboardLayout.IndexOf(0x46);  // F
    private static readonly int HomeRightSlot = PetKeyboardLayout.IndexOf(0x4A); // J

    private sealed class Layer : FrameworkElement
    {
        internal Action<DrawingContext> Paint;
        protected override void OnRender(DrawingContext dc) => Paint?.Invoke(dc);
    }

    private readonly Layer _back = new() { IsHitTestVisible = false };
    private readonly Layer _front = new() { IsHitTestVisible = false };
    private readonly Image _character;
    private readonly Canvas _sparks;

    private readonly Typeface _typeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private readonly Typeface _mono = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private Brush _surface = Brushes.Transparent;
    private Brush _outline = Brushes.Transparent;
    private Brush _accent = Brushes.Transparent;
    private Brush _hand = Brushes.Transparent;
    private Brush _sleeve = Brushes.Transparent;
    private Brush _deskFront = Brushes.Transparent;
    private Brush _deskEdge = Brushes.Transparent;
    private Brush _keyBase = Brushes.Transparent;
    private Brush _keyBorder = Brushes.Transparent;
    private Brush _keyDark = Brushes.Transparent;
    private Brush _panel = Brushes.Transparent;
    private Brush _dim = Brushes.Transparent;
    private Brush _chair = Brushes.Transparent;
    private Brush _chairEdge = Brushes.Transparent;
    private Brush _handLine = Brushes.Transparent;
    private Brush _glow = Brushes.Transparent;
    private Brush _shadow = Brushes.Transparent;
    private Brush _deskTopFill = Brushes.Transparent;
    private Brush _accentSoft = Brushes.Transparent;
    private Brush _accentGlow = Brushes.Transparent;
    private Brush _panelSoft = Brushes.Transparent;
    private Pen _chairPen;
    private Pen _chairCushionPen;
    private Pen _deskPen;
    private Pen _deskLinePen;
    private Pen _keyPen;
    private Pen _keyPenPressed;
    private Pen _panelPen;
    private Pen _screenPen;
    private Pen _mousePen;
    private Pen _mouseWheelPen;
    private Pen _handPen;
    private Pen _handLinePen;
    private Pen _keycapPen;

    private Point _leftHand;
    private Point _rightHand;
    private double _leftDepth;
    private double _rightDepth;
    private long _lastFrame = -1;
    private long _keyAge = long.MaxValue;
    private string _trail = "";
    private bool _showLabels = true;
    private bool _mirrorMouse = true;
    private long _now;

    internal DesktopPetDeskScene(Image character, Canvas sparks)
    {
        _character = character ?? throw new ArgumentNullException(nameof(character));
        _sparks = sparks ?? throw new ArgumentNullException(nameof(sparks));

        Width = LayoutWidth;
        Height = LayoutHeight;
        ClipToBounds = false;
        UseLayoutRounding = true;
        IsHitTestVisible = true;
        // 内部仍用 300×236 的设计坐标绘制，WPF 缩放保证高 DPI 下的矢量清晰度；
        // 命中测试会跟随 RenderTransform，桌宠的点击/拖动区域与实际可见范围一致。
        RenderTransform = new ScaleTransform(LayoutScale, LayoutScale);
        RenderTransformOrigin = new Point(0, 0);

        _back.Width = DesignWidth;
        _back.Height = DesignHeight;
        _front.Width = DesignWidth;
        _front.Height = DesignHeight;
        _back.Paint = PaintBackground;
        _front.Paint = PaintForeground;

        _character.Width = CharacterSize;
        _character.Height = CharacterSize;
        _character.Stretch = Stretch.Uniform;
        SetLeft(_character, CharacterLeft);
        SetTop(_character, CharacterTop);

        Children.Add(_back);
        Children.Add(_character);
        Children.Add(_front);
        Children.Add(_sparks);

        _leftHand = KeyCenter(HomeLeftSlot);
        _rightHand = KeyCenter(HomeRightSlot);
        ApplyPalette(Brushes.Transparent, Brushes.Transparent, Brushes.White, Brushes.Transparent, Brushes.Transparent);
    }

    /// <summary>当前帧的按键高亮槽位，-1 表示没有高亮。</summary>
    internal int HighlightedSlot { get; private set; } = -1;

    /// <summary>当前帧显示的按键标签，空字符串表示没有。</summary>
    internal string HighlightedLabel { get; private set; } = "";

    /// <summary>显示器上实际显示的字符，用于回归检查。</summary>
    internal string MonitorText { get; private set; } = "";

    internal bool MouseLeftPressed { get; private set; }
    internal bool MouseRightPressed { get; private set; }
    internal bool KeyLabelsVisible => _showLabels;
    internal Point LeftHandPoint => _leftHand;
    internal Point RightHandPoint => _rightHand;

    internal static Rect KeyboardBounds => new(KeyboardLeft, KeyboardTop, KeyboardWidth, KeyboardHeight);
    internal static Rect MouseBounds => new(MouseLeft, MouseTop, MouseWidth, MouseHeight);
    internal static Rect MonitorBounds => new(MonitorLeft, MonitorTop, MonitorWidth, MonitorHeight);

    /// <summary>单个键帽的渲染矩形；索引来自 <see cref="PetKeyboardLayout.Keys"/>。</summary>
    internal static Rect KeyBounds(int slot)
    {
        var keys = PetKeyboardLayout.Keys;
        if (slot < 0 || slot >= keys.Count) return Rect.Empty;
        var key = keys[slot];
        double unit = KeyboardWidth / KeyboardUnits;
        double rowHeight = (KeyboardHeight - 2 * KeyboardPadding - (KeyboardRows - 1) * KeyboardRowGap) / KeyboardRows;
        double x = KeyboardLeft + KeyboardPadding + key.Offset * unit;
        double y = KeyboardTop + KeyboardPadding + key.Row * (rowHeight + KeyboardRowGap);
        return new Rect(x, y, Math.Max(1, key.Width * unit - 1.2), Math.Max(1, rowHeight));
    }

    private static Point KeyCenter(int slot)
    {
        var rect = KeyBounds(slot);
        return rect.IsEmpty ? new Point(KeyboardLeft + KeyboardWidth / 2.0, KeyboardTop + KeyboardHeight / 2.0)
            : new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    private static Point MousePoint(PetInputKind button) => button == PetInputKind.MouseRight
        ? new Point(MouseLeft + MouseWidth * .7, MouseTop + MouseHeight * .45)
        : new Point(MouseLeft + MouseWidth * .3, MouseTop + MouseHeight * .45);

    /// <summary>按角色图集的内容比例微调显示尺寸，让四款角色的可见高度更接近。</summary>
    internal void SetCharacterScale(double scale)
    {
        scale = Math.Clamp(scale, .8, 1.3);
        double size = CharacterSize * scale;
        _character.Width = size;
        _character.Height = size;
        SetLeft(_character, (DesignWidth - size) / 2);
        SetTop(_character, CharacterTop - (size - CharacterSize) * .4);
    }

    /// <summary>更新角色配色。桌面与键帽颜色都从角色主色派生，四款角色各自成立。</summary>
    internal void ApplyPalette(Brush surface, Brush outline, Brush accent, Brush hand, Brush sleeve)
    {
        var surfaceColor = ColorOf(surface, Color.FromRgb(22, 34, 51));
        var outlineColor = ColorOf(outline, Color.FromRgb(85, 117, 148));
        var accentColor = ColorOf(accent, Color.FromRgb(147, 224, 249));
        var handColor = ColorOf(hand, Color.FromRgb(246, 205, 178));
        var sleeveColor = ColorOf(sleeve, outlineColor);

        _surface = Frozen(surfaceColor, 1);
        _outline = Frozen(outlineColor, 1);
        _accent = Frozen(accentColor, 1);
        _hand = Frozen(handColor, 1);
        _sleeve = Frozen(sleeveColor, 1);
        _chair = Frozen(Mix(surfaceColor, Colors.Black, .12), .72);
        _chairEdge = Frozen(Mix(outlineColor, Colors.White, .12), .34);
        _deskFront = Frozen(Mix(surfaceColor, Colors.Black, .2), 1);
        _deskEdge = Frozen(Mix(outlineColor, Colors.White, .2), .55);
        _keyBase = Frozen(Mix(surfaceColor, Colors.White, .24), 1);
        _keyBorder = Frozen(outlineColor, .34);
        _keyDark = Frozen(Mix(surfaceColor, Colors.Black, .38), 1);
        _panel = Frozen(Mix(surfaceColor, Colors.Black, .52), 1);
        _dim = Frozen(Mix(outlineColor, Colors.White, .3), .8);
        _handLine = Frozen(Mix(handColor, Colors.Black, .35), .5);

        // 背景与桌面每帧都会重绘，渐变和阴影只在这里创建一次，避免 30fps 的 Freezable 分配。
        _glow = new RadialGradientBrush(Color.FromArgb(28, 160, 200, 255), Color.FromArgb(0, 160, 200, 255))
        {
            Center = new Point(.5, .42),
            GradientOrigin = new Point(.5, .42),
            RadiusX = .5,
            RadiusY = .5
        };
        _glow.Freeze();
        _shadow = new RadialGradientBrush(Color.FromArgb(70, 0, 0, 0), Color.FromArgb(0, 0, 0, 0))
        {
            Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5
        };
        _shadow.Freeze();
        _deskTopFill = new LinearGradientBrush(
            Mix(Mix(surfaceColor, Colors.White, .16), Colors.White, .1), Mix(surfaceColor, Colors.White, .16), 90);
        _deskTopFill.Freeze();

        // 笔刷与画笔每帧复用；此前每帧 new Pen/Clone+Freeze 对常驻 30fps 窗口是持续小对象分配。
        _accentSoft = Frozen(accentColor, .38);
        _accentGlow = Frozen(accentColor, .14);
        _panelSoft = Frozen(Mix(surfaceColor, Colors.Black, .52), .94);
        _chairPen = FrozenPen(_chairEdge, 1.1);
        _chairCushionPen = FrozenPen(_chairEdge, 1);
        _deskPen = FrozenPen(_deskEdge, 1);
        _deskLinePen = FrozenPen(_deskEdge, .85);
        _keyPen = FrozenPen(_keyBorder, .7);
        _keyPenPressed = FrozenPen(_accent, 1.1);
        _panelPen = FrozenPen(_accent, 1);
        _screenPen = FrozenPen(_deskEdge, .8);
        _mousePen = FrozenPen(_deskEdge, 1);
        _mouseWheelPen = FrozenPen(_deskEdge, .8);
        _handPen = FrozenPen(_handLine, .8);
        _handLinePen = FrozenPen(_handLine, .7);
        _keycapPen = FrozenPen(_accent, 1.2);

        InvalidateVisual();
        _back.InvalidateVisual();
        _front.InvalidateVisual();
    }

    /// <summary>
    /// 每帧更新输入跟随状态。motion=false（减少动态效果）时保留键帽高亮与显示器文字，但双手直接落到位。
    /// </summary>
    internal void Render(long now, bool motion, DesktopPetInputState input, bool showLabels, bool mirrorMouse)
    {
        _now = now;
        _showLabels = showLabels;
        _mirrorMouse = mirrorMouse;

        double dt = _lastFrame < 0 ? 16 : Math.Clamp(now - _lastFrame, 1, 120);
        _lastFrame = now;

        var key = input?.LastKey;
        long keyAge = key.HasValue ? now - key.Value.At : long.MaxValue;
        var mouse = mirrorMouse ? input?.LastMouse : null;
        long mouseAge = mouse.HasValue ? now - mouse.Value.At : long.MaxValue;

        _keyAge = keyAge;

        HighlightedSlot = keyAge < 420 ? key?.Slot ?? -1 : -1;
        HighlightedLabel = keyAge < 900 ? key?.Label ?? "" : "";
        MouseLeftPressed = mouseAge < 260 && mouse?.Button == PetInputKind.MouseLeft;
        MouseRightPressed = mouseAge < 260 && mouse?.Button == PetInputKind.MouseRight;

        var left = KeyCenter(HomeLeftSlot);
        var right = KeyCenter(HomeRightSlot);
        double leftTargetDepth = 0, rightTargetDepth = 0;
        if (keyAge < 900 && key.HasValue)
        {
            var center = KeyCenter(key.Value.Slot);
            if (key.Value.LeftHand)
            {
                if (key.Value.Slot >= 0) left = center;
                leftTargetDepth = keyAge < 180 ? 1 : 0;
            }
            else
            {
                if (key.Value.Slot >= 0) right = center;
                rightTargetDepth = keyAge < 180 ? 1 : 0;
            }
        }
        // 鼠标只接管“比最近按键更新”的右手目标；否则点击后 700ms 内的右手键会被鼠标位置覆盖。
        if (mouseAge < 700 && mouse.HasValue && (keyAge >= 900 || mouseAge < keyAge))
        {
            right = MousePoint(mouse.Value.Button);
            rightTargetDepth = mouseAge < 220 ? 1 : 0;
        }

        if (motion)
        {
            double k = 1 - Math.Exp(-dt / 62d);
            double kd = 1 - Math.Exp(-dt / 40d);
            _leftHand = Lerp(_leftHand, left, k);
            _rightHand = Lerp(_rightHand, right, k);
            _leftDepth += (leftTargetDepth - _leftDepth) * kd;
            _rightDepth += (rightTargetDepth - _rightDepth) * kd;
        }
        else
        {
            _leftHand = left;
            _rightHand = right;
            _leftDepth = leftTargetDepth;
            _rightDepth = rightTargetDepth;
        }

        MonitorText = FitMonitor(input);
        _trail = showLabels ? MonitorText : "";

        // 椅背层与输入无关，只在换角色/调色板时由 ApplyPalette 失效。
        _front.InvalidateVisual();
    }

    /// <summary>按显示器可用宽度从最近按键往前拼，保证 MonitorText 与实际绘制内容一致且不截半个标签。</summary>
    private string FitMonitor(DesktopPetInputState input)
    {
        if (input == null || input.Trail.Count == 0) return "";
        double available = MonitorWidth - 8 - 14;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var labels = new System.Collections.Generic.List<string>(8);
        for (int i = input.Trail.Count - 1; i >= 0 && labels.Count < 8; i--)
        {
            string label = input.Trail[i].Label;
            if (string.IsNullOrEmpty(label)) continue;
            labels.Insert(0, label);
            var text = string.Concat(labels);
            var measure = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                _mono, 8.5, _accent, ppd);
            if (measure.Width > available)
            {
                labels.RemoveAt(0);
                break;
            }
        }
        return string.Concat(labels);
    }

    private static Point Lerp(Point from, Point to, double amount) =>
        new(from.X + (to.X - from.X) * amount, from.Y + (to.Y - from.Y) * amount);

    private void PaintBackground(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, DesignWidth, DesignHeight)));
        dc.DrawEllipse(_glow, null, new Point(150, 108), 132, 118);

        // 椅背：让角色读起来是坐着的，而不是站在桌前。
        var chair = new Rect(88, 20, 124, 156);
        dc.DrawRoundedRectangle(_chair, _chairPen, chair, 28, 28);
        var cushion = new Rect(99, 36, 102, 126);
        dc.DrawRoundedRectangle(_chair, _chairCushionPen, cushion, 20, 20);
        dc.DrawLine(_chairCushionPen, new Point(150, 38), new Point(150, 162));
        dc.Pop();
    }

    private void PaintForeground(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, DesignWidth, DesignHeight)));
        PaintDesk(dc);
        PaintMonitor(dc);
        PaintKeyboard(dc);
        PaintMouse(dc);
        PaintHands(dc);
        PaintKeycap(dc);
        dc.Pop();
    }

    private void PaintDesk(DrawingContext dc)
    {
        // 地面软阴影
        dc.DrawEllipse(_shadow, null, new Point(150, 225), 130, 9);

        var top = new Rect(DeskLeft, DeskTop, DeskRight - DeskLeft, DeskNear - DeskTop);
        dc.DrawRoundedRectangle(_deskTopFill, _deskPen, top, 7, 7);
        dc.DrawLine(_deskLinePen, new Point(DeskLeft + 12, DeskTop + 1.5), new Point(DeskRight - 12, DeskTop + 1.5));

        var front = new Rect(DeskLeft + 6, DeskNear, DeskRight - DeskLeft - 12, DeskBottom - DeskNear - 4);
        dc.DrawRoundedRectangle(_deskFront, _deskPen, front, 8, 8);
        dc.DrawLine(_deskLinePen, new Point(DeskLeft + 16, DeskNear + 1), new Point(DeskRight - 16, DeskNear + 1));

        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var brand = new FormattedText("MouseDock", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            _typeface, 8.5, _dim, ppd);
        dc.DrawText(brand, new Point(150 - brand.Width / 2, DeskNear + 16));
    }

    private void PaintMonitor(DrawingContext dc)
    {
        var bezel = new Rect(MonitorLeft, MonitorTop, MonitorWidth, MonitorHeight);
        dc.DrawRoundedRectangle(_keyDark, _screenPen, bezel, 6, 6);
        var screen = new Rect(MonitorLeft + 4, MonitorTop + 4, MonitorWidth - 8, MonitorHeight - 15);
        dc.DrawRoundedRectangle(_panel, _panelPen, screen, 3, 3);

        // 显示器三颗指示灯
        for (int i = 0; i < 3; i++)
            dc.DrawEllipse(_dim, null, new Point(MonitorLeft + 10 + i * 5, MonitorTop + 7.5), .9, .9);

        // 支架
        var stand = new Rect(MonitorLeft + MonitorWidth / 2 - 4, MonitorTop + MonitorHeight - 8, 8, 6);
        dc.DrawRoundedRectangle(_keyDark, _screenPen, stand, 1.5, 1.5);

        if (!_showLabels)
        {
            double ppd0 = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var idle = new FormattedText("MouseDock", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                _mono, 7, _dim, ppd0);
            dc.DrawText(idle, new Point(screen.X + 4, screen.Y + screen.Height / 2 - idle.Height / 2));
            return;
        }

        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        string text = _trail;
        FormattedText body;
        if (string.IsNullOrEmpty(text))
        {
            body = new FormattedText("", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _mono, 8.5, _accent, ppd);
            var idle = new FormattedText("MouseDock", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                _mono, 7.5, _dim, ppd);
            dc.DrawText(idle, new Point(screen.X + 4, screen.Y + screen.Height - idle.Height - 2));
        }
        else
        {
            body = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                _mono, 8.5, _accent, ppd);
        }
        double baseline = screen.Y + screen.Height - body.Height - 2;
        dc.DrawText(body, new Point(screen.X + 4, baseline));
        if ((_now / 520) % 2 == 0)
        {
            var cursor = new Rect(screen.X + 5 + body.Width, baseline + 1, 3, Math.Max(3, body.Height - 1));
            dc.DrawRectangle(_accent, null, cursor);
        }
    }

    private void PaintKeyboard(DrawingContext dc)
    {
        var board = new Rect(KeyboardLeft, KeyboardTop, KeyboardWidth, KeyboardHeight);
        dc.DrawRoundedRectangle(_keyDark, _screenPen, board, 5, 5);

        if (_keyAge < 420)
            dc.DrawRoundedRectangle(_accentGlow, null, new Rect(board.X - 3, board.Y - 3, board.Width + 6, board.Height + 6), 7, 7);

        var keys = PetKeyboardLayout.Keys;
        for (int i = 0; i < keys.Count; i++)
        {
            var rect = KeyBounds(i);
            if (rect.IsEmpty) continue;
            bool pressed = i == HighlightedSlot;
            dc.DrawRoundedRectangle(pressed ? _accent : _keyBase, pressed ? _keyPenPressed : _keyPen,
                pressed ? new Rect(rect.X, rect.Y + 1.2, rect.Width, rect.Height - .8) : rect, 1.6, 1.6);
        }
    }

    private void PaintMouse(DrawingContext dc)
    {
        var body = new Rect(MouseLeft, MouseTop, MouseWidth, MouseHeight);
        dc.DrawRoundedRectangle(_keyDark, _mousePen, body, 12, 12);

        var leftHalf = new Rect(MouseLeft + 2, MouseTop + 2, MouseWidth / 2 - 2.5, MouseHeight - 4);
        var rightHalf = new Rect(MouseLeft + MouseWidth / 2 + .5, MouseTop + 2, MouseWidth / 2 - 2.5, MouseHeight - 4);
        if (MouseLeftPressed) dc.DrawRoundedRectangle(_accentSoft, null, leftHalf, 9, 9);
        if (MouseRightPressed) dc.DrawRoundedRectangle(_accentSoft, null, rightHalf, 9, 9);
        dc.DrawLine(_mouseWheelPen, new Point(MouseLeft + MouseWidth / 2, MouseTop + 5),
            new Point(MouseLeft + MouseWidth / 2, MouseTop + MouseHeight - 5));
        dc.DrawRoundedRectangle(_keyDark, _mouseWheelPen,
            new Rect(MouseLeft + MouseWidth / 2 - 3, MouseTop + 6, 6, 9), 3, 3);
    }

    private void PaintHands(DrawingContext dc)
    {
        DrawHand(dc, _leftHand, _leftDepth);
        DrawHand(dc, _rightHand, _rightDepth);
    }

    private void DrawHand(DrawingContext dc, Point point, double depth)
    {
        double y = point.Y + 2 + depth * 1.6;
        var sleeve = new Rect(point.X - 7.5, y - 6.5, 15, 8);
        dc.DrawRoundedRectangle(_sleeve, null, sleeve, 3, 3);
        var hand = new Rect(point.X - 6.5, y - 2.5, 13, 8);
        dc.DrawRoundedRectangle(_hand, _handPen, hand, 3.4, 3.4);
        dc.DrawLine(_handLinePen, new Point(point.X - 1.5, hand.Y + 1.4), new Point(point.X - 1.5, hand.Bottom - 1.4));
        dc.DrawLine(_handLinePen, new Point(point.X + 2, hand.Y + 1.4), new Point(point.X + 2, hand.Bottom - 1.4));
    }

    private void PaintKeycap(DrawingContext dc)
    {
        if (!_showLabels || _keyAge > 700 || string.IsNullOrEmpty(HighlightedLabel)) return;
        double progress = Math.Clamp(_keyAge / 700d, 0, 1);
        double ease = 1 - Math.Pow(1 - progress, 2);
        double opacity = progress < .12 ? progress / .12 : 1 - Math.Pow((progress - .12) / .88, 1.5);
        if (opacity <= .02) return;

        bool onKeyboard = HighlightedSlot >= 0;
        var center = onKeyboard
            ? KeyCenter(HighlightedSlot)
            : new Point(KeyboardLeft + KeyboardWidth / 2, KeyboardTop + KeyboardHeight / 2);
        string label = HighlightedLabel;
        bool narrow = label.Length == 1;
        double width = narrow ? 24 : Math.Max(30, 11 + label.Length * 9);
        const double height = 21;
        double x = Math.Clamp(center.X - width / 2, 6, DesignWidth - width - 6);
        double y = (onKeyboard ? KeyBounds(HighlightedSlot).Y : KeyboardTop - 4) - 20 - 10 * ease;

        dc.PushOpacity(opacity);
        dc.DrawRoundedRectangle(_panelSoft, _keycapPen, new Rect(x, y, width, height), 6, 6);
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            _typeface, narrow ? 12.5 : 9.5, _accent, ppd);
        dc.DrawText(text, new Point(x + (width - text.Width) / 2, y + (height - text.Height) / 2));
        dc.Pop();
    }

    private static Color ColorOf(Brush brush, Color fallback) =>
        brush is SolidColorBrush solid && solid.Color.A > 0 ? solid.Color : fallback;

    private static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static Brush Frozen(Color color, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(255 * Math.Clamp(opacity, 0, 1)), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
