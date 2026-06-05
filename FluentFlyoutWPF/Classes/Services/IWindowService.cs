using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Classes.Services;

public interface IWindowService
{
    void ShowSettings(string? navigationPage = null);
    void ShowEditTrack(TrackModel track, bool lyricsOnly = false);
    void ShowEqualizer();
    void ShowManageLibrary();
}
