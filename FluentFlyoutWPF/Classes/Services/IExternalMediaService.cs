using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using WindowsMediaController;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF.Classes.Services;

public interface IExternalMediaService : IDisposable
{
    MediaManager? MediaManager { get; }

    event Action<MediaSession, GlobalSystemMediaTransportControlsSessionMediaProperties>? MediaPropertyChanged;
    event Action<MediaSession, GlobalSystemMediaTransportControlsSessionPlaybackInfo?>? PlaybackStateChanged;
    event Action<MediaSession, GlobalSystemMediaTransportControlsSessionTimelineProperties>? TimelinePropertyChanged;
    event Action<MediaSession>? SessionClosed;

    void Initialize();
    void UpdateStateFromSettings();
    void Start();
    void Stop();
    MediaSession? GetPreferredSession();
    bool IsInternalSession(MediaSession session);
    bool IsSessionPlaying(MediaSession? session);
    Task PauseOtherSessions(MediaSession currentMediaSession);
}
