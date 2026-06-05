using System.ComponentModel;
using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Classes.Services;

public interface IPlaybackService : INotifyPropertyChanged
{
    event EventHandler? QueueChanged;
    event EventHandler? TrackChanged;
    event EventHandler<string>? PlaybackError;

    IReadOnlyList<TrackModel> CurrentQueue { get; }
    TrackModel? CurrentTrack { get; }
    int CurrentQueueIndex { get; }
    bool CanUndo { get; }
    string UndoActionName { get; }
    bool IsShuffleEnabled { get; set; }

    void AddToQueue(TrackModel track);
    void ClearQueuePreservingCurrent();
    Task ImportQueueAsync(string filePath);
    void ExportQueue(string filePath);
    void MoveTrack(int oldIndex, int newIndex);
    void MoveTracks(IReadOnlyList<int> oldIndices, int newIndex);
    void Play(TrackModel track);
    void PlayAtIndex(int index);
    void PlayQueue(IReadOnlyList<TrackModel> tracks, int startIndex);
    void PlaySingle(TrackModel track, IReadOnlyList<TrackModel> visibleTracks);
    void RemoveFromQueue(TrackModel track);
    void Undo();
}
