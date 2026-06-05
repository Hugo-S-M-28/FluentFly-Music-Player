using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
namespace FluentFlyoutWPF.Classes.Utils;

internal static class ThemeResourceHelper
{
    public static Brush GetPrimaryTextBrush()
        => GetBrush("TextFillColorPrimaryBrush", SystemColors.ControlTextBrush);

    public static Brush GetSecondaryTextBrush()
        => GetBrush("TextFillColorSecondaryBrush", SystemColors.GrayTextBrush);

    public static SolidColorBrush GetSecondaryTextSolidBrush()
        => GetSolidColorBrush("TextFillColorSecondaryBrush", SystemColors.GrayTextBrush);

    public static SolidColorBrush GetCustomAccentOrNeutralBrush()
        => AccentColorResolver.ResolveAccentBrush();

    public static SolidColorBrush GetAccentBrush()
        => GetCustomAccentOrNeutralBrush();

    public static SolidColorBrush GetPlaceholderAccentBrush()
        => GetSolidColorBrush("SystemAccentColorPrimaryBrush", SystemColors.HighlightBrush);

    public static Color GetAlbumArtShadowColor()
        => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark
            ? Color.FromArgb(255, 255, 255, 255)
            : Color.FromArgb(255, 0, 0, 0);

    public static double GetAlbumArtShadowOpacity()
        => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? 0.18 : 0.35;

    private static Brush GetBrush(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static SolidColorBrush GetSolidColorBrush(string key, Brush fallback)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
            return brush;

        if (fallback is SolidColorBrush fallbackBrush)
            return fallbackBrush;

        return new SolidColorBrush(Colors.Gray);
    }
}
