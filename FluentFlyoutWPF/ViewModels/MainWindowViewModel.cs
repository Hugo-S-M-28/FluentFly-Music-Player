// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.Classes.Services;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Threading.Tasks;
using System.Windows.Media;

namespace FluentFlyoutWPF.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public UserSettings Settings => SettingsManager.Current;
    public NowPlayingViewModel NowPlaying { get; }

    public MainWindowViewModel(NowPlayingViewModel nowPlaying)
    {
        NowPlaying = nowPlaying;
    }

    [ObservableProperty]
    private string mediaIdText = string.Empty;

    [ObservableProperty]
    private ImageSource? mediaIdIcon;

    [ObservableProperty]
    private bool isMediaIdVisible;

    [ObservableProperty]
    private bool isPlaceholderVisible = true;

    [ObservableProperty]
    private bool isPrimaryCloseVisible;

    [ObservableProperty]
    private bool isCompactCloseVisible;

    [ObservableProperty]
    private bool isSeekbarVisible;

    [ObservableProperty]
    private bool isSeekingFromShell;

    [RelayCommand]
    private void PlayPause()
    {
        if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.TogglePlayPause();
            return;
        }

        var session = ExternalMediaService.Instance.GetPreferredSession();
        if (session != null && !ExternalMediaService.Instance.IsInternalSession(session))
        {
            FluentFlyout.Classes.NativeMethods.keybd_event(0xB3, 0, 0, IntPtr.Zero); // Media Play/Pause key
        }
    }

    [RelayCommand]
    private async Task SkipNext()
    {
        var session = ExternalMediaService.Instance.GetPreferredSession();
        if (session != null && !ExternalMediaService.Instance.IsInternalSession(session))
        {
            await session.ControlSession.TrySkipNextAsync();
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.PlayNext();
        }
    }

    [RelayCommand]
    private async Task SkipPrevious()
    {
        var session = ExternalMediaService.Instance.GetPreferredSession();
        if (session != null && !ExternalMediaService.Instance.IsInternalSession(session))
        {
            await session.ControlSession.TrySkipPreviousAsync();
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.PlayPrevious();
        }
    }

    [RelayCommand]
    private async Task ToggleRepeat()
    {
        var session = ExternalMediaService.Instance.GetPreferredSession();
        if (session != null)
        {
            var playbackInfo = session.ControlSession.GetPlaybackInfo();
            if (playbackInfo.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.None)
            {
                await session.ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.List);
            }
            else if (playbackInfo.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.List)
            {
                await session.ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.Track);
            }
            else if (playbackInfo.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.Track)
            {
                await session.ControlSession.TryChangeAutoRepeatModeAsync(global::Windows.Media.MediaPlaybackAutoRepeatMode.None);
            }
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            var current = MusicPlayerService.Instance.RepeatMode;
            MusicPlayerService.Instance.RepeatMode = current switch
            {
                RepeatMode.None => RepeatMode.All,
                RepeatMode.All => RepeatMode.One,
                RepeatMode.One => RepeatMode.None,
                _ => RepeatMode.None
            };
        }
    }

    [RelayCommand]
    private async Task ToggleShuffle()
    {
        var session = ExternalMediaService.Instance.GetPreferredSession();
        if (session != null)
        {
            if (session.ControlSession.GetPlaybackInfo().IsShuffleActive == true)
            {
                await session.ControlSession.TryChangeShuffleActiveAsync(false);
            }
            else
            {
                await session.ControlSession.TryChangeShuffleActiveAsync(true);
            }
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.IsShuffleEnabled = !MusicPlayerService.Instance.IsShuffleEnabled;
        }
    }

    [RelayCommand]
    private void CloseFlyout()
    {
        WeakReferenceMessenger.Default.Send(new ShowMediaFlyoutMessage(toggleMode: true));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        ServiceLocator.AppShell.OpenSettings();
    }

    [RelayCommand]
    private void OpenRepository()
    {
        ServiceLocator.AppShell.OpenRepository();
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        ServiceLocator.AppShell.OpenLogsFolder();
    }

    [RelayCommand]
    private void ReportBug()
    {
        ServiceLocator.AppShell.ReportBug();
    }

    [RelayCommand]
    private void QuitApplication()
    {
        WeakReferenceMessenger.Default.Send(new RequestApplicationShutdownMessage());
    }

    public void UpdateCloseButtonVisibility(bool alwaysDisplay, bool compactLayout)
    {
        IsPrimaryCloseVisible = alwaysDisplay && !compactLayout;
        IsCompactCloseVisible = alwaysDisplay && compactLayout;
    }

    public void UpdateSeekbarVisibility(bool isVisible)
    {
        IsSeekbarVisible = isVisible;
    }

    public void SetPlaceholderVisibility(bool isVisible)
    {
        IsPlaceholderVisible = isVisible;
    }

    public void SetMediaIdentity(string text, ImageSource? icon)
    {
        MediaIdText = text;
        MediaIdIcon = icon;
        IsMediaIdVisible = true;
    }

    public void ClearMediaIdentity()
    {
        MediaIdText = string.Empty;
        MediaIdIcon = null;
        IsMediaIdVisible = false;
    }

    public void BeginSeekFromShell()
    {
        IsSeekingFromShell = true;
        NowPlaying.BeginSeekInteraction();
    }

    public void PreviewSeekFromShell(double seconds)
    {
        NowPlaying.UpdateSeekPreview(seconds);
    }

    public async Task CommitSeekFromShellAsync(double seconds)
    {
        var seekPosition = TimeSpan.FromSeconds(seconds);

        if (ExternalMediaService.Instance.GetPreferredSession() is { } session)
        {
            long ticks = seekPosition.Ticks > 0 ? seekPosition.Ticks : 1;
            await session.ControlSession.TryChangePlaybackPositionAsync(ticks);
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.Seek(seekPosition);
        }

        NowPlaying.EndSeekInteraction();
        IsSeekingFromShell = false;
    }

    public void CancelSeekFromShell()
    {
        NowPlaying.EndSeekInteraction();
        IsSeekingFromShell = false;
    }
}
