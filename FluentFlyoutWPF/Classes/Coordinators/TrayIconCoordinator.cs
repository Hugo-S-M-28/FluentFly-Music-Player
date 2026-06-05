using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Classes;
using MicaWPF.Core.Enums;
using Wpf.Ui.Appearance;

namespace FluentFlyoutWPF.Classes.Coordinators;

public class TrayIconCoordinator
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public void RecreateTrayIconSafely(Wpf.Ui.Tray.Controls.NotifyIcon nIcon)
    {
        try
        {
            nIcon.Visibility = Visibility.Collapsed;

            if (!SettingsManager.Current.NIconHide)
            {
                if (SettingsManager.Current.NIconSymbol == true)
                {
                    var appTheme = ApplicationThemeManager.GetAppTheme();
                    var iconUri = new Uri(appTheme == Wpf.Ui.Appearance.ApplicationTheme.Dark
                        ? "pack://application:,,,/Resources/TrayIcons/FluentFlyoutWhite.png"
                        : "pack://application:,,,/Resources/TrayIcons/FluentFlyoutBlack.png");
                    nIcon.Icon = new BitmapImage(iconUri);
                }
                else
                {
                    var iconUi = new Uri("pack://application:,,,/Resources/FluentFlyout2.ico");
                    nIcon.Icon = new BitmapImage(iconUi);
                }

                nIcon.Visibility = Visibility.Visible;
                nIcon.Register();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to recreate tray icon safely");
        }
    }

    public void OpenSettings() => TrayIconService.Instance.OpenSettings();

    public void OpenRepository() => TrayIconService.Instance.OpenRepository();

    public void OpenLogsFolder() => TrayIconService.Instance.OpenLogsFolder();

    public void ReportBug() => TrayIconService.Instance.ReportBug();
}
