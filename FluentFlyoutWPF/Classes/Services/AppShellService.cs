using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Pages;
using NLog;
using System.Diagnostics;

namespace FluentFlyoutWPF.Classes.Services;

public class AppShellService : IAppShellService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IWindowManager _windowManager;

    public AppShellService(IWindowManager windowManager)
    {
        _windowManager = windowManager;
    }

    public void OpenSettings()
    {
        _windowManager.NavigateSettings(typeof(SystemPage));
    }

    public void OpenRepository()
    {
        TrayIconService.Instance.OpenRepository();
    }

    public void OpenLogsFolder()
    {
        try
        {
            string logsPath = FileSystemHelper.GetLogsPath();
            Process.Start("explorer.exe", logsPath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to open logs folder");
            throw;
        }
    }

    public void ReportBug()
    {
        TrayIconService.Instance.ReportBug();
    }
}
