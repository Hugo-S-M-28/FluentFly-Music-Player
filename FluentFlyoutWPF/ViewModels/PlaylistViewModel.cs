using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace FluentFlyoutWPF.ViewModels;

public partial class PlaylistViewModel : ObservableObject
{
    private DispatcherTimer? _searchDebounceTimer;
    private DispatcherTimer? _undoBannerTimer;
    private readonly IPlaybackService _playbackService;
    private readonly ILibraryService _libraryService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IDialogService _dialogService;
    private readonly ISystemShellService _systemShellService;
    private readonly IWindowService _windowService;

    public UserSettings Settings => SettingsManager.Current;
    public ObservableCollection<TrackModel> PlaylistItems { get; } = [];
    public ICollectionView PlaylistView { get; }

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string queueSummaryText = string.Empty;

    [ObservableProperty]
    private bool canUndo;

    [ObservableProperty]
    private string undoBannerText = string.Empty;

    [ObservableProperty]
    private bool isUndoBannerVisible;

    public PlaylistViewModel(
        IPlaybackService playbackService,
        ILibraryService libraryService,
        IFileDialogService fileDialogService,
        IDialogService dialogService,
        ISystemShellService systemShellService,
        IWindowService windowService)
    {
        _playbackService = playbackService;
        _libraryService = libraryService;
        _fileDialogService = fileDialogService;
        _dialogService = dialogService;
        _systemShellService = systemShellService;
        _windowService = windowService;

        PlaylistView = CollectionViewSource.GetDefaultView(PlaylistItems);
        PlaylistView.Filter = FilterPlaylist;

        UndoBannerText = LocalizationManager.GetString("Queue_ChangesSaved");
        LocalizationManager.LocalizationChanged += HandleLocalizationChanged;
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer?.Stop();
            PlaylistView.Refresh();
        };
        _searchDebounceTimer.Start();
    }

    public void SyncQueue(IReadOnlyList<TrackModel> source)
    {
        if (PlaylistItems.Count == source.Count)
        {
            bool identical = true;
            for (int i = 0; i < source.Count; i++)
            {
                if (!ReferenceEquals(PlaylistItems[i], source[i]))
                {
                    identical = false;
                    break;
                }
            }

            if (identical)
            {
                // Ensure DisplayIndex is always populated even when the list did not change
                // (e.g. first call after app start where items are already in sync)
                for (int i = 0; i < PlaylistItems.Count; i++)
                {
                    PlaylistItems[i].DisplayIndex = i + 1;
                }

                UpdateQueueSummary();
                return;
            }

        }

        var sourceSet = new HashSet<TrackModel>(source, TrackReferenceEqualityComparer.Instance);

        for (int i = PlaylistItems.Count - 1; i >= 0; i--)
        {
            if (!sourceSet.Contains(PlaylistItems[i]))
            {
                PlaylistItems.RemoveAt(i);
            }
        }

        for (int i = 0; i < source.Count; i++)
        {
            var expected = source[i];

            if (i < PlaylistItems.Count)
            {
                if (ReferenceEquals(PlaylistItems[i], expected))
                {
                    continue;
                }

                int existingIndex = -1;
                for (int j = i + 1; j < PlaylistItems.Count; j++)
                {
                    if (ReferenceEquals(PlaylistItems[j], expected))
                    {
                        existingIndex = j;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    PlaylistItems.Move(existingIndex, i);
                }
                else
                {
                    PlaylistItems.Insert(i, expected);
                }
            }
            else
            {
                PlaylistItems.Add(expected);
            }
        }

        while (PlaylistItems.Count > source.Count)
        {
            PlaylistItems.RemoveAt(PlaylistItems.Count - 1);
        }

        for (int i = 0; i < PlaylistItems.Count; i++)
        {
            PlaylistItems[i].DisplayIndex = i + 1;
        }

        UpdateQueueSummary();
    }

    public void UpdateUndoState()
    {
        CanUndo = _playbackService.CanUndo;
        var undoActionKind = _playbackService.UndoActionKind;

        if (CanUndo)
        {
            UndoBannerText = LocalizeUndoAction(undoActionKind);
            IsUndoBannerVisible = true;

            _undoBannerTimer?.Stop();
            _undoBannerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(6)
            };
            _undoBannerTimer.Tick += (_, _) =>
            {
                _undoBannerTimer?.Stop();
                IsUndoBannerVisible = false;
            };
            _undoBannerTimer.Start();
        }
        else
        {
            _undoBannerTimer?.Stop();
            IsUndoBannerVisible = false;
        }
    }

    public void UpdateQueueSummary()
    {
        int count = PlaylistItems.Count;
        double totalSecs = 0;

        foreach (var item in PlaylistItems)
        {
            totalSecs += item.Duration.TotalSeconds;
        }

        var totalDuration = TimeSpan.FromSeconds(totalSecs);
        string durationStr = totalDuration.TotalHours >= 1
                ? string.Format(
                LocalizationManager.GetString("Playlist_DurationHoursMinutesFormat"),
                (int)totalDuration.TotalHours,
                totalDuration.Minutes)
            : string.Format(
                LocalizationManager.GetString("Playlist_DurationMinutesSecondsFormat"),
                totalDuration.Minutes,
                totalDuration.Seconds);

        string summaryFormat = count == 1
            ? LocalizationManager.GetString("Playlist_SummarySingleFormat")
            : LocalizationManager.GetString("Playlist_SummaryPluralFormat");

        QueueSummaryText = string.Format(summaryFormat, count, durationStr);
    }

    private bool FilterPlaylist(object item)
    {
        if (item is not TrackModel track)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return (track.Title?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
               (track.Artist?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true) ||
               (track.Album?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true);
    }

    [RelayCommand]
    public void ProcessDrop(DropPayload payload)
    {
        if (payload.Files != null)
        {
            int targetIndex = payload.TargetIndex ?? _playbackService.CurrentQueue.Count;
            foreach (var path in payload.Files)
            {
                var track = _libraryService.Tracks.FirstOrDefault(t => t.FilePath == path);
                if (track == null)
                {
                    continue;
                }

                _playbackService.AddToQueue(track);
                int addedIndex = _playbackService.CurrentQueue.Count - 1;
                if (addedIndex != targetIndex && addedIndex > 0)
                {
                    _playbackService.MoveTrack(addedIndex, targetIndex);
                    targetIndex++;
                }
            }

            return;
        }

        if (payload.MovedIndices != null)
        {
            int newIndex = payload.TargetIndex ?? 0;
            if (payload.MovedIndices.Count == 1)
            {
                int oldIndex = payload.MovedIndices[0];
                var queue = _playbackService.CurrentQueue;
                if (newIndex > queue.Count - 1)
                {
                    newIndex = queue.Count - 1;
                }
                if (newIndex < 0)
                {
                    newIndex = 0;
                }

                if (oldIndex != newIndex)
                {
                    _playbackService.MoveTrack(oldIndex, newIndex);
                }
            }
            else
            {
                _playbackService.MoveTracks(payload.MovedIndices, newIndex);
            }
        }
    }

    [RelayCommand]
    public async Task LoadPlaylistAsync()
    {
        var filePath = _fileDialogService.OpenFile(
            LocalizationManager.GetString("Playlist_LoadTitle"),
            LocalizationManager.GetString("Playlist_Filter"));

        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                await _playbackService.ImportQueueAsync(filePath);
            }
            catch (Exception ex)
            {
                var errorTitle = LocalizationManager.GetString("Playlist_ErrorLoading");
                await _dialogService.ShowErrorAsync(errorTitle, $"{errorTitle}: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public async Task SavePlaylistAsync()
    {
        if (PlaylistItems.Count == 0)
        {
            await _dialogService.ShowMessageAsync(
                LocalizationManager.GetString("Playlist_EmptyTitle"),
                LocalizationManager.GetString("Playlist_EmptyMessage"));
            return;
        }

        var filePath = _fileDialogService.SaveFile(
            LocalizationManager.GetString("Playlist_SaveTitle"),
            LocalizationManager.GetString("Playlist_FilterSave"),
            LocalizationManager.GetString("Playlist_SaveTitle"));

        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                _playbackService.ExportQueue(filePath);
            }
            catch (Exception ex)
            {
                var errorTitle = LocalizationManager.GetString("Playlist_ErrorSaving");
                await _dialogService.ShowErrorAsync(errorTitle, $"{errorTitle}: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void ClearQueue()
    {
        _playbackService.ClearQueuePreservingCurrent();
    }

    [RelayCommand]
    public void Undo()
    {
        if (_playbackService.CanUndo)
        {
            _playbackService.Undo();
        }
    }

    [RelayCommand]
    public void PlayTrack(TrackModel? track)
    {
        if (track == null)
        {
            return;
        }

        var queue = _playbackService.CurrentQueue;
        for (int i = 0; i < queue.Count; i++)
        {
            if (ReferenceEquals(queue[i], track))
            {
                _playbackService.PlayAtIndex(i);
                return;
            }
        }

        _playbackService.Play(track);
    }

    [RelayCommand]
    public void PlayNext(TrackModel? track)
    {
        if (track == null)
        {
            return;
        }

        int currentIndex = _playbackService.CurrentQueueIndex;
        if (currentIndex < 0)
        {
            return;
        }

        int trackIndex = PlaylistItems.IndexOf(track);
        if (trackIndex >= 0 && trackIndex != currentIndex + 1)
        {
            _playbackService.MoveTrack(trackIndex, currentIndex + 1);
        }
    }

    [RelayCommand]
    public void RemoveFromQueue(TrackModel? track)
    {
        if (track != null)
        {
            _playbackService.RemoveFromQueue(track);
        }
    }

    [RelayCommand]
    public void OpenLocation(TrackModel? track)
    {
        if (track != null)
        {
            _systemShellService.RevealInFileExplorer(track.FilePath);
        }
    }

    [RelayCommand]
    public void ViewInfo(TrackModel? track)
    {
        if (track != null)
        {
            _windowService.ShowEditTrack(track);
        }
    }

    private void HandleLocalizationChanged(object? sender, EventArgs e)
    {
        if (!CanUndo)
        {
            UndoBannerText = LocalizationManager.GetString("Queue_ChangesSaved");
        }
        else
        {
            UndoBannerText = LocalizeUndoAction(_playbackService.UndoActionKind);
        }

        UpdateQueueSummary();
    }

    private static string LocalizeUndoAction(QueueUndoActionKind actionKind)
    {
        return actionKind switch
        {
            QueueUndoActionKind.PlaylistLoaded => LocalizationManager.GetString("Queue_PlaylistLoaded"),
            QueueUndoActionKind.QueueCleared => LocalizationManager.GetString("Queue_Cleared"),
            QueueUndoActionKind.TrackRemoved => LocalizationManager.GetString("Queue_TrackRemoved"),
            QueueUndoActionKind.TracksRemoved => LocalizationManager.GetString("Queue_TracksRemoved"),
            _ => LocalizationManager.GetString("Queue_ChangesSaved"),
        };
    }
}
