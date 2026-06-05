using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;
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
    private LockWindow? _lockWindow;

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

    public async Task HandleLockKeysHotKeyAsync(int vkCode)
    {
        if (!SettingsManager.Current.LockKeysEnabled || FullscreenDetector.IsFullscreenApplicationRunning())
        {
            return;
        }

        if (vkCode == 0x14) // Caps Lock
        {
            _lockWindow ??= _windowManager.GetOrCreateLockWindow();
            var text = Application.Current.TryFindResource("LockWindow_CapsLock")?.ToString() ?? "Caps Lock";
            await _lockWindow.ShowLockFlyoutAsync(text, Keyboard.IsKeyToggled(Key.CapsLock));
        }
        else if (vkCode == 0x90) // Num Lock
        {
            _lockWindow ??= _windowManager.GetOrCreateLockWindow();
            var text = Application.Current.TryFindResource("LockWindow_NumLock")?.ToString() ?? "Num Lock";
            await _lockWindow.ShowLockFlyoutAsync(text, Keyboard.IsKeyToggled(Key.NumLock));
        }
        else if (vkCode == 0x91) // Scroll Lock
        {
            _lockWindow ??= _windowManager.GetOrCreateLockWindow();
            var text = Application.Current.TryFindResource("LockWindow_ScrollLock")?.ToString() ?? "Scroll Lock";
            await _lockWindow.ShowLockFlyoutAsync(text, Keyboard.IsKeyToggled(Key.Scroll));
        }
        else if (vkCode == 0x2D && SettingsManager.Current.LockKeysInsertEnabled) // Insert
        {
            _lockWindow ??= _windowManager.GetOrCreateLockWindow();
            var text = Application.Current.TryFindResource("LockWindow_Insert")?.ToString() ?? "Insert";
            await _lockWindow.ShowLockFlyoutAsync(text, Keyboard.IsKeyToggled(Key.Insert));
        }
    }

    public void ToggleBlur(Window window)
    {
        if (SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled)
        {
            WindowBlurHelper.EnableBlur(window);
        }
        else
        {
            WindowBlurHelper.DisableBlur(window);
        }
    }

    public void Dispose()
    {
        if (_nextUpWindow?.IsLoaded == true)
        {
            try { _nextUpWindow.Close(); } catch { }
        }
        if (_lockWindow?.IsLoaded == true)
        {
            try { _lockWindow.Close(); } catch { }
        }
    }
}
