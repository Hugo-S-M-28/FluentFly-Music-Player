using System;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Models;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF.Classes.Coordinators;

public sealed class PlaybackSessionCoordinator : IDisposable
{
    private readonly IPlaybackService _playbackService;
    private readonly IExternalMediaService _externalMediaService;
    private readonly Action _internalTrackChanged;
    private readonly Action<TrackModel> _internalUpcomingTrackDue;
    private readonly Action _internalPlaybackStateChanged;
    private readonly Action<MediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo?> _externalPlaybackStateChanged;
    private readonly Action<MediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties> _externalMediaPropertyChanged;
    private readonly Action<MediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties> _externalTimelinePropertyChanged;
    private readonly Action<MediaSession> _externalSessionClosed;

    public PlaybackSessionCoordinator(
        IPlaybackService playbackService,
        IExternalMediaService externalMediaService,
        Action internalTrackChanged,
        Action<TrackModel> internalUpcomingTrackDue,
        Action internalPlaybackStateChanged,
        Action<MediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo?> externalPlaybackStateChanged,
        Action<MediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties> externalMediaPropertyChanged,
        Action<MediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties> externalTimelinePropertyChanged,
        Action<MediaSession> externalSessionClosed)
    {
        _playbackService = playbackService;
        _externalMediaService = externalMediaService;
        _internalTrackChanged = internalTrackChanged;
        _internalUpcomingTrackDue = internalUpcomingTrackDue;
        _internalPlaybackStateChanged = internalPlaybackStateChanged;
        _externalPlaybackStateChanged = externalPlaybackStateChanged;
        _externalMediaPropertyChanged = externalMediaPropertyChanged;
        _externalTimelinePropertyChanged = externalTimelinePropertyChanged;
        _externalSessionClosed = externalSessionClosed;
    }

    public void Initialize()
    {
        _externalMediaService.MediaPropertyChanged += OnExternalMediaPropertyChanged;
        _externalMediaService.PlaybackStateChanged += OnExternalPlaybackStateChanged;
        _externalMediaService.TimelinePropertyChanged += OnExternalTimelinePropertyChanged;
        _externalMediaService.SessionClosed += OnExternalSessionClosed;
        _externalMediaService.Initialize();

        _playbackService.TrackChanged += OnInternalTrackChanged;
        _playbackService.UpcomingTrackDue += OnInternalUpcomingTrackDue;
        _playbackService.PropertyChanged += OnInternalPropertyChanged;
    }

    private void OnExternalPlaybackStateChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo? playbackInfo = null)
        => _externalPlaybackStateChanged(mediaSession, playbackInfo);

    private void OnExternalMediaPropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProperties)
        => _externalMediaPropertyChanged(mediaSession, mediaProperties);

    private void OnExternalTimelinePropertyChanged(MediaSession mediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties timelineProperties)
        => _externalTimelinePropertyChanged(mediaSession, timelineProperties);

    private void OnExternalSessionClosed(MediaSession mediaSession)
        => _externalSessionClosed(mediaSession);

    private void OnInternalTrackChanged(object? sender, EventArgs e)
        => _internalTrackChanged();

    private void OnInternalUpcomingTrackDue(object? sender, UpcomingTrackEventArgs e)
    {
        if (e.UpcomingTrack != null)
        {
            _internalUpcomingTrackDue(e.UpcomingTrack);
        }
    }

    private void OnInternalPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPlaybackService.IsPlaying) ||
            e.PropertyName == nameof(IPlaybackService.CurrentTrack))
        {
            _internalPlaybackStateChanged();
        }
    }

    public void Dispose()
    {
        _externalMediaService.MediaPropertyChanged -= OnExternalMediaPropertyChanged;
        _externalMediaService.PlaybackStateChanged -= OnExternalPlaybackStateChanged;
        _externalMediaService.TimelinePropertyChanged -= OnExternalTimelinePropertyChanged;
        _externalMediaService.SessionClosed -= OnExternalSessionClosed;

        _playbackService.TrackChanged -= OnInternalTrackChanged;
        _playbackService.UpcomingTrackDue -= OnInternalUpcomingTrackDue;
        _playbackService.PropertyChanged -= OnInternalPropertyChanged;
    }
}
