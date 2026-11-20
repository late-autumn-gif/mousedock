using System;
using System.Windows;
using System.Windows.Media;

namespace MouseDock.Controls;

/// <summary>德蕾琪娜的微表情状态；键鼠输入优先，其次充电/低电，空闲才轮到随机动作。</summary>
internal enum DracenaMood { Smile, Focus, Happy, Sleepy, Surprised, Yawn }

/// <summary>
/// 德蕾琪娜的分层矢量形象：后发/编发/躯干/头部/眼睛/刘海/发饰分层绘制。
/// 这是分层动画的可运行原型：真正的立绘分层 PNG 可以按同一 API 替换本控件，
/// 桌面场景、键鼠同步和微表情调度不需要改动。
/// </summary>
internal sealed class DracenaPetRig : FrameworkElement
{
    internal const double RigWidth = 208;
    internal const double RigHeight = 208;

    private const double HeadX = 104;
    private const double HeadY = 74;
    private const double HeadRX = 43;
    private const double HeadRY = 41;
    private const double VisibleEyeX = 126;
    private const double VisibleEyeY = 80;
    private const double MouthX = 112;
    private const double MouthY = 102;

    private static readonly Brush Skin = Frozen(Color.FromRgb(247, 223, 210));
    private static readonly Brush SkinShade = Frozen(Color.FromRgb(232, 188, 172));
    private static readonly Brush Blush = Frozen(Color.FromArgb(96, 240, 140, 150));
    private static readonly Brush Hair = Frozen(Color.FromRgb(206, 210, 222));
    private static readonly Brush HairShade = Frozen(Color.FromRgb(166, 172, 190));
    private static readonly Brush HairLight = Frozen(Color.FromRgb(235, 237, 245));
    private static readonly Brush HairGradient = BuildHairGradient();
    private static readonly Brush Dress = Frozen(Color.FromRgb(24, 31, 52));
    private static readonly Brush DressLight = Frozen(Color.FromRgb(38, 48, 78));
    private static readonly Brush Gold = Frozen(Color.FromRgb(232, 184, 91));
    private static readonly Brush GoldLight = Frozen(Color.FromRgb(255, 220, 140));
    private static readonly Brush BlueFlower = Frozen(Color.FromRgb(91, 141, 239));
    private static readonly Brush BlueFlowerLight = Frozen(Color.FromRgb(146, 182, 255));
    private static readonly Brush EyeWhite = Frozen(Color.FromRgb(252, 250, 246));
    private static readonly Brush IrisGold = Frozen(Color.FromRgb(242, 193, 78));
    private static readonly Brush Pupil = Frozen(Color.FromRgb(58, 42, 24));
    private static readonly Brush Mouth = Frozen(Color.FromRgb(196, 86, 106));
    private static readonly Brush Ink = Frozen(Color.FromRgb(46, 40, 58));

    private static readonly Pen LinePen = PenOf(Ink, .9);
    private static readonly Pen HairPen = PenOf(HairShade, .8);
    private static readonly Pen GoldPen = PenOf(Gold, 1.5);
    private static readonly Pen SoftPen = PenOf(SkinShade, .7);

    private static readonly Geometry BackHair = BuildBackHair();
    private static readonly Geometry Bangs = BuildBangs();
    private static readonly Geometry SideBang = BuildSideBang();
    private static readonly Geometry Torso = BuildTorso();

    private readonly Random _random = new(2617);
    private long _nextBlink;
    private long _blinkUntil;
    private long _nextYawn;
    private long _yawnUntil;
    private long _now;
    private double _look;

    /// <summary>本帧的微表情，供回归检查读取。</summary>
    internal DracenaMood Mood { get; private set; } = DracenaMood.Smile;

    /// <summary>本帧是否闭眼。</summary>
    internal bool Blinking { get; private set; }

    internal DracenaPetRig()
    {
        Width = RigWidth;
        Height = RigHeight;
        IsHitTestVisible = false;
    }

    /// <summary>每帧更新表情调度；motion=false（减少动态效果）时保持静态微笑。</summary>
    internal void Render(long now, bool motion, double lookAngle, bool typing, bool mouseActive, bool charging, bool low, bool happy)
    {
        _now = now;
        _look = Math.Clamp(lookAngle, -1, 1);
        if (_nextBlink == 0) _nextBlink = now + 2200 + _random.Next(0, 3600);
        if (_nextYawn == 0) _nextYawn = now + 9000 + _random.Next(0, 6000);

        if (motion)
        {
            if (now >= _nextBlink)
            {
                _blinkUntil = now + 130;
                _nextBlink = now + 2700 + _random.Next(0, 3800);
            }
            Blinking = now < _blinkUntil;

            // 输入优先：键鼠活跃时推迟打哈欠等随机动作；停止输入后至少空闲 9 秒才触发。
            if (typing || mouseActive)
            {
                _yawnUntil = 0;
                _nextYawn = Math.Max(_nextYawn, now + 9000);
            }
            else if (now >= _nextYawn)
            {
                _yawnUntil = now + 900;
                _nextYawn = now + 12000 + _random.Next(0, 7000);
            }
        }
        else
        {
            Blinking = false;
            _yawnUntil = 0;
        }

        if (typing || mouseActive) Mood = DracenaMood.Focus;
        else if (low) Mood = DracenaMood.Sleepy;
        else if (charging) Mood = DracenaMood.Happy;
        else if (now < _yawnUntil) Mood = DracenaMood.Yawn;
        else if (happy) Mood = DracenaMood.Happy;
        else Mood = DracenaMood.Smile;

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, RigWidth, RigHeight)));

        // 头部绕颈部轻微倾斜，跟随鼠标/悬停方向；身体不动。
        dc.PushTransform(new RotateTransform(_look * 1.6, HeadX, 132));

        DrawBackHair(dc);
        DrawBraid(dc);
        DrawNeck(dc);
        DrawTorso(dc);
        DrawHead(dc);
        DrawFace(dc);
        DrawBangs(dc);
        DrawHairPin(dc);

        dc.Pop();
        dc.Pop();
    }

    private void DrawBackHair(DrawingContext dc)
    {
        dc.DrawGeometry(HairGradient, HairPen, BackHair);
        dc.DrawEllipse(Hair, HairPen, new Point(56, 104), 13, 24);
        dc.DrawEllipse(Hair, HairPen, new Point(152, 104), 12, 22);
        // 银灰高光与发丝
        dc.DrawEllipse(HairLight, null, new Point(86, 36), 22, 8);
        dc.DrawLine(PenOf(HairLight, 1.2), new Point(70, 48), new Point(62, 88));
        dc.DrawLine(PenOf(HairLight, 1.2), new Point(140, 46), new Point(148, 86));
    }

    private void DrawBraid(DrawingContext dc)
    {
        var braid = new StreamGeometry();
        using (var context = braid.Open())
        {
            context.BeginFigure(new Point(62, 96), false, false);
            context.BezierTo(new Point(50, 112), new Point(48, 126), new Point(44, 142), true, false);
        }
        braid.Freeze();
        dc.DrawGeometry(null, PenOf(HairShade, 13), braid);
        dc.DrawGeometry(null, PenOf(Hair, 10), braid);
        dc.DrawGeometry(null, PenOf(HairLight, 2.2), braid);
        dc.DrawRectangle(Gold, null, new Rect(46, 122, 9, 3));
        dc.DrawRectangle(Gold, null, new Rect(43, 135, 8, 3));
    }

    private void DrawNeck(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Skin, null, new Rect(96, 104, 16, 26), 6, 6);
    }

    private void DrawTorso(DrawingContext dc)
    {
        dc.DrawGeometry(Dress, null, Torso);
        dc.DrawGeometry(DressLight, null, BuildChest());
        dc.DrawEllipse(Dress, LinePen, new Point(66, 138), 18, 14);
        dc.DrawEllipse(Dress, LinePen, new Point(142, 138), 18, 14);
        dc.DrawLine(GoldPen, new Point(85, 126), new Point(104, 142));
        dc.DrawLine(GoldPen, new Point(123, 126), new Point(104, 142));
        dc.DrawLine(GoldPen, new Point(104, 142), new Point(104, 208));
        dc.DrawEllipse(Gold, null, new Point(104, 140), 3, 3);
        // 领口蓝花
        for (int i = 0; i < 5; i++)
        {
            double angle = i * Math.PI * 2 / 5 - Math.PI / 2;
            dc.DrawEllipse(i % 2 == 0 ? BlueFlower : BlueFlowerLight, null,
                new Point(92 + Math.Cos(angle) * 6, 130 + Math.Sin(angle) * 6), 4.2, 3.2);
        }
        dc.DrawEllipse(Gold, null, new Point(92, 130), 2.6, 2.6);
        // 袖口黑手套
        dc.DrawEllipse(Frozen(Color.FromRgb(20, 22, 34)), null, new Point(66, 146), 8, 5);
        dc.DrawEllipse(Frozen(Color.FromRgb(20, 22, 34)), null, new Point(142, 146), 8, 5);
    }

    private void DrawHead(DrawingContext dc)
    {
        dc.DrawEllipse(Skin, SoftPen, new Point(HeadX, HeadY), HeadRX, HeadRY);
    }

    private void DrawFace(DrawingContext dc)
    {
        dc.DrawEllipse(Blush, null, new Point(140, 94), 7, 3.4);
        dc.DrawEllipse(Blush, null, new Point(72, 94), 6, 3);

        switch (Mood)
        {
            case DracenaMood.Happy:
                DrawClosedEye(dc, happy: true);
                DrawMouth(dc, open: true, smile: 1);
                break;
            case DracenaMood.Yawn:
                DrawClosedEye(dc, happy: false);
                DrawMouth(dc, open: true, smile: 0);
                break;
            case DracenaMood.Sleepy:
                DrawSleepyEye(dc);
                DrawMouth(dc, open: false, smile: .3);
                break;
            case DracenaMood.Focus:
                DrawOpenEye(dc, 7.6, 5.4);
                DrawBrow(dc, focus: true);
                DrawMouth(dc, open: false, smile: 0);
                break;
            case DracenaMood.Surprised:
                DrawOpenEye(dc, 9.6, 7.6);
                DrawBrow(dc, focus: false);
                DrawMouth(dc, open: true, smile: 0);
                break;
            default:
                DrawOpenEye(dc, 10, 8);
                DrawBrow(dc, focus: false);
                DrawMouth(dc, open: false, smile: .8);
                break;
        }

        dc.DrawLine(SoftPen, new Point(108, 92), new Point(110, 93));
    }

    private void DrawOpenEye(DrawingContext dc, double width, double height)
    {
        if (Blinking)
        {
            DrawClosedEye(dc, happy: false);
            return;
        }
        var center = new Point(VisibleEyeX, VisibleEyeY);
        dc.DrawEllipse(EyeWhite, null, center, width, height);
        dc.DrawEllipse(IrisGold, PenOf(Ink, .6), new Point(center.X + .6, center.Y), height * .8, height * .8);
        dc.DrawEllipse(Pupil, null, new Point(center.X + .6, center.Y + .4), height * .42, height * .46);
        dc.DrawEllipse(Frozen(Colors.White), null, new Point(center.X - 1.4, center.Y - 2), 1.9, 2.2);
        dc.DrawEllipse(Frozen(Colors.White), null, new Point(center.X + 2.4, center.Y + 2), 1, 1.2);
    }

    private void DrawSleepyEye(DrawingContext dc)
    {
        var center = new Point(VisibleEyeX, VisibleEyeY);
        dc.DrawEllipse(EyeWhite, null, center, 9.4, 7.4);
        dc.DrawEllipse(IrisGold, null, new Point(center.X, center.Y + 1.4), 5, 4.2);
        dc.DrawEllipse(Pupil, null, new Point(center.X, center.Y + 1.8), 2.4, 2.6);
        dc.DrawRectangle(Skin, null, new Rect(center.X - 10, center.Y - 9, 20, 8.6));
        dc.DrawLine(LinePen, new Point(center.X - 9, center.Y + 1.5), new Point(center.X + 9, center.Y + 1.5));
    }

    private void DrawClosedEye(DrawingContext dc, bool happy)
    {
        var eye = new StreamGeometry();
        using (var context = eye.Open())
        {
            context.BeginFigure(new Point(VisibleEyeX - 8, VisibleEyeY + (happy ? 1.5 : 0)), false, false);
            context.QuadraticBezierTo(new Point(VisibleEyeX, VisibleEyeY + (happy ? -4.5 : 3.5)),
                new Point(VisibleEyeX + 8, VisibleEyeY + (happy ? 1.5 : 0)), true, false);
        }
        eye.Freeze();
        dc.DrawGeometry(null, PenOf(Ink, 1.4), eye);
    }

    private void DrawBrow(DrawingContext dc, bool focus)
    {
        var brow = new StreamGeometry();
        using (var context = brow.Open())
        {
            context.BeginFigure(new Point(VisibleEyeX - 8, 66 + (focus ? 1 : 0)), false, false);
            context.QuadraticBezierTo(new Point(VisibleEyeX, 62 + (focus ? 2 : 0)),
                new Point(VisibleEyeX + 8, 66 + (focus ? 2 : 1)), true, false);
        }
        brow.Freeze();
        dc.DrawGeometry(null, PenOf(HairShade, 1.6), brow);
    }

    private void DrawMouth(DrawingContext dc, bool open, double smile)
    {
        if (open)
        {
            dc.DrawEllipse(Mouth, PenOf(Ink, .6), new Point(MouthX, MouthY + 1),
                Mood == DracenaMood.Yawn ? 6.4 : 4.8, Mood == DracenaMood.Yawn ? 6.8 : 4.2);
            return;
        }
        var mouth = new StreamGeometry();
        using (var context = mouth.Open())
        {
            context.BeginFigure(new Point(MouthX - 6, MouthY), false, false);
            context.QuadraticBezierTo(new Point(MouthX, MouthY + 5 * smile),
                new Point(MouthX + 6, MouthY), true, false);
        }
        mouth.Freeze();
        dc.DrawGeometry(null, PenOf(Mouth, 1.4), mouth);
    }

    private void DrawBangs(DrawingContext dc)
    {
        dc.DrawGeometry(HairGradient, HairPen, Bangs);
        dc.DrawGeometry(HairGradient, HairPen, SideBang);
        dc.DrawLine(PenOf(HairLight, 1.4), new Point(70, 54), new Point(78, 112));
        dc.DrawEllipse(HairShade, null, new Point(90, 50), 15, 6);
        dc.DrawEllipse(Hair, null, new Point(120, 38), 20, 9);
        dc.DrawLine(PenOf(HairLight, 1.1), new Point(118, 46), new Point(132, 62));
    }

    private void DrawHairPin(DrawingContext dc)
    {
        var center = new Point(72, 40);
        for (int i = 0; i < 5; i++)
        {
            double angle = i * Math.PI * 2 / 5 - Math.PI / 2;
            dc.DrawEllipse(BlueFlower, null, new Point(center.X + Math.Cos(angle) * 6, center.Y + Math.Sin(angle) * 6), 4.6, 3.6);
        }
        dc.DrawEllipse(GoldLight, null, center, 3, 3);
        dc.DrawLine(GoldPen, new Point(center.X + 5, center.Y + 4), new Point(center.X + 12, center.Y + 18));
        dc.DrawLine(GoldPen, new Point(center.X + 3, center.Y + 4), new Point(center.X + 6, center.Y + 20));
    }

    /// <summary>卡片头像：把同一套头部/刘海/眼睛绘制缩放到 256 画布，不依赖外部图片。</summary>
    internal static ImageSource CreatePortrait()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.PushTransform(new ScaleTransform(1.5, 1.5));
            dc.PushTransform(new TranslateTransform(-28, 9));
            dc.DrawGeometry(HairGradient, HairPen, BackHair);
            dc.DrawEllipse(Hair, HairPen, new Point(56, 104), 13, 24);
            dc.DrawEllipse(Hair, HairPen, new Point(152, 104), 12, 22);
            dc.DrawEllipse(HairLight, null, new Point(86, 36), 22, 8);
            dc.DrawEllipse(Skin, SoftPen, new Point(HeadX, HeadY), HeadRX, HeadRY);
            dc.DrawEllipse(Blush, null, new Point(140, 94), 7, 3.4);
            dc.DrawEllipse(EyeWhite, null, new Point(VisibleEyeX, VisibleEyeY), 10, 8);
            dc.DrawEllipse(IrisGold, PenOf(Ink, .6), new Point(VisibleEyeX, VisibleEyeY), 6.4, 6.4);
            dc.DrawEllipse(Pupil, null, new Point(VisibleEyeX, VisibleEyeY), 2.9, 3);
            dc.DrawEllipse(Frozen(Colors.White), null, new Point(VisibleEyeX - 1.4, VisibleEyeY - 2), 1.9, 2.2);
            var brow = new StreamGeometry();
            using (var context = brow.Open())
            {
                context.BeginFigure(new Point(VisibleEyeX - 8, 66), false, false);
                context.QuadraticBezierTo(new Point(VisibleEyeX, 62), new Point(VisibleEyeX + 8, 66), true, false);
            }
            brow.Freeze();
            dc.DrawGeometry(null, PenOf(HairShade, 1.6), brow);
            var mouth = new StreamGeometry();
            using (var context = mouth.Open())
            {
                context.BeginFigure(new Point(MouthX - 6, MouthY), false, false);
                context.QuadraticBezierTo(new Point(MouthX, MouthY + 4), new Point(MouthX + 6, MouthY), true, false);
            }
            mouth.Freeze();
            dc.DrawGeometry(null, PenOf(Mouth, 1.4), mouth);
            dc.DrawGeometry(HairGradient, HairPen, Bangs);
            dc.DrawGeometry(HairGradient, HairPen, SideBang);
            dc.DrawLine(PenOf(HairLight, 1.4), new Point(70, 54), new Point(78, 112));
            var pin = new Point(72, 40);
            for (int i = 0; i < 5; i++)
            {
                double angle = i * Math.PI * 2 / 5 - Math.PI / 2;
                dc.DrawEllipse(BlueFlower, null, new Point(pin.X + Math.Cos(angle) * 6, pin.Y + Math.Sin(angle) * 6), 4.6, 3.6);
            }
            dc.DrawEllipse(GoldLight, null, pin, 3, 3);
            dc.Pop();
            dc.Pop();
        }
        group.Freeze();
        return new DrawingImage(group);
    }

    private static Geometry BuildBackHair()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(58, 60), true, true);
            context.BezierTo(new Point(58, 28), new Point(84, 14), new Point(104, 14), true, false);
            context.BezierTo(new Point(128, 14), new Point(150, 30), new Point(150, 60), true, false);
            context.BezierTo(new Point(152, 92), new Point(148, 114), new Point(136, 126), true, false);
            context.BezierTo(new Point(126, 134), new Point(114, 136), new Point(104, 136), true, false);
            context.BezierTo(new Point(92, 136), new Point(80, 134), new Point(70, 126), true, false);
            context.BezierTo(new Point(58, 112), new Point(54, 90), new Point(58, 60), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildBangs()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(60, 58), true, true);
            context.BezierTo(new Point(60, 30), new Point(82, 18), new Point(104, 18), true, false);
            context.BezierTo(new Point(130, 18), new Point(148, 30), new Point(150, 56), true, false);
            context.BezierTo(new Point(150, 62), new Point(147, 68), new Point(143, 73), true, false);
            context.BezierTo(new Point(138, 62), new Point(133, 55), new Point(129, 49), true, false);
            context.BezierTo(new Point(126, 61), new Point(120, 65), new Point(114, 67), true, false);
            context.BezierTo(new Point(112, 54), new Point(110, 47), new Point(108, 43), true, false);
            context.BezierTo(new Point(100, 57), new Point(94, 63), new Point(88, 65), true, false);
            context.BezierTo(new Point(86, 52), new Point(84, 45), new Point(82, 41), true, false);
            context.BezierTo(new Point(75, 52), new Point(69, 58), new Point(63, 67), true, false);
            context.LineTo(new Point(60, 58), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildSideBang()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(64, 46), true, true);
            context.BezierTo(new Point(56, 72), new Point(56, 96), new Point(62, 114), true, false);
            context.BezierTo(new Point(66, 121), new Point(74, 121), new Point(79, 112), true, false);
            context.BezierTo(new Point(73, 92), new Point(73, 70), new Point(77, 50), true, false);
            context.BezierTo(new Point(72, 47), new Point(68, 46), new Point(64, 46), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildTorso()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(82, 120), true, true);
            context.BezierTo(new Point(76, 128), new Point(70, 150), new Point(62, 208), true, false);
            context.LineTo(new Point(146, 208), true, false);
            context.BezierTo(new Point(138, 150), new Point(132, 128), new Point(126, 120), true, false);
            context.BezierTo(new Point(118, 132), new Point(90, 132), new Point(82, 120), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Geometry BuildChest()
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(86, 126), true, true);
            context.BezierTo(new Point(92, 136), new Point(116, 136), new Point(122, 126), true, false);
            context.BezierTo(new Point(120, 150), new Point(118, 176), new Point(112, 202), true, false);
            context.LineTo(new Point(96, 202), true, false);
            context.BezierTo(new Point(90, 176), new Point(88, 150), new Point(86, 126), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    private static Brush BuildHairGradient()
    {
        var gradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1)
        };
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(235, 237, 245), 0));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(206, 210, 222), .45));
        gradient.GradientStops.Add(new GradientStop(Color.FromRgb(166, 172, 190), 1));
        gradient.Freeze();
        return gradient;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen PenOf(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
