using FluentFlyoutWPF.ViewModels;

namespace FluentFlyoutWPF.Classes.Services;

public interface ISettingsService
{
    UserSettings Current { get; }
    void Save();
}
