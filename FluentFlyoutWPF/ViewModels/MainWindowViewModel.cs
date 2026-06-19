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
    private readonly IPlaybackService _playbackService;
    private readonly ISettingsService _settingsService;
    private readonly IAppShellService _appShellService;
    private readonly IPlaybackSourceResolver _playbackSourceResolver;

    public UserSettings Settings => _settingsService.Current;
    public NowPlayingViewModel NowPlaying { get; }

    public MainWindowViewModel(
        NowPlayingViewModel nowPlaying,
        IPlaybackService playbackService,
        ISettingsService settingsService,
        IAppShellService appShellService,
        IPlaybackSourceResolver playbackSourceResolver)
    {
        NowPlaying = nowPlaying;
        _playbackService = playbackService;
        _settingsService = settingsService;
        _appShellService = appShellService;
        _playbackSourceResolver = playbackSourceResolver;
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
    private async Task PlayPause()
    {
        var resolved = _playbackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            _playbackService.TogglePlayPause();
            return;
        }

        if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var session = resolved.ExternalSession;
            var status = session.ControlSession.GetPlaybackInfo().PlaybackStatus;
            if (status == global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                await session.ControlSession.TryPauseAsync();
            }
            else
            {
                await session.ControlSession.TryPlayAsync();
            }
        }
    }

    [RelayCommand]
    private async Task SkipNext()
    {
        var resolved = _playbackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            _playbackService.PlayNext();
            return;
        }

        if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var session = resolved.ExternalSession;
            await session.ControlSession.TrySkipNextAsync();
        }
    }

    [RelayCommand]
    private async Task SkipPrevious()
    {
        var resolved = _playbackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            _playbackService.PlayPrevious();
            return;
        }

        if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var session = resolved.ExternalSession;
            await session.ControlSession.TrySkipPreviousAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleRepeat()
    {
        var resolved = _playbackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            var current = _playbackService.RepeatMode;
            _playbackService.RepeatMode = current switch
            {
                RepeatMode.None => RepeatMode.All,
                RepeatMode.All => RepeatMode.One,
                RepeatMode.One => RepeatMode.None,
                _ => RepeatMode.None
            };
        }
        else if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var session = resolved.ExternalSession;
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
    }

    [RelayCommand]
    private async Task ToggleShuffle()
    {
        var resolved = _playbackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var session = resolved.ExternalSession;
            bool current = session.ControlSession.GetPlaybackInfo().IsShuffleActive ?? false;
            await session.ControlSession.TryChangeShuffleActiveAsync(!current);
        }
        else if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            _playbackService.IsShuffleEnabled = !_playbackService.IsShuffleEnabled;
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
        _appShellService.OpenSettings();
    }

    [RelayCommand]
    private void OpenRepository()
    {
        _appShellService.OpenRepository();
    }

    [RelayCommand]
    private void OpenLogsFolder()
    {
        _appShellService.OpenLogsFolder();
    }

    [RelayCommand]
    private void ReportBug()
    {
        _appShellService.ReportBug();
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

    public void Seek(double seekPositionSeconds)
    {
        var resolved = _playbackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            var seekPosition = TimeSpan.FromSeconds(seekPositionSeconds);
            _playbackService.Seek(seekPosition);
        }
        else if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var seekPosition = TimeSpan.FromSeconds(seekPositionSeconds);
            long ticks = seekPosition.Ticks > 0 ? seekPosition.Ticks : 1;
            _ = resolved.ExternalSession.ControlSession.TryChangePlaybackPositionAsync(ticks);
        }
    }

    public async Task CommitSeekFromShellAsync(double seconds)
    {
        var seekPosition = TimeSpan.FromSeconds(seconds);
        var resolved = _playbackSourceResolver.Resolve();

        if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var session = resolved.ExternalSession;
            long ticks = seekPosition.Ticks > 0 ? seekPosition.Ticks : 1;
            await session.ControlSession.TryChangePlaybackPositionAsync(ticks);
        }
        else if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            _playbackService.Seek(seekPosition);
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
