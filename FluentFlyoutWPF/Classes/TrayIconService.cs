using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Windows;

namespace FluentFlyoutWPF.Classes;

public class TrayIconService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static TrayIconService? _instance;
    private static readonly object _instanceLock = new();

    public static TrayIconService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new TrayIconService();
                }
            }
            return _instance;
        }
    }

    private TrayIconService() { }

    public void OpenSettings()
    {
        SettingsWindow.ShowInstance();
    }

    public void OpenRepository()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/unchihugo/FluentFlyout",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open repository URL");
        }
    }

    public void OpenLogsFolder()
    {
        try
        {
            string logsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentFlyout", "logs");
            if (Directory.Exists(logsPath))
            {
                Process.Start("explorer.exe", logsPath);
            }
            else
            {
                Logger.Warn($"Logs folder does not exist at: {logsPath}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open logs folder");
        }
    }

    public void ReportBug()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/unchihugo/FluentFlyout/issues/new/choose",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open report bug URL");
        }
    }

    public void Quit()
    {
        // This is usually called from NotifyIconQuit_Click in MainWindow
        // MainWindow handles the actual resource cleanup before calling Shutdown
        Application.Current.Shutdown();
    }
}
