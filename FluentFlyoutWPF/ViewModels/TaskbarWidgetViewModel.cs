using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Models;
using Windows.Media.Control;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.ViewModels;

public partial class TaskbarWidgetViewModel : ObservableObject, IDisposable
{
    private readonly IPlaybackService _playbackService;
    private readonly IPlaybackSourceResolver _playbackSourceResolver;
    private readonly ISettingsService _settingsService;
    private readonly System.Windows.Threading.DispatcherTimer? _progressTimer;

    [ObservableProperty]
    private Brush titleForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush secondaryForeground = Brushes.Transparent;

    [ObservableProperty]
    private Brush controlsForeground = Brushes.Transparent;

    public TaskbarWidgetViewModel(
        IPlaybackService playbackService,
        IPlaybackSourceResolver playbackSourceResolver,
        ISettingsService settingsService)
    {
        _playbackService = playbackService;
        _playbackSourceResolver = playbackSourceResolver;
        _settingsService = settingsService;

        UpdateAccentColors();

        WeakReferenceMessenger.Default.Register<UpdateAccentColorMessage>(this, (r, m) =>
        {
            System.Windows.Application.Current?.Dispatcher?.Invoke(UpdateAccentColors);
        });

        if (System.Windows.Application.Current != null)
        {
            _progressTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _progressTimer.Tick += (s, e) => RefreshProgressFromPlaybackSource();
            _progressTimer.Start();
        }
    }

    public TaskbarWidgetViewModel()
        : this(new DesignPlaybackService(), new DesignPlaybackSourceResolver(), new DesignSettingsService())
    {
    }

    public void UpdateAccentColors()
    {
        var albumArtBrush = BitmapHelper.SavedDominantColors.FirstOrDefault();
        bool useAccent = AccentColorResolver.ShouldUseAccent(albumArtBrush);
        // ResolveAccentBrush already implements the 3-level cascade
        // 1. AlbumArt -> 2. Custom -> 3. Neutral (ThemeResourceHelper.GetSecondaryTextSolidBrush).
        // Using it as the gate, instead of the raw albumArtBrush, makes the taskbar widget
        // honor the cascade for the Custom-without-album-art case (previously it fell
        // through to the neutral branch whenever albumArtBrush was null).
        var activeBrush = useAccent ? AccentColorResolver.ResolveAccentBrush(albumArtBrush) : null;
        bool isDarkTheme = ThemeResourceHelper.IsDarkTheme();

        Brush titleBrush;
        Brush secondaryBrush;
        Brush controlsBrush;

        if (useAccent && activeBrush != null)
        {
            var readableBrush = AccentColorResolver.ResolveReadableAccentBrush(albumArtBrush, isDarkTheme);
            titleBrush = readableBrush;
            secondaryBrush = new SolidColorBrush(readableBrush.Color) { Opacity = 0.78 };
            controlsBrush = readableBrush;
        }
        else
        {
            titleBrush = ThemeResourceHelper.GetPrimaryTextSolidBrush();
            secondaryBrush = ThemeResourceHelper.GetSecondaryTextSolidBrush();
            controlsBrush = ThemeResourceHelper.GetSecondaryTextSolidBrush();
        }

        TitleForeground = titleBrush;
        SecondaryForeground = secondaryBrush;
        ControlsForeground = controlsBrush;
    }

    public void Dispose()
    {
        _progressTimer?.Stop();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    public UserSettings Settings => _settingsService.Current;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string artist = string.Empty;

    [ObservableProperty]
    private ImageSource? coverImage;

    [ObservableProperty]
    private ImageSource? backdropImage;

    [ObservableProperty]
    private bool hasMedia;

    [ObservableProperty]
    private bool isPaused = true;

    [ObservableProperty]
    private string tooltipText = string.Empty;

    [ObservableProperty]
    private SymbolRegular playPauseSymbol = SymbolRegular.Pause24;

    [ObservableProperty]
    private SymbolRegular placeholderSymbol = SymbolRegular.MusicNote220;

    [ObservableProperty]
    private Visibility placeholderVisibility = Visibility.Visible;

    [ObservableProperty]
    private Visibility songInfoVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility artistVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility controlsVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private Visibility progressVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private double coverOpacity = 1;

    [ObservableProperty]
    private bool canPrevious;

    [ObservableProperty]
    private bool canPlayPause;

    [ObservableProperty]
    private bool canNext;

    [ObservableProperty]
    private double previousOpacity = 0.5;

    [ObservableProperty]
    private double playPauseOpacity = 0.5;

    [ObservableProperty]
    private double nextOpacity = 0.5;

    [ObservableProperty]
    private double progressRatio;

    public bool UpdatePlayback(
        string title,
        string artist,
        BitmapImage? icon,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus,
        GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
    {
        if (title == "-" && artist == "-")
        {
            ClearPlayback();
            return false;
        }

        string normalizedTitle = !string.IsNullOrEmpty(title) ? title : "-";
        string normalizedArtist = !string.IsNullOrEmpty(artist) ? artist : "-";
        bool trackChanged = !string.Equals(Title, normalizedTitle, StringComparison.Ordinal) ||
                            !string.Equals(Artist, normalizedArtist, StringComparison.Ordinal);

        Title = normalizedTitle;
        Artist = normalizedArtist;
        TooltipText = BuildTooltip(title, artist);
        HasMedia = true;
        IsPaused = playbackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlayPauseSymbol = IsPaused ? SymbolRegular.Play24 : SymbolRegular.Pause24;
        SongInfoVisibility = Visibility.Visible;
        ArtistVisibility = !string.IsNullOrEmpty(artist) ? Visibility.Visible : Visibility.Collapsed;
        ControlsVisibility = Settings.TaskbarWidgetControlsEnabled ? Visibility.Visible : Visibility.Collapsed;

        UpdateCoverState(icon);
        UpdateControlState(playbackControls);

        return trackChanged;
    }

    public void ClearPlayback()
    {
        HasMedia = false;
        IsPaused = true;
        Title = string.Empty;
        Artist = string.Empty;
        TooltipText = string.Empty;
        CoverImage = null;
        CoverOpacity = 1;
        PlaceholderSymbol = SymbolRegular.MusicNote220;
        PlaceholderVisibility = Visibility.Visible;
        SongInfoVisibility = Visibility.Collapsed;
        ArtistVisibility = Visibility.Collapsed;
        ControlsVisibility = Visibility.Collapsed;
        ProgressRatio = 0;
        ProgressVisibility = Visibility.Collapsed;
        CanPrevious = false;
        CanPlayPause = false;
        CanNext = false;
        PreviousOpacity = 0.5;
        PlayPauseOpacity = 0.5;
        NextOpacity = 0.5;
    }

    public void UpdateProgress(double currentSeconds, double totalSeconds)
    {
        currentSeconds = Math.Max(0, currentSeconds);
        totalSeconds = Math.Max(0, totalSeconds);

        if (totalSeconds <= 0)
        {
            ProgressRatio = 0;
            ProgressVisibility = Visibility.Collapsed;
            return;
        }

        ProgressRatio = Math.Clamp(currentSeconds / totalSeconds, 0, 1);
        ProgressVisibility = Visibility.Visible;
    }

    public void RefreshProgressFromPlaybackSource()
    {
        if (!HasMedia)
        {
            UpdateProgress(0, 0);
            return;
        }

        var resolved = _playbackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            var preferredSession = resolved.ExternalSession;
            var timeline = preferredSession.ControlSession.GetTimelineProperties();
            if (timeline == null)
            {
                UpdateProgress(0, 0);
                return;
            }

            var playbackInfo = preferredSession.ControlSession.GetPlaybackInfo();
            var currentPosition = timeline.Position;

            if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                currentPosition += DateTime.Now - timeline.LastUpdatedTime.DateTime;
            }

            var totalDuration = timeline.MaxSeekTime.TotalSeconds > 0
                ? timeline.MaxSeekTime
                : timeline.EndTime;

            if (totalDuration <= TimeSpan.Zero)
            {
                UpdateProgress(0, 0);
                return;
            }

            if (currentPosition > totalDuration)
            {
                currentPosition = totalDuration;
            }

            UpdateProgress(currentPosition.TotalSeconds, totalDuration.TotalSeconds);
            return;
        }

        if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            UpdateProgress(
                _playbackService.CurrentPosition.TotalSeconds,
                _playbackService.TotalDuration.TotalSeconds);
            return;
        }

        UpdateProgress(0, 0);
    }

    [RelayCommand]
    private async Task Previous()
    {
        var resolved = _playbackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            await resolved.ExternalSession.ControlSession.TrySkipPreviousAsync();
        }
        else if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            _playbackService.PlayPrevious();
        }
    }

    [RelayCommand]
    private async Task PlayPause()
    {
        var resolved = _playbackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            if (IsPaused)
            {
                await resolved.ExternalSession.ControlSession.TryPlayAsync();
            }
            else
            {
                await resolved.ExternalSession.ControlSession.TryPauseAsync();
            }
        }
        else if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            _playbackService.TogglePlayPause();
        }
    }

    [RelayCommand]
    private async Task Next()
    {
        var resolved = _playbackSourceResolver.Resolve();
        if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
        {
            await resolved.ExternalSession.ControlSession.TrySkipNextAsync();
        }
        else if (resolved.Kind == PlaybackSourceKind.Internal)
        {
            _playbackService.PlayNext();
        }
    }

    [RelayCommand]
    private void OpenFlyout()
    {
        WeakReferenceMessenger.Default.Send(new ShowMediaFlyoutMessage(toggleMode: true, forceShow: true));
    }

    private void UpdateCoverState(BitmapImage? icon)
    {
        CoverImage = icon;
        if (icon == null)
        {
            PlaceholderSymbol = SymbolRegular.MusicNote220;
            PlaceholderVisibility = Visibility.Visible;
            CoverOpacity = 1;
            return;
        }

        if (IsPaused)
        {
            PlaceholderSymbol = SymbolRegular.Pause24;
            PlaceholderVisibility = Visibility.Visible;
            CoverOpacity = 0.4;
        }
        else
        {
            PlaceholderVisibility = Visibility.Collapsed;
            CoverOpacity = 1;
        }
    }

    private void UpdateControlState(GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls)
    {
        if (Settings.TaskbarWidgetControlsEnabled && playbackControls != null)
        {
            SetControlState(
                playbackControls.IsPreviousEnabled,
                playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled,
                playbackControls.IsNextEnabled);
        }
        else if (Settings.TaskbarWidgetControlsEnabled)
        {
            SetControlState(
                _playbackService.CanGoPrevious,
                canPlayPause: true,
                _playbackService.CanGoNext);
        }
        else
        {
            SetControlState(false, false, false);
        }
    }

    private void SetControlState(bool canPrevious, bool canPlayPause, bool canNext)
    {
        CanPrevious = canPrevious;
        CanPlayPause = canPlayPause;
        CanNext = canNext;
        PreviousOpacity = canPrevious ? 1 : 0.5;
        PlayPauseOpacity = canPlayPause ? 1 : 0.5;
        NextOpacity = canNext ? 1 : 0.5;
    }

    private static string BuildTooltip(string title, string artist)
    {
        var tooltip = !string.IsNullOrEmpty(title) ? title : string.Empty;
        tooltip += !string.IsNullOrEmpty(artist) ? "\n\n" + artist : string.Empty;
        return tooltip;
    }

    private sealed class DesignPlaybackSourceResolver : IPlaybackSourceResolver
    {
        public ResolvedPlaybackSource Resolve() => new(PlaybackSourceKind.None);
    }

    private sealed class DesignSettingsService : ISettingsService
    {
        public UserSettings Current { get; } = new();
        public void Save()
        {
        }
    }

    private sealed class DesignPlaybackService : IPlaybackService
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public event EventHandler? QueueChanged { add { } remove { } }
        public event EventHandler? TrackChanged { add { } remove { } }
        public event EventHandler<UpcomingTrackEventArgs>? UpcomingTrackDue { add { } remove { } }
        public event EventHandler<string>? PlaybackError { add { } remove { } }

        public IReadOnlyList<TrackModel> CurrentQueue => [];
        public TrackModel? CurrentTrack => null;
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
