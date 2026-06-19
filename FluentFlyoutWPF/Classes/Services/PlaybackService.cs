using System.ComponentModel;
using System.Linq;
using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Classes.Services;

public sealed class PlaybackService : IPlaybackService
{
    private MusicPlayerService Player => MusicPlayerService.Instance;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => Player.PropertyChanged += value;
        remove => Player.PropertyChanged -= value;
    }

    public event EventHandler? QueueChanged
    {
        add => Player.QueueChanged += value;
        remove => Player.QueueChanged -= value;
    }

    public event EventHandler? TrackChanged
    {
        add => Player.TrackChanged += value;
        remove => Player.TrackChanged -= value;
    }

    public event EventHandler<UpcomingTrackEventArgs>? UpcomingTrackDue
    {
        add => Player.UpcomingTrackDue += value;
        remove => Player.UpcomingTrackDue -= value;
    }

    public event EventHandler<string>? PlaybackError
    {
        add => Player.PlaybackError += value;
        remove => Player.PlaybackError -= value;
    }

    public IReadOnlyList<TrackModel> CurrentQueue => Player.CurrentQueue;
    public TrackModel? CurrentTrack => Player.CurrentTrack;
    public int CurrentQueueIndex => Player.CurrentQueueIndex;
    public bool CanUndo => Player.CanUndo;
    public QueueUndoActionKind UndoActionKind => Player.UndoActionKind;

    public bool IsShuffleEnabled
    {
        get => Player.IsShuffleEnabled;
        set => Player.IsShuffleEnabled = value;
    }

    public bool IsPlaying
    {
        get => Player.IsPlaying;
        set => Player.IsPlaying = value;
    }

    public TimeSpan CurrentPosition
    {
        get => Player.CurrentPosition;
        set => Player.CurrentPosition = value;
    }

    public TimeSpan TotalDuration => Player.TotalDuration;

    public float Volume
    {
        get => Player.Volume;
        set => Player.Volume = value;
    }

    public RepeatMode RepeatMode
    {
        get => Player.RepeatMode;
        set => Player.RepeatMode = value;
    }

    public bool CanGoNext => Player.CanGoNext;
    public bool CanGoPrevious => Player.CanGoPrevious;
    public LyricLine? CurrentLyricLine => Player.CurrentLyricLine;
    public List<LyricLine> CurrentLyrics => Player.CurrentLyrics;

    public void AddToQueue(TrackModel track) => Player.AddToQueue(track);
    public void ClearQueuePreservingCurrent() => Player.ClearQueuePreservingCurrent();
    public Task ImportQueueAsync(string filePath) => Player.ImportQueueAsync(filePath);
    public void ExportQueue(string filePath) => Player.ExportQueue(filePath);
    public void MoveTrack(int oldIndex, int newIndex) => Player.MoveTrack(oldIndex, newIndex);
    public void MoveTracks(IReadOnlyList<int> oldIndices, int newIndex) => Player.MoveTracks(oldIndices.ToList(), newIndex);
    public void Play(TrackModel track) => Player.Play(track);
    public void PlayAtIndex(int index) => Player.PlayAtIndex(index);
    public void PlayQueue(IReadOnlyList<TrackModel> tracks, int startIndex) => Player.PlayQueue(tracks.ToList(), startIndex);
    public void PlaySingle(TrackModel track, IReadOnlyList<TrackModel> visibleTracks) => Player.PlaySingle(track, visibleTracks.ToList());
    public void RemoveFromQueue(TrackModel track) => Player.RemoveFromQueue(track);
    public void Undo() => Player.Undo();

    public void Play() => Player.Play();
    public void Pause() => Player.Pause();
    public void Stop() => Player.Stop();
    public bool PlayNext() => Player.PlayNext();
    public bool PlayPrevious() => Player.PlayPrevious();
    public void TogglePlayPause() => Player.TogglePlayPause();
    public void Seek(TimeSpan position) => Player.Seek(position);
}
