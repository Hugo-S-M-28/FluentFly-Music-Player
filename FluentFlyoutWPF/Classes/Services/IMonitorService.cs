using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Classes.Services;

public interface IMonitorService
{
    IReadOnlyList<MonitorOption> GetMonitorOptions();
    IReadOnlyList<MonitorUtil.MonitorInfo> GetMonitors();
}
