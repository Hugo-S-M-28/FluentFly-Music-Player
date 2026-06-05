using System.Windows;
using System.Windows.Media;

namespace FluentFlyoutWPF.Classes.Utils;

internal static class AccentResourceHelper
{
    public static void RefreshAccentResources()
    {
        if (Application.Current == null)
            return;

        ApplyResolvedAccentResources(Application.Current.Resources);

        foreach (Window window in Application.Current.Windows)
        {
            ApplyResolvedAccentResources(window.Resources);
        }
    }

    public static void ApplyResolvedAccentResources(ResourceDictionary resources)
    {
        var accentBrush = AccentColorResolver.ResolveAccentBrush();
        bool useAccent = AccentColorResolver.ShouldUseAccent();
        var secondaryAccentBrush = CreateBrushVariant(accentBrush.Color, useAccent ? 0.32 : 0.18);
        var tertiaryAccentBrush = CreateBrushVariant(accentBrush.Color, useAccent ? 0.18 : 0.10);
        var disabledAccentBrush = CreateBrushVariant(accentBrush.Color, useAccent ? 0.45 : 0.35);

        Set(resources, "AccentFillColorDefaultBrush", accentBrush);
        Set(resources, "AccentFillColorSecondaryBrush", secondaryAccentBrush);
        Set(resources, "AccentFillColorTertiaryBrush", tertiaryAccentBrush);
        Set(resources, "AccentFillColorDisabledBrush", disabledAccentBrush);
        Set(resources, "AccentFillColorSelectedTextBackgroundBrush", secondaryAccentBrush);
        Set(resources, "AccentTextFillColorPrimaryBrush", accentBrush);
        Set(resources, "AccentTextFillColorSecondaryBrush", accentBrush);
        Set(resources, "SystemAccentColorPrimaryBrush", accentBrush);
        Set(resources, "SystemAccentColorSecondaryBrush", secondaryAccentBrush);
        Set(resources, "SystemAccentColorTertiaryBrush", tertiaryAccentBrush);
        Set(resources, "SystemAccentColor", accentBrush.Color);
        Set(resources, "NavigationViewSelectionIndicatorForeground", accentBrush);
        Set(resources, "NavigationViewItemBackgroundSelectedLeftFluent", tertiaryAccentBrush);
    }

    private static void Set(ResourceDictionary resources, object key, object value)
    {
        resources[key] = value;
    }

    internal static SolidColorBrush CreateBrushVariant(Color color, double opacity)
    {
        var variant = new SolidColorBrush(color)
        {
            Opacity = opacity
        };
        variant.Freeze();
        return variant;
    }
}
