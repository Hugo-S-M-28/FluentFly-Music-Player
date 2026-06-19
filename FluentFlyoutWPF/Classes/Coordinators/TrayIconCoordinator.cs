using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyout.Classes.Settings;
using Wpf.Ui.Appearance;
using Wpf.Ui.Tray.Controls;

namespace FluentFlyoutWPF.Classes.Coordinators;

public class TrayIconCoordinator
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private const string DefaultIconUri = "pack://application:,,,/Resources/FluentFlyout2.ico";
    private readonly Func<string, ImageSource> _buildIcon;

    public TrayIconCoordinator()
        : this(BuildIconBitmap)
    {
    }

    internal TrayIconCoordinator(Func<string, ImageSource> buildIcon)
    {
        _buildIcon = buildIcon;
    }

    /// <summary>
    /// Recreates or updates the tray icon based on current settings and theme with retry/fallback.
    /// </summary>
    public Task RecreateTrayIconAsync(NotifyIcon nIcon, int maxAttempts = 3, int delayMs = 500)
    {
        return RecreateTrayIconAsync(new NotifyIconAdapter(nIcon), maxAttempts, delayMs);
    }

    internal async Task RecreateTrayIconAsync(ITrayNotifyIcon nIcon, int maxAttempts = 3, int delayMs = 500)
    {
        maxAttempts = Math.Max(1, maxAttempts);
        delayMs = Math.Max(0, delayMs);

        if (SettingsManager.Current.NIconHide)
        {
            if (nIcon.IsRegistered)
            {
                nIcon.Unregister();
                nIcon.Icon = null;
                Logger.Info("Tray icon unregistered because NIconHide is enabled");
            }

            return;
        }

        bool preferSymbolIcon = SettingsManager.Current.NIconSymbol;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            bool allowSymbolIcon = preferSymbolIcon && attempt < maxAttempts;
            string iconUri = ResolveIconUri(allowSymbolIcon);

            try
            {
                Logger.Info(
                    $"Tray icon registration attempt {attempt}/{maxAttempts}: NIconHide={SettingsManager.Current.NIconHide}, NIconSymbol={SettingsManager.Current.NIconSymbol}, Resource={iconUri}, PreviouslyRegistered={nIcon.IsRegistered}");

                if (nIcon.IsRegistered)
                {
                    nIcon.Unregister();
                }

                nIcon.Icon = null;
                nIcon.Icon = _buildIcon(iconUri);
                nIcon.TooltipText = "FluentFlyout";
                nIcon.Visibility = Visibility.Visible;
                nIcon.Register();

                if (nIcon.IsRegistered)
                {
                    Logger.Info(
                        $"Tray icon registered successfully on attempt {attempt} (FallbackUsed={!allowSymbolIcon && preferSymbolIcon})");
                    return;
                }

                Logger.Warn($"Tray icon Register() returned without IsRegistered=true on attempt {attempt}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Tray icon registration attempt {attempt}/{maxAttempts} failed");
            }

            if (attempt < maxAttempts)
            {
                await Task.Delay(delayMs);
            }
        }

        Logger.Error($"Failed to register tray icon after {maxAttempts} attempts");
    }

    internal static string ResolveIconUri(bool allowSymbolIcon)
    {
        if (allowSymbolIcon)
        {
            var appTheme = ApplicationThemeManager.GetAppTheme();
            return appTheme == ApplicationTheme.Dark
                ? "pack://application:,,,/Resources/TrayIcons/FluentFlyoutWhite.png"
                : "pack://application:,,,/Resources/TrayIcons/FluentFlyoutBlack.png";
        }

        return DefaultIconUri;
    }

    private static BitmapImage BuildIconBitmap(string iconUri)
    {
        BitmapImage image = new();
        image.BeginInit();
        image.UriSource = new Uri(iconUri, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public void OpenSettings() => TrayIconService.Instance.OpenSettings();

    public void OpenRepository() => TrayIconService.Instance.OpenRepository();

    public void OpenLogsFolder() => TrayIconService.Instance.OpenLogsFolder();

    public void ReportBug() => TrayIconService.Instance.ReportBug();
}

internal interface ITrayNotifyIcon
{
    bool IsRegistered { get; }
    ImageSource? Icon { get; set; }
    string TooltipText { get; set; }
    Visibility Visibility { get; set; }
    void Register();
    void Unregister();
}

internal sealed class NotifyIconAdapter : ITrayNotifyIcon
{
    private readonly NotifyIcon _notifyIcon;

    public NotifyIconAdapter(NotifyIcon notifyIcon)
    {
        _notifyIcon = notifyIcon;
    }

    public bool IsRegistered => _notifyIcon.IsRegistered;
    public ImageSource? Icon
    {
        get => _notifyIcon.Icon;
        set => _notifyIcon.Icon = value!;
    }

    public string TooltipText
    {
        get => _notifyIcon.TooltipText;
        set => _notifyIcon.TooltipText = value;
    }

    public Visibility Visibility
    {
        get => _notifyIcon.Visibility;
        set => _notifyIcon.Visibility = value;
    }

    public void Register() => _notifyIcon.Register();

    public void Unregister() => _notifyIcon.Unregister();
}
