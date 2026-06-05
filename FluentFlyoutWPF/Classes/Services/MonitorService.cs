using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Models;
using System.Linq;

namespace FluentFlyoutWPF.Classes.Services;

public sealed class MonitorService : IMonitorService
{
    public IReadOnlyList<MonitorOption> GetMonitorOptions()
    {
        return MonitorUtil.GetMonitors()
            .Select((monitor, index) => new MonitorOption
            {
                Index = index,
                DeviceId = monitor.deviceId,
                IsPrimary = monitor.isPrimary,
                DisplayName = monitor.isPrimary
                    ? $"* {index + 1} ({monitor.deviceName})"
                    : $"{index + 1} ({monitor.deviceName})"
            })
            .ToList();
    }

    public IReadOnlyList<MonitorUtil.MonitorInfo> GetMonitors()
    {
        return MonitorUtil.GetMonitors();
    }
}
