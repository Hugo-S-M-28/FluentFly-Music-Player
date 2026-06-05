using System;
using System.Linq;
using System.Windows.Media;
using Windows.Media.Control;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyout.Windows;
using FluentFlyout.Controls;

namespace FluentFlyoutWPF.Classes.Coordinators;

public class TaskbarIntegrationCoordinator : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly MainWindow _mainWindow;
    private readonly IWindowManager _windowManager = App.GetRequiredService<IWindowManager>();
    private TaskbarWindow? _taskbarWindow;

    public TaskbarWindow? TaskbarWindow => _taskbarWindow;

    public TaskbarIntegrationCoordinator(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        _taskbarWindow = _windowManager.ShowTaskbarWindow();
        UpdateTaskbar();
    }

    public void RecreateTaskbarWindow()
    {
        try
        {
            Logger.Info("Recreating Taskbar Widget window");

            if (_taskbarWindow != null)
            {
                try
                {
                    _taskbarWindow.Close();
                }
                catch (Exception ex)
                {
                    Logger.Debug(ex, "Ignored error during TaskbarWindow close");
                }
                _taskbarWindow = null;
            }

            _taskbarWindow = _windowManager.RecreateTaskbarWindow();
            UpdateTaskbar();

            Logger.Info("Taskbar Widget window recreated successfully");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to recreate Taskbar Widget window");
        }
    }

    public void UpdateTaskbar()
    {
        if (_taskbarWindow == null) return;

        bool isInternalActive = SettingsManager.Current.InternalPlayerEnabled && 
                               (MusicPlayerService.Instance.IsPlaying || MusicPlayerService.Instance.CurrentTrack != null);

        if (isInternalActive)
        {
            var track = MusicPlayerService.Instance.CurrentTrack;
            if (track != null)
            {
                var playbackStatus = MusicPlayerService.Instance.IsPlaying ? 
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing : 
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
                
                if (track.AlbumArt == null && !string.IsNullOrEmpty(track.AlbumArtPath))
                    track.AlbumArt = LibraryManager.Instance.GetAlbumArt(track, 400);

                BitmapHelper.SetCurrentBitmap(track.AlbumArt);
                _mainWindow.ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);
                
                _taskbarWindow.UpdateUi(track.Title, track.FullArtistDisplay, track.AlbumArt, playbackStatus, null);
                return;
            }
        }

        var focusedSession = ExternalMediaService.Instance.GetPreferredSession();
        
        if (ExternalMediaService.Instance.MediaManager != null && ExternalMediaService.Instance.MediaManager.IsStarted && focusedSession != null)
        {
            var songInfo = TryGetMediaProperties(focusedSession.ControlSession);
            if (songInfo != null)
            {
                var playbackInfo = focusedSession.ControlSession.GetPlaybackInfo();
                var thumbnail = BitmapHelper.GetThumbnail(songInfo.Thumbnail);
                _taskbarWindow.UpdateUi(songInfo.Title, songInfo.Artist, thumbnail, playbackInfo.PlaybackStatus, playbackInfo.Controls);
                return;
            }
        }

        // Fallback: If internal player is enabled and has a track, use its data directly
        if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            var track = MusicPlayerService.Instance.CurrentTrack;
            var playbackStatus = MusicPlayerService.Instance.IsPlaying ? 
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing : 
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
            
            if (track.AlbumArt == null && !string.IsNullOrEmpty(track.AlbumArtPath))
                track.AlbumArt = LibraryManager.Instance.GetAlbumArt(track, 400);

            BitmapHelper.SetCurrentBitmap(track.AlbumArt);
            _mainWindow.ApplyAccentColor(BitmapHelper.GetDominantColors(1).FirstOrDefault(), true);
            
            _taskbarWindow.UpdateUi(track.Title, track.FullArtistDisplay, track.AlbumArt, playbackStatus, null);
            return;
        }

        // If no media is found, clear the taskbar widget
        _taskbarWindow.UpdateUi("-", "-", null, GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed);
    }

    private static GlobalSystemMediaTransportControlsSessionMediaProperties? TryGetMediaProperties(GlobalSystemMediaTransportControlsSession controlSession)
    {
        try
        {
            return controlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            Logger.Error(ex, "Failed to retrieve data from the player");
            return null;
        }
    }

    public void Dispose()
    {
        try
        {
            TaskbarVisualizerControl.DisposeVisualizer();
            if (_taskbarWindow?.IsLoaded == true)
            {
                _taskbarWindow.Close();
            }
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Ignored error during TaskbarWindow dispose");
        }
    }
}
