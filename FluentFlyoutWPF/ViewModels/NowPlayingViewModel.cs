// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using Windows.Media.Control;

namespace FluentFlyoutWPF.ViewModels;

public partial class NowPlayingViewModel : ObservableObject
{
    private bool _isUserSeeking;
    private System.Threading.CancellationTokenSource? _syncCts;
    public UserSettings Settings => SettingsManager.Current;

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string artist;

    private readonly IDialogService _dialogService;

    public NowPlayingViewModel(IDialogService dialogService)
    {
        _dialogService = dialogService;
        MusicPlayerService.Instance.PlaybackError += async (s, msg) =>
        {
            var titleStr = System.Windows.Application.Current.TryFindResource("Edit_ErrorTitle")?.ToString() ?? "Error";
            await _dialogService.ShowErrorAsync(titleStr, msg);
        };
        title = System.Windows.Application.Current.Resources["Player_NoTrack"] as string ?? "No track playing";
        artist = System.Windows.Application.Current.Resources["Player_SelectTrackMsg"] as string ?? "Select a track from the library";
        volume = MusicPlayerService.Instance.Volume;
        playPauseSymbol = Wpf.Ui.Controls.SymbolRegular.Play24;
        playPauseCompactSymbol = Wpf.Ui.Controls.SymbolRegular.Play16;
        shuffleSymbol = Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24;
        repeatSymbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAllOff24;
        playPauseOpacity = 1.0;
        transportOpacity = 1.0;
        shuffleIconOpacity = 0.5;
        repeatIconOpacity = 0.5;
        shuffleVisibility = Visibility.Visible;
        repeatVisibility = Visibility.Visible;
        backdropImage = MediaBackdropStyleService.GetFallbackImageSource();
        shuffleForeground = GetDefaultForeground();
        repeatForeground = GetDefaultForeground();

        // Auto-sync with MusicPlayerService
        MusicPlayerService.Instance.TrackChanged += (s, e) => SyncWithPlayer();
        MusicPlayerService.Instance.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(MusicPlayerService.IsPlaying) || 
                e.PropertyName == nameof(MusicPlayerService.CurrentTrack))
            {
                SyncWithPlayer();
            }
            else if (e.PropertyName == nameof(MusicPlayerService.CurrentPosition))
            {
                UpdatePosition();
            }
            else if (e.PropertyName == nameof(MusicPlayerService.CurrentLyricLine))
            {
                ActiveLyricLine = MusicPlayerService.Instance.CurrentLyricLine;
                Lyrics = ActiveLyricLine?.Text ?? string.Empty;
            }
            else if (e.PropertyName == nameof(MusicPlayerService.Volume))
            {
                Volume = MusicPlayerService.Instance.Volume;
            }
        };

        // Auto-sync with ExternalMediaService
        ExternalMediaService.Instance.MediaPropertyChanged += (session, props) => SyncWithPlayer();
        ExternalMediaService.Instance.PlaybackStateChanged += (session, info) => SyncWithPlayer();
        ExternalMediaService.Instance.TimelinePropertyChanged += (session, props) => UpdatePosition();

        SyncWithPlayer();
    }

    public void RefreshFromServices()
    {
        SyncWithPlayer();
    }

    private void SyncWithPlayer()
    {
        _syncCts?.Cancel();
        _syncCts = new System.Threading.CancellationTokenSource();
        _ = SyncWithPlayerAsync(_syncCts.Token);
    }

    private async Task SyncWithPlayerAsync(System.Threading.CancellationToken ct)
    {
        await Task.Yield();
        if (ct.IsCancellationRequested) return;

        var resolved = PlaybackSourceResolver.Resolve();

        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            var track = MusicPlayerService.Instance.CurrentTrack;
            if (track != null)
            {
                if (ct.IsCancellationRequested) return;
                Title = track.Title;
                Artist = track.FullArtistDisplay;
                HasTrack = true;
                CurrentTrack = track;
                CoverImage = track.AlbumArt ?? LibraryManager.Instance.GetAlbumArt(track, 400);
                CoverUrl = track.CoverUrl;
                BackdropImage = CoverImage ?? MediaBackdropStyleService.GetFallbackImageSource();
                IsPlaying = MusicPlayerService.Instance.IsPlaying;
                TotalTime = track.Duration.ToString(@"mm\:ss");
                SeekMaximum = track.Duration.TotalSeconds;
                HasLyrics = SyncLyricsCollection(MusicPlayerService.Instance.CurrentLyrics);
                ActiveLyricLine = MusicPlayerService.Instance.CurrentLyricLine;
                Lyrics = ActiveLyricLine?.Text ?? string.Empty;
                UpdatePosition();
                UpdatePlaybackVisualState(IsPlaying);
                UpdateShuffleRepeatState(
                    SettingsManager.Current.ShuffleEnabled || IsShuffleEnabled,
                    MusicPlayerService.Instance.IsShuffleEnabled,
                    SettingsManager.Current.RepeatEnabled || RepeatMode != RepeatMode.None,
                    MusicPlayerService.Instance.RepeatMode);
                return;
            }
        }
        else if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var session = resolved.ExternalSession;
            var props = session.ControlSession.GetPlaybackInfo();
            if (ct.IsCancellationRequested) return;
            var mediaProps = await session.ControlSession.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
            
            if (mediaProps != null)
            {
                Title = mediaProps.Title;
                Artist = mediaProps.Artist;
                HasTrack = true;
                CoverImage = await BitmapHelper.GetThumbnailAsync(mediaProps.Thumbnail).ConfigureAwait(true);
                if (ct.IsCancellationRequested) return;
                CoverUrl = string.Empty;
                BackdropImage = CoverImage ?? MediaBackdropStyleService.GetFallbackImageSource();
                IsPlaying = props.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                
                var timeline = session.ControlSession.GetTimelineProperties();
                TotalTime = timeline.EndTime.ToString(@"mm\:ss");
                SeekMaximum = timeline.EndTime.TotalSeconds;
                HasLyrics = SyncLyricsCollection([]);
                ActiveLyricLine = null;
                Lyrics = string.Empty;
                UpdatePosition();
                UpdatePlaybackVisualState(IsPlaying);
                UpdateShuffleRepeatState(
                    SettingsManager.Current.ShuffleEnabled,
                    props.IsShuffleActive ?? false,
                    SettingsManager.Current.RepeatEnabled,
                    props.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.Track
                        ? RepeatMode.One
                        : props.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.List
                            ? RepeatMode.All
                            : RepeatMode.None);
                return;
            }
        }

        // 3. Default state
        Title = System.Windows.Application.Current.Resources["Player_NoTrack"] as string ?? "No track playing";
        Artist = System.Windows.Application.Current.Resources["Player_SelectTrackMsg"] as string ?? "Select a track from the library";
        HasTrack = false;
        CurrentTrack = null;
        CoverImage = null;
        CoverUrl = string.Empty;
        BackdropImage = MediaBackdropStyleService.GetFallbackImageSource();
        IsPlaying = false;
        CurrentTime = "0:00";
        TotalTime = "0:00";
        SeekValue = 0;
        SeekMaximum = 100;
        HasLyrics = SyncLyricsCollection([]);
        ActiveLyricLine = null;
        Lyrics = string.Empty;
        UpdatePlaybackVisualState(false, false);
        UpdateShuffleRepeatState(SettingsManager.Current.ShuffleEnabled, false, SettingsManager.Current.RepeatEnabled, RepeatMode.None);
    }

    public void UpdatePosition()
    {
        if (_isUserSeeking)
        {
            return;
        }

        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            var pos = MusicPlayerService.Instance.CurrentPosition;
            SeekValue = pos.TotalSeconds;
            CurrentTime = pos.ToString(@"mm\:ss");
            CurrentPositionTimeSpan = pos;
        }
        else if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var session = resolved.ExternalSession;
            var timeline = session.ControlSession.GetTimelineProperties();
            SeekValue = timeline.Position.TotalSeconds;
            CurrentTime = timeline.Position.ToString(@"mm\:ss");
            CurrentPositionTimeSpan = timeline.Position;
        }
    }

    public void UpdateTimeline(TimeSpan position, TimeSpan maximum)
    {
        if (!_isUserSeeking)
        {
            SeekValue = position.TotalSeconds;
            CurrentPositionTimeSpan = position;
            CurrentTime = position.ToString(position.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
        }

        SeekMaximum = maximum.TotalSeconds;
        TotalTime = maximum.ToString(maximum.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
    }

    public void BeginSeekInteraction()
    {
        _isUserSeeking = true;
    }

    public void UpdateSeekPreview(double seekValue)
    {
        SeekValue = seekValue;
        CurrentPositionTimeSpan = TimeSpan.FromSeconds(seekValue);
        CurrentTime = CurrentPositionTimeSpan.ToString(CurrentPositionTimeSpan.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss");
    }

    public void EndSeekInteraction()
    {
        _isUserSeeking = false;
    }

    public bool IsUserSeeking => _isUserSeeking;

    public void CommitSeek()
    {
        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            if (MusicPlayerService.Instance.CurrentTrack != null)
            {
                MusicPlayerService.Instance.Seek(TimeSpan.FromSeconds(SeekValue));
            }
        }
        else if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var seekPosition = TimeSpan.FromSeconds(SeekValue);
            long ticks = seekPosition.Ticks > 0 ? seekPosition.Ticks : 1;
            _ = resolved.ExternalSession.ControlSession.TryChangePlaybackPositionAsync(ticks);
        }
    }

    public void SeekFromLyricsTarget(object? target)
    {
        switch (target)
        {
            case LyricLine line:
                MusicPlayerService.Instance.Seek(line.Time);
                break;
            case LyricWord word:
                MusicPlayerService.Instance.Seek(word.Time);
                break;
        }
    }

    [RelayCommand]
    private void HidePlaylist()
    {
        IsPlaylistVisible = false;
    }

    [RelayCommand]
    private void ResumeLyricsSync()
    {
        CanResumeLyricsSync = false;
    }

    public void SetCompactTransportState(bool canPlayPause, bool isPlaying, bool canSkip)
    {
        PlayPauseCompactSymbol = canPlayPause
            ? (isPlaying ? Wpf.Ui.Controls.SymbolRegular.Pause16 : Wpf.Ui.Controls.SymbolRegular.Play16)
            : Wpf.Ui.Controls.SymbolRegular.Stop16;
        IsPlayPauseEnabled = canPlayPause;
        PlayPauseOpacity = canPlayPause ? 1.0 : 0.35;
        AreTransportControlsEnabled = canSkip;
        TransportOpacity = canSkip ? 1.0 : 0.35;
    }

    public void UpdateShuffleRepeatState(bool showShuffle, bool isShuffle, bool showRepeat, RepeatMode repeatMode)
    {
        ShuffleVisibility = showShuffle ? Visibility.Visible : Visibility.Collapsed;
        RepeatVisibility = showRepeat ? Visibility.Visible : Visibility.Collapsed;

        IsShuffleEnabled = isShuffle;
        RepeatMode = repeatMode;

        ShuffleSymbol = isShuffle
            ? Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24
            : Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24;
        ShuffleIconOpacity = isShuffle ? 1.0 : 0.4;

        switch (repeatMode)
        {
            case RepeatMode.One:
                RepeatSymbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeat124;
                RepeatIconOpacity = 1.0;
                break;
            case RepeatMode.All:
                RepeatSymbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAll24;
                RepeatIconOpacity = 1.0;
                break;
            default:
                RepeatSymbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAllOff24;
                RepeatIconOpacity = 0.4;
                break;
        }
    }

    public void ApplyRepeatShuffleForegrounds(Brush activeBrush, Brush inactiveBrush)
    {
        ShuffleForeground = IsShuffleEnabled ? activeBrush : inactiveBrush;
        RepeatForeground = RepeatMode == RepeatMode.None ? inactiveBrush : activeBrush;
    }

    private void UpdatePlaybackVisualState(bool isPlaying, bool canPlayPause = true)
    {
        PlayPauseSymbol = isPlaying
            ? Wpf.Ui.Controls.SymbolRegular.Pause24
            : Wpf.Ui.Controls.SymbolRegular.Play24;
        SetCompactTransportState(canPlayPause, isPlaying, canPlayPause);
    }

    private bool SyncLyricsCollection(IReadOnlyList<LyricLine> source)
    {
        if (LyricLines.Count != source.Count || LyricLines.Where((line, index) => !EqualityComparer<LyricLine>.Default.Equals(line, source[index])).Any())
        {
            LyricLines.Clear();
            foreach (var line in source)
            {
                LyricLines.Add(line);
            }
        }

        return source.Count > 0;
    }

    [ObservableProperty]
    private BitmapImage? coverImage;

    [ObservableProperty]
    private ImageSource? backdropImage;

    [ObservableProperty]
    private string lyrics = string.Empty;

    [ObservableProperty]
    private bool hasTrack;

    [ObservableProperty]
    private bool isPlaylistVisible;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private Wpf.Ui.Controls.SymbolRegular playPauseSymbol;

    [ObservableProperty]
    private Wpf.Ui.Controls.SymbolRegular playPauseCompactSymbol;

    [ObservableProperty]
    private double playPauseOpacity;

    [ObservableProperty]
    private bool isPlayPauseEnabled = true;

    [ObservableProperty]
    private bool areTransportControlsEnabled = true;

    [ObservableProperty]
    private double transportOpacity = 1.0;

    [ObservableProperty]
    private string currentTime = "0:00";

    [ObservableProperty]
    private TimeSpan currentPositionTimeSpan;

    [ObservableProperty]
    private string totalTime = "0:00";

    [ObservableProperty]
    private double seekValue;

    [ObservableProperty]
    private double seekMaximum = 100;

    [ObservableProperty]
    private double volume = 0.8;

    [ObservableProperty]
    private bool isShuffleEnabled;

    [ObservableProperty]
    private RepeatMode repeatMode = RepeatMode.None;

    [ObservableProperty]
    private Wpf.Ui.Controls.SymbolRegular shuffleSymbol;

    [ObservableProperty]
    private Wpf.Ui.Controls.SymbolRegular repeatSymbol;

    [ObservableProperty]
    private double shuffleIconOpacity;

    [ObservableProperty]
    private double repeatIconOpacity;

    [ObservableProperty]
    private Visibility shuffleVisibility = Visibility.Visible;

    [ObservableProperty]
    private Visibility repeatVisibility = Visibility.Visible;

    [ObservableProperty]
    private Brush shuffleForeground;

    [ObservableProperty]
    private Brush repeatForeground;

    [ObservableProperty]
    private TrackModel? currentTrack;

    [ObservableProperty]
    private string coverUrl = string.Empty;

    [ObservableProperty]
    private ObservableCollection<LyricLine> lyricLines = new();

    [ObservableProperty]
    private LyricLine? activeLyricLine;

    [ObservableProperty]
    private bool hasLyrics;

    [ObservableProperty]
    private bool canResumeLyricsSync;

    [ObservableProperty]
    private System.Windows.Media.Brush pressedForeground = System.Windows.Media.Brushes.White;

    /// <summary>
    /// Returns the correct primary-text brush for the current app theme.
    /// Uses the dynamic WPF-UI resource so it is always right for both Light and Dark themes.
    /// </summary>
    private static Brush GetDefaultForeground()
        => System.Windows.Application.Current?.TryFindResource("TextFillColorPrimaryBrush") as Brush
           ?? SystemColors.ControlTextBrush;

    partial void OnVolumeChanged(double value)
    {
        if (Math.Abs(MusicPlayerService.Instance.Volume - value) > 0.001)
        {
            MusicPlayerService.Instance.Volume = (float)value;
        }
    }

    [RelayCommand]
    private async Task PlayPause()
    {
        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            MusicPlayerService.Instance.TogglePlayPause();
        }
        else if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var session = resolved.ExternalSession;
            var status = session.ControlSession.GetPlaybackInfo().PlaybackStatus;
            if (status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                await session.ControlSession.TryPauseAsync();
            else
                await session.ControlSession.TryPlayAsync();
        }
    }

    [RelayCommand]
    private async Task SkipNext()
    {
        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            MusicPlayerService.Instance.PlayNext();
        }
        else if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            await resolved.ExternalSession.ControlSession.TrySkipNextAsync();
        }
    }

    [RelayCommand]
    private async Task SkipPrevious()
    {
        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            MusicPlayerService.Instance.PlayPrevious();
        }
        else if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            await resolved.ExternalSession.ControlSession.TrySkipPreviousAsync();
        }
    }

    [RelayCommand]
    private async Task ToggleShuffle()
    {
        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            MusicPlayerService.Instance.IsShuffleEnabled = !MusicPlayerService.Instance.IsShuffleEnabled;
            IsShuffleEnabled = MusicPlayerService.Instance.IsShuffleEnabled;
        }
        else if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var session = resolved.ExternalSession;
            bool current = session.ControlSession.GetPlaybackInfo().IsShuffleActive ?? false;
            await session.ControlSession.TryChangeShuffleActiveAsync(!current);
        }
    }

    [RelayCommand]
    private async Task ToggleRepeat()
    {
        var resolved = PlaybackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            var mode = MusicPlayerService.Instance.RepeatMode;
            MusicPlayerService.Instance.RepeatMode = mode switch
            {
                RepeatMode.None => RepeatMode.All,
                RepeatMode.All => RepeatMode.One,
                RepeatMode.One => RepeatMode.None,
                _ => RepeatMode.None
            };
            RepeatMode = MusicPlayerService.Instance.RepeatMode;
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
    private void EditLyrics()
    {
        if (MusicPlayerService.Instance.CurrentTrack != null)
        {
            ServiceLocator.Windows.ShowEditTrack(MusicPlayerService.Instance.CurrentTrack, true);
        }
    }

    [RelayCommand]
    private void OpenEqualizer()
    {
        ServiceLocator.Windows.ShowEqualizer();
    }
}
