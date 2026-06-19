using System.Runtime.CompilerServices;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.ViewModels;
using Windows.Media.Control;
using WindowsMediaController;
using Xunit;
using static WindowsMediaController.MediaManager;

namespace FluentFlyout.Tests;

public class PlaybackSourceResolverServiceTests
{
    [Fact]
    public void Resolve_InternalPlayingWins()
    {
        var playback = new ResolverPlaybackService
        {
            IsPlaying = true,
            CurrentTrack = new TrackModel { Title = "Internal" }
        };
        var external = new ResolverExternalMediaService
        {
            PreferredSession = CreateSession(),
            IsPreferredSessionPlaying = true
        };
        var resolver = CreateResolver(playback, external);

        var resolved = resolver.Resolve();

        Assert.Equal(PlaybackSourceKind.Internal, resolved.Kind);
        Assert.Null(resolved.ExternalSession);
    }

    [Fact]
    public void Resolve_ExternalPlayingWinsWhenInternalNotPlaying()
    {
        var playback = new ResolverPlaybackService
        {
            IsPlaying = false,
            CurrentTrack = new TrackModel { Title = "Internal" }
        };
        var session = CreateSession();
        var external = new ResolverExternalMediaService
        {
            PreferredSession = session,
            IsPreferredSessionPlaying = true
        };
        var resolver = CreateResolver(playback, external);

        var resolved = resolver.Resolve();

        Assert.Equal(PlaybackSourceKind.External, resolved.Kind);
        Assert.Same(session, resolved.ExternalSession);
    }

    [Fact]
    public void Resolve_InternalTrackWinsWhenExternalDoesNotExist()
    {
        var playback = new ResolverPlaybackService
        {
            IsPlaying = false,
            CurrentTrack = new TrackModel { Title = "Internal" }
        };
        var resolver = CreateResolver(playback, new ResolverExternalMediaService());

        var resolved = resolver.Resolve();

        Assert.Equal(PlaybackSourceKind.Internal, resolved.Kind);
    }

    [Fact]
    public void Resolve_ExternalPausedExistsWhenInternalIsEmpty()
    {
        var session = CreateSession();
        var external = new ResolverExternalMediaService
        {
            PreferredSession = session,
            IsPreferredSessionPlaying = false
        };
        var resolver = CreateResolver(new ResolverPlaybackService(), external);

        var resolved = resolver.Resolve();

        Assert.Equal(PlaybackSourceKind.External, resolved.Kind);
        Assert.Same(session, resolved.ExternalSession);
    }

    [Fact]
    public void Resolve_NoneWhenNoSourcesExist()
    {
        var resolver = CreateResolver(new ResolverPlaybackService(), new ResolverExternalMediaService());

        var resolved = resolver.Resolve();

        Assert.Equal(PlaybackSourceKind.None, resolved.Kind);
    }

    private static PlaybackSourceResolverService CreateResolver(
        ResolverPlaybackService playbackService,
        ResolverExternalMediaService externalMediaService)
    {
        return new PlaybackSourceResolverService(
            new ResolverSettingsService(),
            playbackService,
            externalMediaService);
    }

    private static MediaSession CreateSession()
        => (MediaSession)RuntimeHelpers.GetUninitializedObject(typeof(MediaSession));

    private sealed class ResolverSettingsService : ISettingsService
    {
        public UserSettings Current { get; } = new() { InternalPlayerEnabled = true };
        public void Save() { }
    }

    private sealed class ResolverExternalMediaService : IExternalMediaService
    {
        public MediaManager? MediaManager => null;
        public MediaSession? PreferredSession { get; set; }
        public bool IsPreferredSessionPlaying { get; set; }

        public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties>? MediaPropertyChanged { add { } remove { } }
        public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo?>? PlaybackStateChanged { add { } remove { } }
        public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties>? TimelinePropertyChanged { add { } remove { } }
        public event Action<MediaSession>? SessionClosed { add { } remove { } }

        public void Initialize() { }
        public void UpdateStateFromSettings() { }
        public void Start() { }
        public void Stop() { }
        public MediaSession? GetPreferredSession() => PreferredSession;
        public bool IsInternalSession(MediaSession session) => false;
        public bool IsSessionPlaying(MediaSession? session) => session != null && IsPreferredSessionPlaying;
        public Task PauseOtherSessions(MediaSession currentMediaSession) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class ResolverPlaybackService : IPlaybackService
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public event EventHandler? QueueChanged { add { } remove { } }
        public event EventHandler? TrackChanged { add { } remove { } }
        public event EventHandler<UpcomingTrackEventArgs>? UpcomingTrackDue { add { } remove { } }
        public event EventHandler<string>? PlaybackError { add { } remove { } }

        public IReadOnlyList<TrackModel> CurrentQueue => [];
        public TrackModel? CurrentTrack { get; set; }
        public int CurrentQueueIndex => -1;
        public bool CanUndo => false;
        public QueueUndoActionKind UndoActionKind => QueueUndoActionKind.None;
        public bool IsShuffleEnabled { get; set; }
        public bool IsPlaying { get; set; }
        public TimeSpan CurrentPosition { get; set; }
        public TimeSpan TotalDuration => TimeSpan.Zero;
        public float Volume { get; set; }
        public RepeatMode RepeatMode { get; set; }
        public bool CanGoNext => false;
        public bool CanGoPrevious => false;
        public LyricLine? CurrentLyricLine => null;
        public List<LyricLine> CurrentLyrics => [];

        public void AddToQueue(TrackModel track) { }
        public void ClearQueuePreservingCurrent() { }
        public Task ImportQueueAsync(string filePath) => Task.CompletedTask;
        public void ExportQueue(string filePath) { }
        public void MoveTrack(int oldIndex, int newIndex) { }
        public void MoveTracks(IReadOnlyList<int> oldIndices, int newIndex) { }
        public void Play(TrackModel track) { }
        public void PlayAtIndex(int index) { }
        public void PlayQueue(IReadOnlyList<TrackModel> tracks, int startIndex) { }
        public void PlaySingle(TrackModel track, IReadOnlyList<TrackModel> visibleTracks) { }
        public void RemoveFromQueue(TrackModel track) { }
        public void Undo() { }
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public bool PlayNext() => false;
        public bool PlayPrevious() => false;
        public void TogglePlayPause() { }
        public void Seek(TimeSpan position) { }
    }
}
