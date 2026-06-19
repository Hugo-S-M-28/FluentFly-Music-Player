using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Windows;

namespace FluentFlyoutWPF.Classes.Coordinators;

public class OverlayCoordinator : IDisposable
{
    private readonly IWindowManager _windowManager = App.GetRequiredService<IWindowManager>();
    private NextUpWindow? _nextUpWindow;
    private string _currentTitle = string.Empty;

    public void CloseNextUpWindow()
    {
        if (_nextUpWindow != null)
        {
            try
            {
                _nextUpWindow.Close();
            }
            catch (Exception)
            {
                // Ignored
            }
            _nextUpWindow = null;
        }
    }

    public void ShowNextUpWindow(string title, string artist, BitmapImage thumbnail)
    {
        if (!SettingsManager.Current.NextUpEnabled || FullscreenDetector.IsFullscreenApplicationRunning())
        {
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_nextUpWindow == null && _currentTitle != title)
            {
                _nextUpWindow = _windowManager.ShowNextUp(title, artist, thumbnail);
                _currentTitle = title;
                _nextUpWindow.Closed += (s, e) => _nextUpWindow = null;
            }
            else if (_nextUpWindow != null && _currentTitle != title)
            {
                WindowHelper.SetVisibility(_nextUpWindow, false);
                _nextUpWindow.Close();
                _nextUpWindow = _windowManager.ShowNextUp(title, artist, thumbnail);
                _currentTitle = title;
                _nextUpWindow.Closed += (s, e) => _nextUpWindow = null;
            }
            else if (_nextUpWindow != null && thumbnail != null)
            {
                _nextUpWindow.UpdateThumbnail(thumbnail);
            }
        });
    }

    public void ToggleBlur(Window window)
    {
        WindowBlurHelper.ApplyWindowBackdrop(window);
    }

    public void Dispose()
    {
        if (_nextUpWindow?.IsLoaded == true)
        {
            try { _nextUpWindow.Close(); } catch { }
        }
    }
}
