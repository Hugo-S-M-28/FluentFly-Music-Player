using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FluentFlyoutWPF.Classes.Utils;

public static class VisualTreeHelperEx
{
    public static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            current = GetParent(current);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject current)
    {
        if (current is Visual or Visual3D)
        {
            try
            {
                return VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
            }
            catch (InvalidOperationException)
            {
                return LogicalTreeHelper.GetParent(current);
            }
        }

        return LogicalTreeHelper.GetParent(current);
    }
}
