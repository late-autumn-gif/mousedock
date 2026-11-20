using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MouseDock.Services;

namespace MouseDock.Controls;

/// <summary>
/// 桌前键盘桌宠场景：背景光晕 → 角色图集 → 霓虹桌垫、小玩偶、键盘、鼠标、双手与前沿文本条。
/// 角色图集和星星画布由桌宠窗口持有，本控件只负责桌前场景与输入跟随动画。
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

    private const double MatTop = 142;
    private const double MatBottom = 218;

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

    private const double CharacterLeft = 46;
    private const double CharacterTop = 0;
    private const double CharacterSize = 208;

    private static readonly Rect MatRect = new(16, MatTop, 268, MatBottom - MatTop);
    private static readonly Geometry MatGeometry = new RectangleGeometry(MatRect, 12, 12);
    private static readonly Geometry CableGeometry = BuildCableGeometry();
    private static readonly Geometry EarLeftGeometry = BuildTriangle(new Point(35, 150), new Point(31, 141), new Point(40, 146));
    private static readonly Geometry EarRightGeometry = BuildTriangle(new Point(49, 150), new Point(53, 141), new Point(44, 146));

    private static readonly int HomeLeftSlot = PetKeyboardLayout.IndexOf(0x46);  // F
    private static readonly int HomeRightSlot = PetKeyboardLayout.IndexOf(0x4A); // J

    private sealed class Layer : FrameworkElement
    {
        internal Action<DrawingContext> Paint;
        protected override void OnRender(DrawingContext dc) => Paint?.Invoke(dc);
    }

    private readonly Layer _back = new() { IsHitTestVisible = false };
    private readonly Layer _front = new() { IsHitTestVisible = false };
    private FrameworkElement _character;
    private double _characterScale = 1;
    private readonly Canvas _sparks;

    private readonly Typeface _typeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private readonly Typeface _mono = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private Brush _accent = Brushes.Transparent;
    private Brush _hand = Brushes.Transparent;
    private Brush _sleeve = Brushes.Transparent;
    private Brush _keyBase = Brushes.Transparent;
    private Brush _keyBorder = Brushes.Transparent;
    private Brush _keyDark = Brushes.Transparent;
    private Brush _dim = Brushes.Transparent;
    private Brush _accentSoft = Brushes.Transparent;
    private Brush _accentGlow = Brushes.Transparent;
    private Brush _glow = Brushes.Transparent;
    private Brush _shadow = Brushes.Transparent;
    private Brush _matFill = Brushes.Transparent;    private Brush _plushBody = Brushes.Transparent;
    private Brush _plushShade = Brushes.Transparent;
    private Pen _edgePen;
    private Pen _rimPen;
    private Pen _keyPen;
    private Pen _keyPenPressed;
    private Pen _handPen;
    private Pen _handLinePen;
    private Pen _keycapPen;
    private Pen _neonPen;
    private Pen _neonGlowPen;
    private Pen _neonHaloPen;
    private Pen _plushPen;

    private Point _leftHand;
    private Point _rightHand;
    private double _leftDepth;
    private double _rightDepth;
    private long _lastFrame = -1;
    private long _keyAge = long.MaxValue;
    private string _trail = "";
    private bool _showLabels = true;
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
        if (_character is Image image) image.Stretch = Stretch.Uniform;
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

    /// <summary>当前帧的按键标签，空字符串表示没有。</summary>
    internal string HighlightedLabel { get; private set; } = "";

    /// <summary>桌垫前沿文本条实际显示的字符；空字符串表示当前没有输入。</summary>
    internal string DisplayText { get; private set; } = "";

    internal bool MouseLeftPressed { get; private set; }
    internal bool MouseRightPressed { get; private set; }
    internal bool KeyLabelsVisible => _showLabels;
    internal Point LeftHandPoint => _leftHand;
    internal Point RightHandPoint => _rightHand;

    internal static Rect KeyboardBounds => new(KeyboardLeft, KeyboardTop, KeyboardWidth, KeyboardHeight);
    internal static Rect MouseBounds => new(MouseLeft, MouseTop, MouseWidth, MouseHeight);

    /// <summary>霓虹桌垫的外接矩形，用于回归检查键盘/鼠标没有跑出垫面。</summary>
    internal static Rect MatBounds => MatRect;

    /// <summary>前沿文本条的可用区域，位于键盘下方、桌垫底边之上。</summary>
    internal static Rect DisplayBounds => new(50, 200, 200, 16);

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

    /// <summary>当前使用的角色视觉；普通角色是 PNG Image，德蕾琪娜是分层矢量控件。</summary>
    internal FrameworkElement CharacterVisual => _character;

    /// <summary>切换角色视觉时保持场景图层顺序：背景 → 角色 → 前景 → 星星。</summary>
    internal void SetCharacterVisual(FrameworkElement visual)
    {
        if (visual == null || ReferenceEquals(visual, _character)) return;
        int index = Children.IndexOf(_character);
        if (index >= 0) Children.RemoveAt(index);
        else index = 1;
        _character = visual;
        if (_character is Image image) image.Stretch = Stretch.Uniform;
        Children.Insert(Math.Min(index, Children.Count), visual);
        ApplyCharacterLayout();
    }

    private void ApplyCharacterLayout()
    {
        double size = CharacterSize * _characterScale;
        _character.Width = size;
        _character.Height = size;
        SetLeft(_character, (DesignWidth - size) / 2);
        SetTop(_character, CharacterTop - (size - CharacterSize) * .4);
    }

    /// <summary>按角色图集的内容比例微调显示尺寸，让可见高度更接近。</summary>
    internal void SetCharacterScale(double scale)
    {
        _characterScale = Math.Clamp(scale, .8, 1.3);
        ApplyCharacterLayout();
    }

    /// <summary>更新角色配色。桌垫、键帽、霓虹描边与小玩偶都从角色主色派生，四款角色各自成立。</summary>
    internal void ApplyPalette(Brush surface, Brush outline, Brush accent, Brush hand, Brush sleeve)
    {
        var surfaceColor = ColorOf(surface, Color.FromRgb(22, 34, 51));
        var outlineColor = ColorOf(outline, Color.FromRgb(85, 117, 148));
        var accentColor = ColorOf(accent, Color.FromRgb(147, 224, 249));
        var handColor = ColorOf(hand, Color.FromRgb(246, 205, 178));
        var sleeveColor = ColorOf(sleeve, outlineColor);

        _accent = Frozen(accentColor, 1);
        _hand = Frozen(handColor, 1);
        _sleeve = Frozen(sleeveColor, 1);
        _keyBase = Frozen(Mix(surfaceColor, Colors.White, .2), 1);
        _keyBorder = Frozen(outlineColor, .34);
        _keyDark = Frozen(Mix(surfaceColor, Colors.Black, .38), 1);
        _dim = Frozen(Mix(outlineColor, Colors.White, .3), .8);
        _accentSoft = Frozen(accentColor, .34);
        _accentGlow = Frozen(accentColor, .16);

        // 背景与桌垫每帧都会重绘，渐变/画笔/几何只在这里创建一次，避免 30fps 的 Freezable 分配。
        _glow = new RadialGradientBrush(Color.FromArgb(44, accentColor.R, accentColor.G, accentColor.B), Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B))
        {
            Center = new Point(.5, .45),
            GradientOrigin = new Point(.5, .45),
            RadiusX = .52,
            RadiusY = .52
        };
        _glow.Freeze();
        _shadow = new RadialGradientBrush(Color.FromArgb(96, 0, 0, 0), Color.FromArgb(0, 0, 0, 0))
        {
            Center = new Point(.5, .5), GradientOrigin = new Point(.5, .5), RadiusX = .5, RadiusY = .5
        };
        _shadow.Freeze();
        var matFill = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        matFill.GradientStops.Add(new GradientStop(Mix(surfaceColor, Colors.Black, .56), 0));
        matFill.GradientStops.Add(new GradientStop(Mix(surfaceColor, accentColor, .14), .48));
        matFill.GradientStops.Add(new GradientStop(Mix(surfaceColor, Colors.Black, .8), 1));
        matFill.Freeze();
        _matFill = matFill;
        _plushBody = Frozen(Color.FromRgb(244, 233, 218), 1);
        _plushShade = Frozen(Mix(Color.FromRgb(244, 233, 218), surfaceColor, .28), 1);

        _edgePen = FrozenPen(Frozen(outlineColor, .5), .9);
        _rimPen = FrozenPen(Frozen(accentColor, .55), 1.2);
        _keyPen = FrozenPen(_keyBorder, .7);
        _keyPenPressed = FrozenPen(_accent, 1.1);
        _handPen = FrozenPen(Frozen(Mix(handColor, Colors.Black, .35), .5), .8);
        _handLinePen = FrozenPen(Frozen(Mix(handColor, Colors.Black, .35), .5), .7);
        _keycapPen = FrozenPen(_accent, 1.2);
        _neonPen = FrozenPen(_accent, 2);
        _neonGlowPen = FrozenPen(Frozen(accentColor, .18), 6);
        _neonHaloPen = FrozenPen(Frozen(accentColor, .07), 12);
        _plushPen = FrozenPen(Frozen(Mix(surfaceColor, Colors.Black, .45), .8), .8);

        InvalidateVisual();
        _back.InvalidateVisual();
        _front.InvalidateVisual();
    }

    /// <summary>
    /// 每帧更新输入跟随状态。motion=false（减少动态效果）时保留键帽高亮与文本条字符，但双手直接落到位。
    /// </summary>
    internal void Render(long now, bool motion, DesktopPetInputState input, bool showLabels, bool mirrorMouse)
    {
        _now = now;
        _showLabels = showLabels;

        double dt = _lastFrame < 0 ? 16 : Math.Clamp(now - _lastFrame, 1, 120);
        _lastFrame = now;

        var key = input?.LastKey;
        long keyAge = key.HasValue ? now - key.Value.At : long.MaxValue;
        var mouse = mirrorMouse ? input?.LastMouse : null;
        long mouseAge = mouse.HasValue ? now - mouse.Value.At : long.MaxValue;

        _keyAge = keyAge;

        bool leftHeld = mirrorMouse && input?.HeldButton == PetInputKind.MouseLeft;
        bool rightHeld = mirrorMouse && input?.HeldButton == PetInputKind.MouseRight;
        long releaseAt = input?.MouseReleaseAt ?? 0;
        bool leftRecent = mouse.HasValue && mouseAge < 260 && mouse.Value.Button == PetInputKind.MouseLeft && releaseAt <= mouse.Value.At;
        bool rightRecent = mouse.HasValue && mouseAge < 260 && mouse.Value.Button == PetInputKind.MouseRight && releaseAt <= mouse.Value.At;
        MouseLeftPressed = leftHeld || leftRecent;
        MouseRightPressed = rightHeld || rightRecent;

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
        // 鼠标只接管“比最近按键更新”的右手目标；按住期间一直停在鼠标上，松开后保留 700ms 再回键盘。
        bool mouseTarget = mirrorMouse && mouse.HasValue && (mouseAge < 700 || input?.HeldButton != null);
        if (mouseTarget && mouse.HasValue && (keyAge >= 900 || mouseAge < keyAge))
        {
            right = MousePoint(mouse.Value.Button);
            rightTargetDepth = (mouseAge < 220 || input?.HeldButton != null) ? 1 : 0;
        }

        HighlightedSlot = keyAge < 420 ? key?.Slot ?? -1 : -1;
        HighlightedLabel = keyAge < 900 ? key?.Label ?? "" : "";

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

        DisplayText = FitDisplay(input);
        _trail = showLabels ? DisplayText : "";

        // 背景光晕与角色无关，只在换角色/调色板时由 ApplyPalette 失效。
        _front.InvalidateVisual();
    }

    /// <summary>按文本条可用宽度从最近按键往前拼，保证 DisplayText 与实际绘制内容一致且不截半个标签。</summary>
    private string FitDisplay(DesktopPetInputState input)
    {
        if (input == null || input.Trail.Count == 0) return "";
        double available = DisplayBounds.Width - 8;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var labels = new List<string>(8);
        for (int i = input.Trail.Count - 1; i >= 0 && labels.Count < 8; i--)
        {
            string label = input.Trail[i].Label;
            if (string.IsNullOrEmpty(label)) continue;
            labels.Insert(0, label);
            var text = string.Concat(labels);
            var measure = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                _mono, 9.5, _accent, ppd);
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
        dc.DrawEllipse(_glow, null, new Point(150, 118), 140, 118);
        dc.DrawEllipse(_shadow, null, new Point(150, 226), 124, 9);
        dc.Pop();
    }

    private void PaintForeground(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, DesignWidth, DesignHeight)));
        PaintMat(dc);
        PaintPlush(dc);
        PaintKeyboard(dc);
        PaintMouse(dc);
        PaintHands(dc);
        PaintDisplay(dc);
        PaintKeycap(dc);
        dc.Pop();
    }

    private void PaintMat(DrawingContext dc)
    {
        // 外发光先画、垫面后画，霓虹描边最后覆盖，形成参考图的边缘光效。
        dc.DrawGeometry(null, _neonHaloPen, MatGeometry);
        dc.DrawGeometry(null, _neonGlowPen, MatGeometry);
        dc.DrawGeometry(_matFill, null, MatGeometry);
        dc.DrawGeometry(null, _neonPen, MatGeometry);

        // 左下角延出的霓虹线缆，和参考图一致地让垫子“有存在感”。
        dc.DrawGeometry(null, _neonHaloPen, CableGeometry);
        dc.DrawGeometry(null, _neonGlowPen, CableGeometry);
        dc.DrawGeometry(null, _neonPen, CableGeometry);
    }

    private void PaintPlush(DrawingContext dc)
    {
        // 小玩偶猫：白/奶油色身体，耳朵和尾巴用角色强调色。
        dc.DrawEllipse(_plushShade, _plushPen, new Point(58, 169), 7, 3.6);
        dc.DrawEllipse(_plushBody, _plushPen, new Point(46, 168), 11, 9);
        dc.DrawGeometry(_plushBody, _plushPen, EarLeftGeometry);
        dc.DrawGeometry(_plushBody, _plushPen, EarRightGeometry);
        dc.DrawEllipse(_accentSoft, null, new Point(35.6, 147.2), 2.2, 2.6);
        dc.DrawEllipse(_accentSoft, null, new Point(48.4, 147.2), 2.2, 2.6);
        dc.DrawEllipse(_plushBody, _plushPen, new Point(42, 155), 9, 8);
        dc.DrawEllipse(_plushShade, null, new Point(38.6, 154.4), 1.15, 1.4);
        dc.DrawEllipse(_plushShade, null, new Point(45.4, 154.4), 1.15, 1.4);
        dc.DrawLine(_plushPen, new Point(41, 158.6), new Point(43, 158.6));
        dc.DrawEllipse(_plushBody, _plushPen, new Point(42, 175), 4, 2.8);
        dc.DrawEllipse(_plushBody, _plushPen, new Point(50, 175), 4, 2.8);
        dc.DrawEllipse(_accentSoft, null, new Point(46, 172.5), 1.6, 1.6);
    }

    private void PaintKeyboard(DrawingContext dc)
    {
        var body = new Rect(KeyboardLeft - 4, KeyboardTop - 3, KeyboardWidth + 8, KeyboardHeight + 6);
        dc.DrawRoundedRectangle(_keyDark, _rimPen, body, 6, 6);
        if (_keyAge < 420)
            dc.DrawRoundedRectangle(_accentGlow, null, new Rect(body.X - 2, body.Y - 2, body.Width + 4, body.Height + 4), 8, 8);

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
        dc.DrawRoundedRectangle(_keyDark, (MouseLeftPressed || MouseRightPressed) ? _rimPen : _edgePen, body, 12, 12);

        var leftHalf = new Rect(MouseLeft + 2, MouseTop + 2, MouseWidth / 2 - 2.5, MouseHeight - 4);
        var rightHalf = new Rect(MouseLeft + MouseWidth / 2 + .5, MouseTop + 2, MouseWidth / 2 - 2.5, MouseHeight - 4);
        if (MouseLeftPressed) dc.DrawRoundedRectangle(_accentSoft, null, leftHalf, 9, 9);
        if (MouseRightPressed) dc.DrawRoundedRectangle(_accentSoft, null, rightHalf, 9, 9);
        dc.DrawLine(_edgePen, new Point(MouseLeft + MouseWidth / 2, MouseTop + 5),
            new Point(MouseLeft + MouseWidth / 2, MouseTop + MouseHeight - 5));
        dc.DrawRoundedRectangle(_keyDark, _edgePen,
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

    private void PaintDisplay(DrawingContext dc)
    {
        string text = _showLabels ? _trail : "";
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var area = DisplayBounds;
        if (!string.IsNullOrEmpty(text))
        {
            var body = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                _mono, 9.5, _accent, ppd);
            dc.DrawText(body, new Point(150 - body.Width / 2, area.Y + 3));
            if ((_now / 520) % 2 == 0)
                dc.DrawRectangle(_accent, null, new Rect(150 + body.Width / 2 + 2, area.Y + 4, 2.4, area.Height - 8));
            return;
        }

        var idle = new FormattedText(_showLabels ? "MouseDock" : "··", CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, _mono, 8.5, _dim, ppd);
        dc.DrawText(idle, new Point(150 - idle.Width / 2, area.Y + 4));
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
        dc.DrawRoundedRectangle(_keyDark, _keycapPen, new Rect(x, y, width, height), 6, 6);
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            _typeface, narrow ? 12.5 : 9.5, _accent, ppd);
        dc.DrawText(text, new Point(x + (width - text.Width) / 2, y + (height - text.Height) / 2));
        dc.Pop();
    }

    private static Geometry BuildCableGeometry()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(20, 214), false, false);
            context.BezierTo(new Point(-2, 220), new Point(-2, 232), new Point(20, 234), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildTriangle(Point a, Point b, Point c)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(a, true, true);
            context.LineTo(b, true, false);
            context.LineTo(c, true, false);
        }
        geometry.Freeze();
        return geometry;
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
