using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Services;

namespace FluentFlyoutWPF.ViewModels;

public partial class TaskbarVisualizerViewModel : ObservableObject
{
    private readonly IWindowManager? _windowManager;
    private readonly ISettingsService _settingsService;

    public UserSettings Settings => _settingsService.Current;

    public bool IsClickable => Settings.TaskbarVisualizerClickable;
    public bool HasContent => Settings.TaskbarVisualizerHasContent;

    [ObservableProperty]
    private ImageSource? backdropImage;

    public TaskbarVisualizerViewModel(IWindowManager windowManager, ISettingsService settingsService)
    {
        _windowManager = windowManager;
        _settingsService = settingsService;
    }

    public TaskbarVisualizerViewModel()
    {
        _settingsService = new DesignSettingsService();
    }

    public void UpdateBackground(ImageSource? image)
    {
        BackdropImage = image;
    }

    public void RefreshFromSettings()
    {
        OnPropertyChanged(nameof(IsClickable));
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(Settings));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (!IsClickable || !HasContent)
        {
            return;
        }

        _windowManager?.ShowSettings("TaskbarVisualizerPage");
    }

    private sealed class DesignSettingsService : ISettingsService
    {
        public UserSettings Current { get; } = new();
        public void Save()
        {
        }
    }
}
