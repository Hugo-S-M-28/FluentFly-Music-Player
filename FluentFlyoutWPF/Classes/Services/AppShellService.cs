using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Pages;

namespace FluentFlyoutWPF.Classes.Services;

public class AppShellService : IAppShellService
{
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
        TrayIconService.Instance.OpenLogsFolder();
    }

    public void ReportBug()
    {
        TrayIconService.Instance.ReportBug();
    }
}
