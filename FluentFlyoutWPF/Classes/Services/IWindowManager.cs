using System;
using System.Windows.Media.Imaging;
using FluentFlyout.Windows;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Windows;

namespace FluentFlyoutWPF.Classes.Services;

public interface IWindowManager
{
    void ShowSettings(string? navigationPage = null);
    void NavigateSettings(Type pageType);
    void ShowEditTrack(TrackModel track, bool lyricsOnly = false);
    void ShowEqualizer();
    void ShowManageLibrary();
    TaskbarWindow ShowTaskbarWindow();
    TaskbarWindow RecreateTaskbarWindow();
    NextUpWindow ShowNextUp(string title, string artist, BitmapImage thumbnail);
    LockWindow GetOrCreateLockWindow();
}
