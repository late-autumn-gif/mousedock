using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace MouseDock.Controls;

/// <summary>Applies a gentle opacity cycle to a single number, including its optional percent sign.</summary>
public static class ChargeBreath
{
    public static readonly DependencyProperty ActiveProperty = DependencyProperty.RegisterAttached(
        "Active", typeof(bool), typeof(ChargeBreath), new PropertyMetadata(false, Changed));

    public static bool GetActive(DependencyObject target) => (bool)target.GetValue(ActiveProperty);
    public static void SetActive(DependencyObject target, bool value) => target.SetValue(ActiveProperty, value);

    internal static double OpacityAt(double phase) => phase < 0 ? 1 : .83 + .17 * Math.Cos(phase * 2 * Math.PI);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;
        element.Loaded -= Refresh;
        element.Unloaded -= Stop;
        element.IsVisibleChanged -= VisibilityChanged;
        if ((bool)e.NewValue)
        {
            element.Loaded += Refresh;
            element.Unloaded += Stop;
            element.IsVisibleChanged += VisibilityChanged;
        }
        Refresh(element, null);
    }

    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Refresh(sender, null);
    private static void Stop(object sender, RoutedEventArgs e) =>
        ((FrameworkElement)sender).BeginAnimation(UIElement.OpacityProperty, null);

    private static void Refresh(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (!GetActive(element) || !element.IsLoaded || !element.IsVisible || !SystemParameters.ClientAreaAnimation)
        {
            Stop(sender, e);
            return;
        }
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, .66, TimeSpan.FromMilliseconds(900))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }
}
