using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Classes.Services;

public class WindowService : IWindowService
{
    private readonly IWindowManager _windowManager;

    public WindowService(IWindowManager windowManager)
    {
        _windowManager = windowManager;
    }

    public void ShowSettings(string? navigationPage = null)
    {
        _windowManager.ShowSettings(navigationPage);
    }

    public void ShowEditTrack(TrackModel track, bool lyricsOnly = false)
    {
        _windowManager.ShowEditTrack(track, lyricsOnly);
    }

    public void ShowEqualizer()
    {
        _windowManager.ShowEqualizer();
    }

    public void ShowManageLibrary()
    {
        _windowManager.ShowManageLibrary();
    }
}
