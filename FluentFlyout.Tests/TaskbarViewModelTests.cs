using FluentFlyout.Classes.Settings;
using FluentFlyout.Windows;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.ViewModels;
using FluentFlyoutWPF.Windows;
using System.Windows;
using System.Windows.Media;
using Windows.Media.Control;
using Xunit;

namespace FluentFlyout.Tests;

[CollectionDefinition("TaskbarViewModels", DisableParallelization = true)]
public sealed class TaskbarViewModelsCollectionDefinition
{
}

[Collection("TaskbarViewModels")]
public class TaskbarWidgetViewModelTests
{
    [Fact]
    public void ClearPlayback_ResetsBindableState()
    {
        var viewModel = CreateWidgetViewModel();

        viewModel.UpdatePlayback("Song", "Artist", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);
        viewModel.UpdateProgress(50, 100);
        viewModel.ClearPlayback();

        Assert.False(viewModel.HasMedia);
        Assert.Equal(string.Empty, viewModel.Title);
        Assert.Equal(string.Empty, viewModel.Artist);
        Assert.Equal(Visibility.Collapsed, viewModel.SongInfoVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.ProgressVisibility);
        Assert.False(viewModel.CanPrevious);
        Assert.False(viewModel.CanPlayPause);
        Assert.False(viewModel.CanNext);
        Assert.Equal(0, viewModel.ProgressRatio);
    }

    [Fact]
    public void UpdatePlayback_ControlsDisabled_DisablesTransportAndSetsMetadata()
    {
        var settingsService = new FakeSettingsService
        {
            Current = { TaskbarWidgetControlsEnabled = false }
        };
        var viewModel = CreateWidgetViewModel(settingsService: settingsService);

        bool changed = viewModel.UpdatePlayback(
            "Song",
            "Artist",
            null,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);

        Assert.True(changed);
        Assert.True(viewModel.HasMedia);
        Assert.True(viewModel.IsPaused);
        Assert.Equal("Song", viewModel.Title);
        Assert.Equal("Artist", viewModel.Artist);
        Assert.Equal("Song\n\nArtist", viewModel.TooltipText);
        Assert.Equal(Visibility.Visible, viewModel.SongInfoVisibility);
        Assert.Equal(Visibility.Visible, viewModel.ArtistVisibility);
        Assert.False(viewModel.CanPlayPause);
        Assert.Equal(0.5, viewModel.PlayPauseOpacity);
    }

    [Theory]
    [InlineData(50, 100, 0.5, Visibility.Visible)]
    [InlineData(150, 100, 1.0, Visibility.Visible)]
    [InlineData(-10, 100, 0.0, Visibility.Visible)]
    [InlineData(10, 0, 0.0, Visibility.Collapsed)]
    public void UpdateProgress_ClampsRatioAndVisibility(double current, double total, double expectedRatio, Visibility expectedVisibility)
    {
        var viewModel = CreateWidgetViewModel();

        viewModel.UpdateProgress(current, total);

        Assert.Equal(expectedRatio, viewModel.ProgressRatio, precision: 3);
        Assert.Equal(expectedVisibility, viewModel.ProgressVisibility);
    }

    private static TaskbarWidgetViewModel CreateWidgetViewModel(
        FakePlaybackService? playbackService = null,
        FakePlaybackSourceResolver? playbackSourceResolver = null,
        FakeSettingsService? settingsService = null)
    {
        playbackService ??= new FakePlaybackService();
        playbackSourceResolver ??= new FakePlaybackSourceResolver();
        settingsService ??= new FakeSettingsService();

        return new TaskbarWidgetViewModel(playbackService, playbackSourceResolver, settingsService);
    }
}

[Collection("TaskbarViewModels")]
public class TaskbarVisualizerViewModelTests
{
    [Fact]
    public void UpdateBackground_StoresBindableImage()
    {
        var viewModel = new TaskbarVisualizerViewModel(new FakeWindowManager(), new FakeSettingsService());
        var image = new DrawingImage();

        viewModel.UpdateBackground(image);

        Assert.Same(image, viewModel.BackdropImage);
    }

    [Fact]
    public void OpenSettingsCommand_NavigatesToVisualizerPageWhenClickable()
    {
        var settingsService = new FakeSettingsService
        {
            Current =
            {
                TaskbarVisualizerClickable = true,
                TaskbarVisualizerHasContent = true
            }
        };
        var windowManager = new FakeWindowManager();
        var viewModel = new TaskbarVisualizerViewModel(windowManager, settingsService);

        viewModel.OpenSettingsCommand.Execute(null);

        Assert.Equal("TaskbarVisualizerPage", windowManager.LastNavigationPage);
    }

    private sealed class FakeWindowManager : IWindowManager
    {
        public string? LastNavigationPage { get; private set; }

        public void ShowSettings(string? navigationPage = null) => LastNavigationPage = navigationPage;
        public void NavigateSettings(Type pageType) { }
        public void ShowEditTrack(TrackModel track, bool lyricsOnly = false) { }
        public void ShowEqualizer() { }
        public void ShowManageLibrary() { }
        public TaskbarWindow ShowTaskbarWindow() => throw new NotSupportedException();
        public TaskbarWindow RecreateTaskbarWindow() => throw new NotSupportedException();
        public NextUpWindow ShowNextUp(string title, string artist, System.Windows.Media.Imaging.BitmapImage? thumbnail, NextUpDisplayMode displayMode = NextUpDisplayMode.UpNext, bool autoClose = true) => throw new NotSupportedException();
        public LockWindow GetOrCreateLockWindow() => throw new NotSupportedException();
    }
}

internal sealed class FakeSettingsService : ISettingsService
{
    public UserSettings Current { get; } = new();
    public void Save() { }
}

internal sealed class FakePlaybackSourceResolver : IPlaybackSourceResolver
{
    public ResolvedPlaybackSource Source { get; set; } = new(PlaybackSourceKind.None);
    public ResolvedPlaybackSource Resolve() => Source;
}

internal sealed class FakePlaybackService : IPlaybackService
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    public event EventHandler? QueueChanged { add { } remove { } }
    public event EventHandler? TrackChanged { add { } remove { } }
    public event EventHandler<UpcomingTrackEventArgs>? UpcomingTrackDue { add { } remove { } }
    public event EventHandler<string>? PlaybackError { add { } remove { } }

    public IReadOnlyList<TrackModel> CurrentQueue => [];
    public TrackModel? CurrentTrack { get; set; }
    public int CurrentQueueIndex => 0;
    public bool CanUndo => false;
    public QueueUndoActionKind UndoActionKind => QueueUndoActionKind.None;
    public bool IsShuffleEnabled { get; set; }
    public bool IsPlaying { get; set; }
    public TimeSpan CurrentPosition { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public float Volume { get; set; }
    public RepeatMode RepeatMode { get; set; }
    public bool CanGoNext { get; set; }
    public bool CanGoPrevious { get; set; }
    public LyricLine? CurrentLyricLine => null;
    public List<LyricLine> CurrentLyrics => [];
    public int PlayNextCalls { get; private set; }
    public int PlayPreviousCalls { get; private set; }
    public int TogglePlayPauseCalls { get; private set; }

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
    public bool PlayNext() { PlayNextCalls++; return true; }
    public bool PlayPrevious() { PlayPreviousCalls++; return true; }
    public void TogglePlayPause() { TogglePlayPauseCalls++; }
    public void Seek(TimeSpan position) { CurrentPosition = position; }
}
