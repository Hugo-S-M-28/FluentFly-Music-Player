using FluentFlyoutWPF.ViewModels;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Xunit;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Models;

namespace FluentFlyout.Tests;

public class MainWindowViewModelBindingStateTests
{
    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    public void UpdateCloseButtonVisibility_MapsSettingsToPrimaryAndCompactButtons(
        bool alwaysDisplay,
        bool compactLayout,
        bool expectedPrimary,
        bool expectedCompact)
    {
        var viewModel = CreateViewModel();

        viewModel.UpdateCloseButtonVisibility(alwaysDisplay, compactLayout);

        Assert.Equal(expectedPrimary, viewModel.IsPrimaryCloseVisible);
        Assert.Equal(expectedCompact, viewModel.IsCompactCloseVisible);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UpdateSeekbarVisibility_UpdatesBindableState(bool isVisible)
    {
        var viewModel = CreateViewModel();

        viewModel.UpdateSeekbarVisibility(isVisible);

        Assert.Equal(isVisible, viewModel.IsSeekbarVisible);
    }

    [Fact]
    public void SetAndClearMediaIdentity_UpdateBindableState()
    {
        var viewModel = CreateViewModel();
        var icon = new DrawingImage();

        viewModel.SetMediaIdentity("Spotify", icon);

        Assert.True(viewModel.IsMediaIdVisible);
        Assert.Equal("Spotify", viewModel.MediaIdText);
        Assert.Same(icon, viewModel.MediaIdIcon);

        viewModel.ClearMediaIdentity();

        Assert.False(viewModel.IsMediaIdVisible);
        Assert.Equal(string.Empty, viewModel.MediaIdText);
        Assert.Null(viewModel.MediaIdIcon);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        var nowPlaying = (NowPlayingViewModel)RuntimeHelpers.GetUninitializedObject(typeof(NowPlayingViewModel));
        return new MainWindowViewModel(
            nowPlaying,
            new MockPlaybackService(),
            new MockSettingsService(),
            new MockAppShellService(),
            new MockPlaybackSourceResolver());
    }

    private class MockPlaybackService : IPlaybackService
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public event System.EventHandler? QueueChanged { add { } remove { } }
        public event System.EventHandler? TrackChanged { add { } remove { } }
        public event System.EventHandler<UpcomingTrackEventArgs>? UpcomingTrackDue { add { } remove { } }
        public event System.EventHandler<string>? PlaybackError { add { } remove { } }
        public System.Collections.Generic.IReadOnlyList<TrackModel> CurrentQueue => null!;
        public TrackModel? CurrentTrack => null;
        public int CurrentQueueIndex => 0;
        public bool CanUndo => false;
        public QueueUndoActionKind UndoActionKind => QueueUndoActionKind.None;
        public bool IsShuffleEnabled { get; set; }
        public bool IsPlaying { get; set; }
        public System.TimeSpan CurrentPosition { get; set; }
        public System.TimeSpan TotalDuration => System.TimeSpan.Zero;
        public float Volume { get; set; }
        public RepeatMode RepeatMode { get; set; }
        public bool CanGoNext => false;
        public bool CanGoPrevious => false;
        public LyricLine? CurrentLyricLine => null;
        public System.Collections.Generic.List<LyricLine> CurrentLyrics => null!;
        public void AddToQueue(TrackModel track) {}
        public void ClearQueuePreservingCurrent() {}
        public System.Threading.Tasks.Task ImportQueueAsync(string filePath) => System.Threading.Tasks.Task.CompletedTask;
        public void ExportQueue(string filePath) {}
        public void MoveTrack(int oldIndex, int newIndex) {}
        public void MoveTracks(System.Collections.Generic.IReadOnlyList<int> oldIndices, int newIndex) {}
        public void Play(TrackModel track) {}
        public void PlayAtIndex(int index) {}
        public void PlayQueue(System.Collections.Generic.IReadOnlyList<TrackModel> tracks, int startIndex) {}
        public void PlaySingle(TrackModel track, System.Collections.Generic.IReadOnlyList<TrackModel> visibleTracks) {}
        public void RemoveFromQueue(TrackModel track) {}
        public void Undo() {}
        public void Play() {}
        public void Pause() {}
        public void Stop() {}
        public bool PlayNext() => false;
        public bool PlayPrevious() => false;
        public void TogglePlayPause() {}
        public void Seek(System.TimeSpan position) {}
    }

    private class MockSettingsService : ISettingsService
    {
        public UserSettings Current { get; } = new UserSettings();
        public void Save() {}
    }

    private class MockAppShellService : IAppShellService
    {
        public void OpenSettings() {}
        public void OpenRepository() {}
        public void OpenLogsFolder() {}
        public void ReportBug() {}
    }

    private class MockPlaybackSourceResolver : IPlaybackSourceResolver
    {
        public ResolvedPlaybackSource Resolve() => new(PlaybackSourceKind.None);
    }
}
