using System;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Views
{
    /// <summary>悬浮图标：角色款 / 照片款 / 迷你款三种形态，共用同一个 96×96 的圆角外壳。</summary>
    public partial class IconWindow : Window
    {
        private readonly ScaleTransform _photoScale = new ScaleTransform(1, 1, 0.5, 0.5);
        private readonly TranslateTransform _photoOffset = new TranslateTransform(0, 0);
        private readonly TransformGroup _photoTransform = new TransformGroup();

        private bool _dragging;
        private Point _dragStart;
        private double _dragOriginX, _dragOriginY;

        public IconWindow()
        {
            InitializeComponent();
            _photoTransform.Children.Add(_photoScale);
            _photoTransform.Children.Add(_photoOffset);
            PhotoBrush.RelativeTransform = _photoTransform;

            App.Battery.Refreshed += (_, __) => UpdateValue();
            Loaded += (_, __) =>
            {
                ApplyMode(App.Settings.IconMode, false);
                UpdateValue();
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            };
        }

        public void ApplyMode(string mode, bool persist)
        {
            mode = (mode ?? "character").ToLowerInvariant();
            if (persist) App.Settings.IconMode = mode;

            CharLayer.Visibility = mode == "character" ? Visibility.Visible : Visibility.Collapsed;
            PhotoLayer.Visibility = mode == "photo" ? Visibility.Visible : Visibility.Collapsed;
            MiniLayer.Visibility = mode == "mini" ? Visibility.Visible : Visibility.Collapsed;

            if (mode == "character") LoadCharacter();
            if (mode == "photo") LoadPhoto();
            if (mode == "mini") UpdateValue();
        }

        public void LoadCharacter()
        {
            string path = App.Settings.CharacterAsset;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                path = App.CharacterAssets.Count > 0 ? App.CharacterAssets[0] : null;

            var bmp = Palette.Load(path);
            CharBrush.ImageSource = bmp;
            CharBrush.Stretch = Stretch.UniformToFill;
        }

        public void LoadPhoto()
        {
            string path = App.Settings.PhotoAsset;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                path = App.DefaultPhotoAsset;

            var bmp = Palette.Load(path);
            PhotoBrush.ImageSource = bmp;
            PhotoBrush.Stretch = Stretch.UniformToFill;
            ApplyPhotoTransform();
        }

        public void UpdateValue()
        {
            var dev = App.PrimaryDevice;
            string text = dev == null ? "--" : dev.LevelText;
            CharPlateText.Text = text;
            PhotoPlateText.Text = text;
            MiniText.Text = text;
            if (dev != null)
            {
                MiniGlyph.Kind = dev.Kind;
                CharPlateText.Foreground = Brushes.White;
            }
        }

        public void ApplyPhotoTransform()
        {
            double zoom = Math.Clamp(App.Settings.PhotoZoom, 1, 4);
            _photoScale.ScaleX = zoom;
            _photoScale.ScaleY = zoom;

            double limit = (zoom - 1) / 2.0;
            App.Settings.PhotoOffsetX = Math.Clamp(App.Settings.PhotoOffsetX, -limit, limit);
            App.Settings.PhotoOffsetY = Math.Clamp(App.Settings.PhotoOffsetY, -limit, limit);
            _photoOffset.X = App.Settings.PhotoOffsetX;
            _photoOffset.Y = App.Settings.PhotoOffsetY;
        }

        public void ApplyTheme(ThemePreset theme, bool animate)
        {
            var text = new SolidColorBrush(theme.Text);
            TextElement.SetForeground(MiniLayer, text);
            MiniText.Foreground = text;
            MiniGlyph.GlyphBrush = new SolidColorBrush(Palette.Mix(theme.Text, theme.Accent, 0.35));

            if (animate)
            {
                var dur = TimeSpan.FromMilliseconds(240);
                MiniTop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.BgTop, dur));
                MiniBottom.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.BgBottom, dur));
            }
            else
            {
                MiniTop.Color = theme.BgTop;
                MiniBottom.Color = theme.BgBottom;
            }
        }

        private void OnDragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            try { DragMove(); } catch { }
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (PhotoLayer.Visibility != Visibility.Visible) return;
            e.Handled = true;
            double factor = e.Delta > 0 ? 1.08 : 1 / 1.08;
            App.Settings.PhotoZoom = Math.Clamp(App.Settings.PhotoZoom * factor, 1.0, 4.0);
            ApplyPhotoTransform();
            App.SaveSettings();
        }

        private void OnPhotoMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _dragStart = e.GetPosition(this);
            _dragOriginX = App.Settings.PhotoOffsetX;
            _dragOriginY = App.Settings.PhotoOffsetY;
            _dragging = true;
            PhotoLayer.CaptureMouse();
            e.Handled = true;
        }

        private void OnPhotoMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || App.Settings.PhotoZoom <= 1.001) return;
            var p = e.GetPosition(this);
            App.Settings.PhotoOffsetX = _dragOriginX + (p.X - _dragStart.X) / 96.0;
            App.Settings.PhotoOffsetY = _dragOriginY + (p.Y - _dragStart.Y) / 96.0;
            ApplyPhotoTransform();
        }

        private void OnPhotoMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            PhotoLayer.ReleaseMouseCapture();
            App.SaveSettings();
        }

        private void OnContextMenu(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            App.ShowQuickMenu(this);
        }
    }
}
