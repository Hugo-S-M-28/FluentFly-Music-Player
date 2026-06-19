using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Windows.Media;
using Windows.Storage.Streams;
using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Classes;

public class SmtcService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static SmtcService? _instance;
    private static readonly object _instanceLock = new();

    private SystemMediaTransportControls? _smtc;
    private bool _initialized;

    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler<TimeSpan>? PositionChangeRequested;

    public static SmtcService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new SmtcService();
                }
            }
            return _instance;
        }
    }

    private SmtcService()
    {
        // SMTC initialization is deferred: it must be invoked explicitly with a valid
        // HWND via Initialize(IntPtr hwnd) once the main window has been shown.
    }

    /// <summary>
    /// Initializes the System Media Transport Controls and associates them with the
    /// given window handle. Safe to call multiple times; subsequent calls are no-ops.
    /// Must be called from the UI thread after the owning window has a valid HWND.
    /// </summary>
    public static void Initialize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        // Ensure the singleton exists on the UI thread (events are subscribed on the
        // SMTC instance, so the same dispatcher must own it).
        var app = Application.Current;
        if (app == null) return;
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => Initialize(hwnd));
            return;
        }

        if (_instance == null)
        {
            lock (_instanceLock)
            {
                _instance ??= new SmtcService();
            }
        }
        var service = _instance!;
        if (service._initialized) return;

        try
        {
            service._smtc = SystemMediaTransportControls.GetForCurrentView();
            if (service._smtc == null) return;

            // GetForCurrentView() returns a CoreWindow-backed SMTC that requires an
            // explicit HWND association in desktop (WPF) hosts. Without this call the
            // API throws 0x80070578 (Invalid window handle) the first time the
            // controls are touched.
            WinRT.Interop.InitializeWithWindow.Initialize(service._smtc, hwnd);

            service._smtc.IsEnabled = true;
            service._smtc.IsPlayEnabled = true;
            service._smtc.IsPauseEnabled = true;
            service._smtc.IsNextEnabled = true;
            service._smtc.IsPreviousEnabled = true;
            service._smtc.IsStopEnabled = true;

            service._smtc.ButtonPressed += service.Smtc_ButtonPressed;
            service._smtc.PlaybackPositionChangeRequested += service.Smtc_PlaybackPositionChangeRequested;
            service._smtc.ShuffleEnabledChangeRequested += service.Smtc_ShuffleEnabledChangeRequested;
            service._smtc.AutoRepeatModeChangeRequested += service.Smtc_AutoRepeatModeChangeRequested;

            service._initialized = true;
            Logger.Info("SMTC initialized successfully");
        }
        catch (Exception ex)
        {
            // Any failure here (missing UWP context, no HWND, COM interop) leaves SMTC
            // disabled. The rest of the app keeps working — the public methods below
            // no-op when _smtc is null.
            service._smtc = null;
            Logger.Warn(ex, "Failed to initialize SMTC; system media controls will be disabled.");
        }
    }

    public event EventHandler<bool>? ShuffleChangeRequested;
    public event EventHandler<MediaPlaybackAutoRepeatMode>? RepeatModeChangeRequested;

    private void Smtc_ShuffleEnabledChangeRequested(SystemMediaTransportControls sender, ShuffleEnabledChangeRequestedEventArgs args)
    {
        ShuffleChangeRequested?.Invoke(this, args.RequestedShuffleEnabled);
    }

    private void Smtc_AutoRepeatModeChangeRequested(SystemMediaTransportControls sender, AutoRepeatModeChangeRequestedEventArgs args)
    {
        RepeatModeChangeRequested?.Invoke(this, args.RequestedAutoRepeatMode);
    }

    private void Smtc_PlaybackPositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
    {
        PositionChangeRequested?.Invoke(this, args.RequestedPlaybackPosition);
    }

    private void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                PlayRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Pause:
                PauseRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Next:
                NextRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Previous:
                PreviousRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    public void UpdatePlaybackStatus(bool isPlaying)
    {
        if (_smtc != null)
        {
            _smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
        }
    }

    public async Task UpdateMetadataAsync(TrackModel? track, TimeSpan currentPosition, TimeSpan totalDuration)
    {
        if (_smtc == null) return;

        try
        {
            var updater = _smtc.DisplayUpdater;

            if (track == null)
            {
                updater.MusicProperties.Title = string.Empty;
                updater.MusicProperties.Artist = string.Empty;
                updater.MusicProperties.AlbumTitle = string.Empty;
                updater.Thumbnail = null;
                updater.Update();
                return;
            }

            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = track.Title;
            updater.MusicProperties.Artist = track.Artist;
            updater.MusicProperties.AlbumTitle = track.Album;

            if (!string.IsNullOrEmpty(track.AlbumArtPath) && File.Exists(track.AlbumArtPath))
            {
                try
                {
                    var file = await global::Windows.Storage.StorageFile.GetFileFromPathAsync(track.AlbumArtPath);
                    updater.Thumbnail = RandomAccessStreamReference.CreateFromFile(file);
                }
                catch { /* Ignore thumbnail errors */ }
            }
            else
            {
                updater.Thumbnail = null;
            }

            updater.Update();
            UpdateTimeline(currentPosition, totalDuration);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error updating SMTC metadata");
        }
    }

    public void UpdateTimeline(TimeSpan currentPosition, TimeSpan totalDuration)
    {
        if (_smtc == null || totalDuration <= TimeSpan.Zero) return;
        try
        {
            var timelineProperties = new SystemMediaTransportControlsTimelineProperties
            {
                StartTime = TimeSpan.Zero,
                MinSeekTime = TimeSpan.Zero,
                MaxSeekTime = totalDuration,
                EndTime = totalDuration,
                Position = currentPosition
            };
            _smtc.UpdateTimelineProperties(timelineProperties);
        }
        catch { /* SMTC updates can be finicky */ }
    }

    public void UpdateShuffleRepeat(bool isShuffle, MediaPlaybackAutoRepeatMode repeatMode)
    {
        if (_smtc == null) return;
        _smtc.ShuffleEnabled = isShuffle;
        _smtc.AutoRepeatMode = repeatMode;
    }
}
