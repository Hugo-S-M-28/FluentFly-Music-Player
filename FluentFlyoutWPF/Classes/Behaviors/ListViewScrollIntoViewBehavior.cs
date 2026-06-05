using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Classes.Behaviors;

public static class ListViewScrollIntoViewBehavior
{
    public static readonly DependencyProperty ScrollTargetProperty =
        DependencyProperty.RegisterAttached(
            "ScrollTarget",
            typeof(object),
            typeof(ListViewScrollIntoViewBehavior),
            new PropertyMetadata(null, OnScrollTargetChanged));

    public static object? GetScrollTarget(DependencyObject obj) => obj.GetValue(ScrollTargetProperty);
    public static void SetScrollTarget(DependencyObject obj, object? value) => obj.SetValue(ScrollTargetProperty, value);

    private static void OnScrollTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListView listView && e.NewValue != null)
        {
            listView.Dispatcher.BeginInvoke(() => listView.ScrollIntoView(e.NewValue));
        }
    }
}
