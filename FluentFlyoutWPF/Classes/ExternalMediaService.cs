using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Messages;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

using FluentFlyoutWPF.Classes.Services;

namespace FluentFlyoutWPF.Classes;

public class ExternalMediaService : IExternalMediaService
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static ExternalMediaService? _instance;
    private static readonly object _instanceLock = new();
    private static readonly string _ownProcessName = Process.GetCurrentProcess().ProcessName;
    private static readonly string[] _ownSessionKeywords =
    [
        _ownProcessName,
        "FluentFlyout",
        "FluentFlyoutWPF",
        "FluentFlyoutMSIX",
        "FluentFlyoutAuthors.FluentFlyout"
    ];

    private string? _lastPreferredSessionId;

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
            if (!_isStarted)
            {
                Start();
            }
        }
        else
        {
            if (_isStarted)
            {
                Stop();
            }

            WeakReferenceMessenger.Default.Send(new UpdateTaskbarMessage());
        }
    }

    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        try
        {
            MediaManager = new MediaManager();
            MediaManager.OnAnyMediaPropertyChanged += (s, e) => MediaPropertyChanged?.Invoke(s, e);
            MediaManager.OnAnyPlaybackStateChanged += (s, e) => PlaybackStateChanged?.Invoke(s, e);
            MediaManager.OnAnyTimelinePropertyChanged += (s, e) => TimelinePropertyChanged?.Invoke(s, e);
            MediaManager.OnAnySessionClosed += s => SessionClosed?.Invoke(s);
            MediaManager.Start();
            _isStarted = true;
            Logger.Info("External media manager started");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to start MediaManager");
        }
    }

    public void Stop()
    {
        if (!_isStarted || MediaManager == null)
        {
            return;
        }

        try
        {
            MediaManager.Dispose();
            MediaManager = null;
            _isStarted = false;
            _lastPreferredSessionId = null;
            Logger.Info("External media manager stopped");
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Error while disposing MediaManager");
        }
    }

    public MediaSession? GetPreferredSession()
    {
        if (MediaManager == null || !_isStarted)
        {
            return null;
        }

        if (!SettingsManager.Current.SystemMediaControlEnabled)
        {
            return null;
        }

        IReadOnlyList<MediaSession> candidates = GetExternalSessions();
        if (candidates.Count == 0)
        {
            Logger.Debug("GetPreferredSession: no external session candidates available.");
            TrackPreferredSession(null);
            return null;
        }

        MediaSession? focusedSession = null;
        try
        {
            focusedSession = MediaManager.GetFocusedSession();
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to read focused session");
        }

        MediaSession? bestSession = null;
        int bestScore = int.MinValue;

        foreach (MediaSession session in candidates)
        {
            var playbackInfo = session.ControlSession.GetPlaybackInfo();
            bool isFocused = focusedSession != null && string.Equals(session.Id, focusedSession.Id, StringComparison.OrdinalIgnoreCase);
            int score = CalculateSessionScore(
                session.Id,
                isFocused,
                playbackInfo?.PlaybackStatus ?? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed,
                playbackInfo?.Controls?.IsPauseEnabled ?? false,
                playbackInfo?.Controls?.IsPlayEnabled ?? false,
                playbackInfo?.Controls?.IsNextEnabled ?? false,
                playbackInfo?.Controls?.IsPreviousEnabled ?? false);

            Logger.Debug(
                "External session candidate: {SessionId}; Score={Score}; Focused={Focused}; Status={Status}; Controls=[Play:{Play}, Pause:{Pause}, Next:{Next}, Previous:{Previous}]",
                session.Id,
                score,
                isFocused,
                playbackInfo?.PlaybackStatus,
                playbackInfo?.Controls?.IsPlayEnabled ?? false,
                playbackInfo?.Controls?.IsPauseEnabled ?? false,
                playbackInfo?.Controls?.IsNextEnabled ?? false,
                playbackInfo?.Controls?.IsPreviousEnabled ?? false);

            if (score > bestScore)
            {
                bestScore = score;
                bestSession = session;
            }
        }

        TrackPreferredSession(bestSession);
        return bestSession;
    }

    public bool IsInternalSession(MediaSession session)
    {
        return IsOwnSessionId(session.Id);
    }

    public bool IsSessionPlaying(MediaSession? session)
    {
        if (session == null)
        {
            return false;
        }

        try
        {
            return session.ControlSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Failed to inspect external session playback status for {SessionId}", session.Id);
            return false;
        }
    }

    public async Task PauseOtherSessions(MediaSession currentMediaSession)
    {
        if (MediaManager == null || !SettingsManager.Current.PauseOtherSessionsEnabled)
        {
            return;
        }

        var tasks = MediaManager.CurrentMediaSessions.Values
            .Where(session => session.Id != currentMediaSession.Id &&
                              !IsInternalSession(session) &&
                              session.ControlSession.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            .Select(session => session.ControlSession.TryPauseAsync().AsTask());

        await Task.WhenAll(tasks);
    }

    internal IReadOnlyList<MediaSession> GetExternalSessions()
    {
        if (MediaManager == null || !_isStarted)
        {
            return Array.Empty<MediaSession>();
        }

        return MediaManager.CurrentMediaSessions.Values
            .Where(session => !IsInternalSession(session))
            .ToList();
    }

    internal static bool IsOwnSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        return _ownSessionKeywords.Any(keyword => sessionId.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    internal static int CalculateSessionScore(
        string sessionId,
        bool isFocused,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus playbackStatus,
        bool canPause,
        bool canPlay,
        bool canNext,
        bool canPrevious)
    {
        int score = playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? 1000 : 0;

        if (canPause)
        {
            score += 20;
        }

        if (canPlay)
        {
            score += 20;
        }

        if (canNext)
        {
            score += 10;
        }

        if (canPrevious)
        {
            score += 10;
        }

        if (isFocused)
        {
            score += 100;
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            score -= 50;
        }

        return score;
    }

    private void TrackPreferredSession(MediaSession? session)
    {
        string? sessionId = session?.Id;
        if (string.Equals(_lastPreferredSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastPreferredSessionId = sessionId;

        if (sessionId == null)
        {
            Logger.Info("Active external session changed: none");
        }
        else
        {
            Logger.Info("Active external session changed: {SessionId}", sessionId);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
