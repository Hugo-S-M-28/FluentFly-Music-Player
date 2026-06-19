using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.ViewModels;

namespace FluentFlyoutWPF.Classes.Services;

public sealed class SettingsService : ISettingsService
{
    public UserSettings Current => SettingsManager.Current;

    public void Save()
    {
        SettingsManager.SaveSettings();
    }
}
