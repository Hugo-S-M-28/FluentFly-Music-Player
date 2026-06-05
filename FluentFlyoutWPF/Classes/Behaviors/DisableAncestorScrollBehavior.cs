using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluentFlyoutWPF.Classes.Behaviors;

public static class DisableAncestorScrollBehavior
{
    public static readonly DependencyProperty DisableVerticalAncestorScrollProperty =
        DependencyProperty.RegisterAttached(
            "DisableVerticalAncestorScroll",
            typeof(bool),
            typeof(DisableAncestorScrollBehavior),
            new PropertyMetadata(false, OnDisableVerticalAncestorScrollChanged));

    public static bool GetDisableVerticalAncestorScroll(DependencyObject obj) =>
        (bool)obj.GetValue(DisableVerticalAncestorScrollProperty);

    public static void SetDisableVerticalAncestorScroll(DependencyObject obj, bool value) =>
        obj.SetValue(DisableVerticalAncestorScrollProperty, value);

    private static void OnDisableVerticalAncestorScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= ElementOnLoaded;
        if ((bool)e.NewValue)
        {
            element.Loaded += ElementOnLoaded;
        }
    }

    private static void ElementOnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DependencyObject target)
        {
            return;
        }

        var parent = VisualTreeHelper.GetParent(target);
        while (parent != null)
        {
            if (parent is ScrollViewer scrollViewer)
            {
                scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                break;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }
    }
}
