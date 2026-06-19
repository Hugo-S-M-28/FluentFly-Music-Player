using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Windows;

namespace FluentFlyoutWPF.Classes.Coordinators;

public sealed class LockKeyFlyoutCoordinator : IDisposable
{
    private readonly IWindowManager _windowManager;
    private LockWindow? _window;

    public LockKeyFlyoutCoordinator(IWindowManager windowManager)
    {
        _windowManager = windowManager;
    }

    public async Task HandleHotKeyAsync(int vkCode, bool isKeyDown)
    {
        if (isKeyDown ||
            !SettingsManager.Current.LockKeysEnabled ||
            FullscreenDetector.IsFullscreenApplicationRunning())
        {
            return;
        }

        var request = LockKeyStateResolver.Resolve(
            vkCode,
            SettingsManager.Current.LockKeysInsertEnabled,
            () => Keyboard.IsKeyToggled(Key.CapsLock),
            () => Keyboard.IsKeyToggled(Key.NumLock),
            () => Keyboard.IsKeyToggled(Key.Scroll));

        if (request != null)
        {
            await ShowAsync(request);
        }
    }

    private async Task ShowAsync(LockKeyFlyoutRequest request)
    {
        _window ??= _windowManager.GetOrCreateLockWindow();
        string text = Application.Current.TryFindResource(request.ResourceKey)?.ToString() ?? request.FallbackText;
        await _window.ShowLockFlyoutAsync(text, request.IsOn, request.UsePressedText);
    }

    public void Dispose()
    {
        if (_window?.IsLoaded == true)
        {
            try
            {
                _window.Close();
            }
            catch
            {
                // Ignore shutdown-time window disposal races.
            }
        }
    }
}
