namespace FluentFlyoutWPF.Classes.Services;

public static class ServiceLocator
{
    public static IDialogService Dialog { get; set; } = null!;
    public static IFileDialogService FileDialog { get; set; } = null!;
    public static IWindowService Windows { get; set; } = null!;
    public static ISystemShellService Shell { get; set; } = null!;
    public static IAppShellService AppShell { get; set; } = null!;
}
