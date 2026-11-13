using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MouseDock.Models;
using MouseDock.Services;

namespace MouseDock.Controls
{
    public static class DeviceActions
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(DeviceActions), new PropertyMetadata(false, Changed));
        public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);
        public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);
        private static void Changed(DependencyObject obj, DependencyPropertyChangedEventArgs e)
        {
            if (obj is not FrameworkElement element) return;
            if ((bool)e.NewValue)
            {
                element.PreviewMouseRightButtonUp += RightClick;
                element.ContextMenuOpening += KeyboardMenu;
                element.Focusable = true;
                element.ContextMenu = new ContextMenu();
            }
            else
            {
                element.PreviewMouseRightButtonUp -= RightClick;
                element.ContextMenuOpening -= KeyboardMenu;
                element.ClearValue(FrameworkElement.ContextMenuProperty);
            }
        }
        private static void RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && Show(element)) e.Handled = true;
        }
        private static void KeyboardMenu(object sender, ContextMenuEventArgs e)
        {
            if (sender is FrameworkElement element && Show(element)) e.Handled = true;
        }
        private static bool Show(FrameworkElement element)
        {
            if (element.DataContext is not DeviceInfo device || App.Battery == null) return false;
            var menu = CreateMenu(device, App.Battery);
            element.ContextMenu = menu;
            menu.PlacementTarget = element;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
            return true;
        }
        internal static ContextMenu CreateMenu(DeviceInfo device, BatteryService battery)
        {
            var menu = new ContextMenu
            {
                Background = new SolidColorBrush(App.Theme.BgTop),
                Foreground = new SolidColorBrush(App.Theme.Text)
            };
            menu.Items.Add(new MenuItem { Header = device.Name, IsEnabled = false });
            menu.Items.Add(new Separator());
            if (App.SupportsDpi(device))
            {
                var dpi = new MenuItem
                {
                    Header = "DPI 调节…",
                    IsEnabled = device.Connected && device.RecognitionEnabled && !App.Settings.SafeMode
                };
                dpi.Click += (_, __) => App.ShowDpi(device);
                menu.Items.Add(dpi);
                menu.Items.Add(new Separator());
            }
            var cancel = new MenuItem { Header = "取消识别", IsEnabled = device.RecognitionEnabled,
                ToolTip = "停止 MouseDock 的电量查询；仍可在设置中重新识别" };
            cancel.Click += (_, __) => battery.CancelRecognition(device);
            var hide = new MenuItem { Header = "隐藏设备", IsEnabled = !device.Hidden,
                ToolTip = "从所有面板和电量托盘中隐藏；勾选设备即可恢复" };
            hide.Click += (_, __) => battery.HideDevice(device);
            var retry = new MenuItem { Header = "重新识别", ToolTip = "重新查询并恢复显示；等待期间保留旧电量并标记待刷新" };
            retry.Click += (_, __) => battery.Reidentify(device);
            menu.Items.Add(cancel); menu.Items.Add(hide); menu.Items.Add(retry);
            return menu;
        }
    }
}
