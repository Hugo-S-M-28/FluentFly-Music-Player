using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
namespace FluentFlyoutWPF.Classes.Utils;

internal static class ThemeResourceHelper
{
    public static bool IsDarkTheme()
        => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

    public static Brush GetPrimaryTextBrush()
        => GetBrush("TextFillColorPrimaryBrush", SystemColors.ControlTextBrush);

    public static Brush GetSecondaryTextBrush()
        => GetBrush("TextFillColorSecondaryBrush", SystemColors.GrayTextBrush);

    public static SolidColorBrush GetPrimaryTextSolidBrush()
        => GetSolidColorBrush("TextFillColorPrimaryBrush", SystemColors.ControlTextBrush);

    public static SolidColorBrush GetSecondaryTextSolidBrush()
        => GetSolidColorBrush("TextFillColorSecondaryBrush", SystemColors.GrayTextBrush);

    public static SolidColorBrush GetControlFillSecondarySolidBrush()
        => GetSolidColorBrush("ControlFillColorSecondaryBrush", SystemColors.ControlLightBrush);

    public static SolidColorBrush GetControlFillTertiarySolidBrush()
        => GetSolidColorBrush("ControlFillColorTertiaryBrush", SystemColors.ControlBrush);

    public static SolidColorBrush GetControlStrokeSolidBrush()
        => GetSolidColorBrush("ControlStrongStrokeColorDefaultBrush", SystemColors.ActiveBorderBrush);

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

    public static SolidColorBrush GetContrastBrush(Color color)
    {
        double luminance = ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255d;
        return luminance > 0.62
            ? new SolidColorBrush(Color.FromRgb(18, 18, 18))
            : new SolidColorBrush(Colors.White);
    }

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
