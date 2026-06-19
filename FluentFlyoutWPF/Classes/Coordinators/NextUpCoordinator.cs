using System;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Windows;
using Windows.Media.Control;

namespace FluentFlyoutWPF.Classes.Coordinators;

public sealed class NextUpCoordinator : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly IWindowManager _windowManager;
    private readonly ILibraryService _libraryService;
    private readonly ISettingsService _settingsService;
    private readonly Dispatcher _dispatcher;
    private readonly Func<bool> _isMediaFlyoutVisible;

    private NextUpWindow? _window;
    private string _currentKey = string.Empty;
    private string _pendingKey = string.Empty;
    private string _pendingTitle = string.Empty;
    private string _pendingArtist = string.Empty;
    private BitmapImage? _pendingThumbnail;

    public NextUpCoordinator(
        IWindowManager windowManager,
        ILibraryService libraryService,
        ISettingsService settingsService,
        Dispatcher dispatcher,
        Func<bool> isMediaFlyoutVisible)
    {
        _windowManager = windowManager;
        _libraryService = libraryService;
        _settingsService = settingsService;
        _dispatcher = dispatcher;
        _isMediaFlyoutVisible = isMediaFlyoutVisible;
    }

    public void CloseIfOpen(bool ignoreSafeguard = false)
    {
        if (_window == null)
        {
            return;
        }

        try
        {
            _ = _window.CloseWithAnimationAsync(ignoreSafeguard);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Ignored error while closing NextUpWindow");
        }
    }

    public void ShowInternalPreview(TrackModel upcomingTrack)
    {
        if (!CanShow())
        {
            return;
        }

        var thumbnail = upcomingTrack.AlbumArt;
        if (thumbnail == null && !string.IsNullOrEmpty(upcomingTrack.AlbumArtPath))
        {
            thumbnail = _libraryService.GetAlbumArt(upcomingTrack, 400);
            upcomingTrack.AlbumArt = thumbnail;
        }

        string key = BuildKey("Internal", "Internal", upcomingTrack.Title, upcomingTrack.Artist, upcomingTrack.FilePath);

        _dispatcher.Invoke(() =>
        {
            ShowOrUpdate(key, upcomingTrack.Title, upcomingTrack.Artist, thumbnail, NextUpDisplayMode.UpNext, autoClose: true);
        });
    }

    public void ShowExternalNowPlaying(
        string sessionId,
        string title,
        string artist,
        BitmapImage? thumbnail,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus playbackStatus,
        bool allowPending)
    {
        if (!CanShow())
        {
            ClearPending();
            return;
        }

        string key = BuildKey("External", sessionId, title, artist);

        if (playbackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            if (allowPending)
            {
                _pendingKey = key;
                _pendingTitle = title;
                _pendingArtist = artist;
                _pendingThumbnail = thumbnail;
                Logger.Debug($"NextUp: saved pending external now-playing request for {_pendingKey}.");
            }

            return;
        }

        _dispatcher.Invoke(() =>
        {
            ShowOrUpdate(key, title, artist, thumbnail, NextUpDisplayMode.NowPlaying, autoClose: true);
            ClearPending();
        });
    }

    public void RetryPendingExternal(
        string sessionId,
        string title,
        string artist,
        BitmapImage? thumbnail,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus playbackStatus)
    {
        string key = BuildKey("External", sessionId, title, artist);
        if (string.IsNullOrEmpty(_pendingKey) || _pendingKey != key)
        {
            return;
        }

        ShowExternalNowPlaying(sessionId, title, artist, thumbnail ?? _pendingThumbnail, playbackStatus, allowPending: false);
    }

    public void ClearPendingForSession(string sessionId)
    {
        if (!string.IsNullOrEmpty(_pendingKey) &&
            _pendingKey.StartsWith($"External|{sessionId}|", StringComparison.Ordinal))
        {
            ClearPending();
        }
    }

    private bool CanShow()
    {
        return _settingsService.Current.NextUpEnabled &&
               !FullscreenDetector.IsFullscreenApplicationRunning() &&
               !_isMediaFlyoutVisible();
    }

    private void ShowOrUpdate(
        string key,
        string title,
        string artist,
        BitmapImage? thumbnail,
        NextUpDisplayMode displayMode,
        bool autoClose)
    {
        bool isDifferentTrack = _currentKey != key;

        if (_window == null && isDifferentTrack)
        {
            _window = _windowManager.ShowNextUp(title, artist, thumbnail, displayMode, autoClose);
            _currentKey = key;
            _window.Closed += (_, _) => _window = null;
        }
        else if (_window != null && isDifferentTrack)
        {
            WindowHelper.SetVisibility(_window, false);
            _window.Close();
            _window = _windowManager.ShowNextUp(title, artist, thumbnail, displayMode, autoClose);
            _currentKey = key;
            _window.Closed += (_, _) => _window = null;
        }
        else if (_window != null)
        {
            _window.UpdateThumbnail(thumbnail);
        }
    }

    private void ClearPending()
    {
        _pendingKey = string.Empty;
        _pendingTitle = string.Empty;
        _pendingArtist = string.Empty;
        _pendingThumbnail = null;
    }

    private static string BuildKey(string source, string sessionId, string title, string artist, string uniqueId = "")
        => string.Join("|", source, sessionId, title, artist, uniqueId);

    public void Dispose()
    {
        CloseIfOpen(ignoreSafeguard: true);
        ClearPending();
    }
}
