using FluentFlyout.Windows;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Windows;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.ViewModels;

internal static class DesignTimeViewModelFactory
{
    private static readonly IFileDialogService FileDialog = new DesignTimeFileDialogService();
    private static readonly IDialogService Dialog = new DesignTimeDialogService();
    private static readonly ISystemShellService Shell = new DesignTimeSystemShellService();
    private static readonly IAppShellService AppShell = new DesignTimeAppShellService();
    private static readonly IWindowManager WindowManager = new DesignTimeWindowManager();
    private static readonly IWindowService WindowService = new WindowService(WindowManager);
    private static readonly IMonitorService MonitorService = new MonitorService();
    private static readonly ILibraryService LibraryService = new LibraryService();
    private static readonly IPlaybackService PlaybackService = new PlaybackService();
    private static readonly IExternalMediaService ExternalMediaService = new DesignTimeExternalMediaService();
    private static readonly ISettingsService SettingsService = new DesignTimeSettingsService();
    private static readonly IPlaybackSourceResolver PlaybackSourceResolver = new PlaybackSourceResolverService(SettingsService, PlaybackService, ExternalMediaService);

    public static SettingsShellViewModel CreateSettingsShellViewModel()
    {
        return new SettingsShellViewModel(FileDialog, Dialog, Shell, AppShell, WindowManager, MonitorService);
    }

    public static LibraryViewModel CreateLibraryViewModel()
    {
        return new LibraryViewModel(LibraryService, PlaybackService, WindowService, Shell, FileDialog, Dialog);
    }

    public static PlaylistViewModel CreatePlaylistViewModel()
    {
        return new PlaylistViewModel(PlaybackService, LibraryService, FileDialog, Dialog, Shell, WindowService);
    }

    public static NowPlayingViewModel CreateNowPlayingViewModel()
    {
        return new NowPlayingViewModel(Dialog, PlaybackService, ExternalMediaService, SettingsService, LibraryService, WindowService, PlaybackSourceResolver);
    }

    private sealed class DesignTimeDialogService : IDialogService
    {
        public Task ShowErrorAsync(string title, string message) => Task.CompletedTask;
        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;
        public Task<bool> ShowConfirmAsync(string title, string message) => Task.FromResult(false);
    }

    private sealed class DesignTimeFileDialogService : IFileDialogService
    {
        public string? OpenFile(string title, string filter) => null;
        public string? SaveFile(string title, string filter, string defaultFileName = "") => null;
    }

    private sealed class DesignTimeSystemShellService : ISystemShellService
    {
        public void OpenUrl(string url) { }
        public void RevealInFileExplorer(string filePath) { }
    }

    private sealed class DesignTimeAppShellService : IAppShellService
    {
        public void OpenLogsFolder() { }
        public void OpenRepository() { }
        public void OpenSettings() { }
        public void ReportBug() { }
    }

    private sealed class DesignTimeWindowManager : IWindowManager
    {
        public LockWindow GetOrCreateLockWindow() => throw new NotSupportedException();
        public void NavigateSettings(Type pageType) { }
        public TaskbarWindow RecreateTaskbarWindow() => throw new NotSupportedException();
        public void ShowEditTrack(TrackModel track, bool lyricsOnly = false) { }
        public void ShowEqualizer() { }
        public void ShowManageLibrary() { }
        public NextUpWindow ShowNextUp(string title, string artist, System.Windows.Media.Imaging.BitmapImage? thumbnail, NextUpDisplayMode displayMode = NextUpDisplayMode.UpNext, bool autoClose = true) => throw new NotSupportedException();
        public void ShowSettings(string? navigationPage = null) { }
        public TaskbarWindow ShowTaskbarWindow() => throw new NotSupportedException();
    }

    private sealed class DesignTimeExternalMediaService : IExternalMediaService
    {
        public global::WindowsMediaController.MediaManager? MediaManager => null;

        private Action<global::WindowsMediaController.MediaManager.MediaSession, global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties>? _mediaPropertyChanged;
        public event Action<global::WindowsMediaController.MediaManager.MediaSession, global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties>? MediaPropertyChanged
        {
            add { _mediaPropertyChanged += value; }
            remove { _mediaPropertyChanged -= value; }
        }

        private Action<global::WindowsMediaController.MediaManager.MediaSession, global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackInfo?>? _playbackStateChanged;
        public event Action<global::WindowsMediaController.MediaManager.MediaSession, global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackInfo?>? PlaybackStateChanged
        {
            add { _playbackStateChanged += value; }
            remove { _playbackStateChanged -= value; }
        }

        private Action<global::WindowsMediaController.MediaManager.MediaSession, global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionTimelineProperties>? _timelinePropertyChanged;
        public event Action<global::WindowsMediaController.MediaManager.MediaSession, global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionTimelineProperties>? TimelinePropertyChanged
        {
            add { _timelinePropertyChanged += value; }
            remove { _timelinePropertyChanged -= value; }
        }

        private Action<global::WindowsMediaController.MediaManager.MediaSession>? _sessionClosed;
        public event Action<global::WindowsMediaController.MediaManager.MediaSession>? SessionClosed
        {
            add { _sessionClosed += value; }
            remove { _sessionClosed -= value; }
        }

        public void Initialize() { }
        public void UpdateStateFromSettings() { }
        public void Start() { }
        public void Stop() { }
        public global::WindowsMediaController.MediaManager.MediaSession? GetPreferredSession() => null;
        public bool IsInternalSession(global::WindowsMediaController.MediaManager.MediaSession session) => false;
        public bool IsSessionPlaying(global::WindowsMediaController.MediaManager.MediaSession? session) => false;
        public Task PauseOtherSessions(global::WindowsMediaController.MediaManager.MediaSession currentMediaSession) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class DesignTimeSettingsService : ISettingsService
    {
        public UserSettings Current { get; } = new UserSettings();
        public void Save() { }
    }
}
