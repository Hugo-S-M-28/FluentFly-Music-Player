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
        return new NowPlayingViewModel(Dialog);
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
        public NextUpWindow ShowNextUp(string title, string artist, BitmapImage thumbnail) => throw new NotSupportedException();
        public void ShowSettings(string? navigationPage = null) { }
        public TaskbarWindow ShowTaskbarWindow() => throw new NotSupportedException();
    }
}
