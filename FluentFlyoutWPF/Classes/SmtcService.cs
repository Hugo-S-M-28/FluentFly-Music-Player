using System;
using System.IO;
using System.Threading.Tasks;
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
        try
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _smtc = SystemMediaTransportControls.GetForCurrentView();
                if (_smtc != null)
                {
                    _smtc.IsEnabled = true;
                    _smtc.IsPlayEnabled = true;
                    _smtc.IsPauseEnabled = true;
                    _smtc.IsNextEnabled = true;
                    _smtc.IsPreviousEnabled = true;
                    _smtc.IsStopEnabled = true;

                    _smtc.ButtonPressed += Smtc_ButtonPressed;
                    _smtc.PlaybackPositionChangeRequested += Smtc_PlaybackPositionChangeRequested;
                    _smtc.ShuffleEnabledChangeRequested += Smtc_ShuffleEnabledChangeRequested;
                    _smtc.AutoRepeatModeChangeRequested += Smtc_AutoRepeatModeChangeRequested;
                }
            });
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to initialize SMTC. Expected in non-interactive sessions.");
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

    public async void UpdateMetadata(TrackModel? track, TimeSpan currentPosition, TimeSpan totalDuration)
    {
        if (_smtc == null) return;

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
