using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using FluentFlyout.Windows;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.ViewModels;
using FluentFlyoutWPF.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace FluentFlyoutWPF.Classes.Services;

public class WindowManager : IWindowManager
{
    private readonly IServiceProvider _services;
    private SettingsWindow? _settingsWindow;
    private EqualizerWindow? _equalizerWindow;
    private TaskbarWindow? _taskbarWindow;
    private LockWindow? _lockWindow;
    private readonly Dictionary<string, EditTrackWindow> _editTrackWindows = [];

    public WindowManager(IServiceProvider services)
    {
        _services = services;
    }

    public void ShowSettings(string? navigationPage = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _settingsWindow ??= CreateSettingsWindow();
            ShowAndActivate(_settingsWindow);

            if (!string.IsNullOrWhiteSpace(navigationPage))
            {
                var pageType = System.Reflection.Assembly
                    .GetExecutingAssembly()
                    .GetType($"FluentFlyoutWPF.Pages.{navigationPage}");
                if (pageType != null)
                {
                    _settingsWindow.NavigateToPage(pageType);
                }
            }
        });
    }

    public void NavigateSettings(Type pageType)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _settingsWindow ??= CreateSettingsWindow();
            ShowAndActivate(_settingsWindow);
            _settingsWindow.NavigateToPage(pageType);
        });
    }

    public void ShowEditTrack(TrackModel track, bool lyricsOnly = false)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_editTrackWindows.TryGetValue(track.FilePath, out var existingWindow))
            {
                if (existingWindow.WindowState == WindowState.Minimized)
                {
                    existingWindow.WindowState = WindowState.Normal;
                }

                if (lyricsOnly)
                {
                    existingWindow.SelectLyricsTab();
                }

                ShowAndActivate(existingWindow);
                return;
            }

            var viewModel = new EditTrackViewModel(track);
            var window = new EditTrackWindow(viewModel)
            {
                Owner = GetOwnerWindow()
            };

            if (lyricsOnly)
            {
                window.Loaded += (_, _) => window.SelectLyricsTab();
            }

            _editTrackWindows[track.FilePath] = window;
            window.Closed += (_, _) => _editTrackWindows.Remove(track.FilePath);
            window.Show();
        });
    }

    public void ShowEqualizer()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _equalizerWindow ??= CreateSingletonWindow<EqualizerWindow>(() => _equalizerWindow = null);
            ShowAndActivate(_equalizerWindow);
        });
    }

    public void ShowManageLibrary()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = _services.GetRequiredService<ManageLibraryWindow>();
            window.Owner = GetOwnerWindow();
            window.ShowDialog();
        });
    }

    public TaskbarWindow ShowTaskbarWindow()
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            _taskbarWindow ??= CreateSingletonWindow<TaskbarWindow>(() => _taskbarWindow = null, showImmediately: true, assignOwner: false);
            if (!_taskbarWindow.IsVisible)
            {
                _taskbarWindow.Show();
            }

            return _taskbarWindow;
        });
    }

    public TaskbarWindow RecreateTaskbarWindow()
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            if (_taskbarWindow != null)
            {
                try
                {
                    _taskbarWindow.Close();
                }
                catch
                {
                    // Ignored while recreating
                }
            }

            _taskbarWindow = CreateSingletonWindow<TaskbarWindow>(() => _taskbarWindow = null, showImmediately: true, assignOwner: false);
            return _taskbarWindow;
        });
    }

    public NextUpWindow ShowNextUp(string title, string artist, BitmapImage? thumbnail, NextUpDisplayMode displayMode = NextUpDisplayMode.UpNext, bool autoClose = true)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var settingsViewModel = _services.GetRequiredService<SettingsShellViewModel>();
            return new NextUpWindow(settingsViewModel, title, artist, thumbnail, displayMode, autoClose);
        });
    }

    public LockWindow GetOrCreateLockWindow()
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            _lockWindow ??= CreateSingletonWindow<LockWindow>(() => _lockWindow = null, assignOwner: false);
            return _lockWindow;
        });
    }

    private SettingsWindow CreateSettingsWindow()
    {
        var window = _services.GetRequiredService<SettingsWindow>();
        window.Closed += (_, _) => _settingsWindow = null;
        return window;
    }

    private TWindow CreateSingletonWindow<TWindow>(Action onClosed, bool showImmediately = false, bool assignOwner = true)
        where TWindow : Window
    {
        var window = _services.GetRequiredService<TWindow>();
        window.Closed += (_, _) => onClosed();

        if (assignOwner && window.Owner == null && window != Application.Current.MainWindow)
        {
            window.Owner = GetOwnerWindow();
        }

        if (showImmediately)
        {
            window.Show();
        }

        return window;
    }

    private static Window GetOwnerWindow()
    {
        return Application.Current.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current.MainWindow
            ?? Application.Current.Windows.OfType<Window>().FirstOrDefault()
            ?? throw new InvalidOperationException("No active window is available to own the requested window.");
    }

    private static void ShowAndActivate(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Focus();
    }
}
