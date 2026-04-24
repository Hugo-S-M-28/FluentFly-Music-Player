using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;
using WindowsMediaController;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Utils;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF.Classes;

public class ExternalMediaService : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static ExternalMediaService? _instance;
    private static readonly object _instanceLock = new();

    public static ExternalMediaService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new ExternalMediaService();
                }
            }
            return _instance;
        }
    }

    public MediaManager? MediaManager { get; private set; }
    private bool _isStarted;

    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties>? MediaPropertyChanged;
    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo?>? PlaybackStateChanged;
    public event Action<MediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties>? TimelinePropertyChanged;
    public event Action<MediaSession>? SessionClosed;

    private ExternalMediaService() { }

    public void Initialize()
    {
        UpdateStateFromSettings();
    }

    public void UpdateStateFromSettings()
    {
        if (SettingsManager.Current.SystemMediaControlEnabled)
        {
            if (!_isStarted) Start();
        }
        else
        {
            if (_isStarted) Stop();
            
            // Trigger a taskbar update to clear any stuck sessions
            WeakReferenceMessenger.Default.Send(new UpdateTaskbarMessage());
        }
    }

    public void Start()
    {
        if (_isStarted) return;

        try
        {
            MediaManager = new MediaManager();
            MediaManager.OnAnyMediaPropertyChanged += (s, e) => MediaPropertyChanged?.Invoke(s, e);
            MediaManager.OnAnyPlaybackStateChanged += (s, e) => PlaybackStateChanged?.Invoke(s, e);
            MediaManager.OnAnyTimelinePropertyChanged += (s, e) => TimelinePropertyChanged?.Invoke(s, e);
            MediaManager.OnAnySessionClosed += (s) => SessionClosed?.Invoke(s);
            MediaManager.Start();
            _isStarted = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to start MediaManager");
        }
    }

    public void Stop()
    {
        if (!_isStarted || MediaManager == null) return;

        try
        {
            MediaManager.Dispose();
            MediaManager = null;
            _isStarted = false;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Error while disposing MediaManager");
        }
    }

    public MediaSession? GetPreferredSession()
    {
        if (MediaManager == null || !_isStarted) return null;

        bool internalIsPlaying = SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.IsPlaying;
        bool internalHasTrack = SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null;

        // 1. Prioritize Internal Player if it's playing
        if (internalIsPlaying) return null;

        // 2. Fallback to System Media (Spotify, etc.) if enabled
        if (SettingsManager.Current.SystemMediaControlEnabled)
        {
            var focused = MediaManager.GetFocusedSession();
            if (focused != null && !IsInternalSession(focused)) return focused;
        }

        // 3. Last fallback to internal player even if it's not playing
        if (internalHasTrack) return null;

        return null;
    }

    public bool IsInternalSession(MediaSession session)
    {
        string processName = Process.GetCurrentProcess().ProcessName;
        return session.Id.Contains(processName, StringComparison.OrdinalIgnoreCase) || 
               session.Id.Contains("FluentFlyout", StringComparison.OrdinalIgnoreCase) ||
               session.Id.Contains("FluentFlyoutWPF", StringComparison.OrdinalIgnoreCase);
    }

    public async Task PauseOtherSessions(MediaSession currentMediaSession)
    {
        if (MediaManager == null || !SettingsManager.Current.PauseOtherSessionsEnabled) return;

        var tasks = MediaManager.CurrentMediaSessions.Values
            .Where(session => session.Id != currentMediaSession.Id && 
                             session.ControlSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            .Select(session => session.ControlSession.TryPauseAsync().AsTask());

        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        Stop();
    }
}
