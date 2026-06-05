namespace FluentFlyoutWPF.Models;

public sealed class MonitorOption
{
    public int Index { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public bool IsPrimary { get; init; }
    public string DeviceId { get; init; } = string.Empty;
}
