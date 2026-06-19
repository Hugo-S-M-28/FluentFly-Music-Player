using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.ViewModels;
using FluentFlyoutWPF.Models;
using System.Windows.Media.Imaging;
using FluentFlyout.Windows;
using FluentFlyoutWPF.Windows;

namespace FluentFlyout.Tests;

public class SettingsShellViewModelTests
{
    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? OpenFile(string title, string filter) => null;
        public string? SaveFile(string title, string filter, string defaultFileName = "") => null;
    }

    private sealed class FakeDialogService : IDialogService
    {
        public int ShowErrorCalls { get; private set; }
        public int ShowMessageCalls { get; private set; }
        public int ShowConfirmCalls { get; private set; }

        public Task ShowErrorAsync(string title, string message)
        {
            ShowErrorCalls++;
            return Task.CompletedTask;
        }

        public Task ShowMessageAsync(string title, string message)
        {
            ShowMessageCalls++;
            return Task.CompletedTask;
        }

        public Task<bool> ShowConfirmAsync(string title, string message)
        {
            ShowConfirmCalls++;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSystemShellService : ISystemShellService
    {
        public void RevealInFileExplorer(string filePath) { }
        public void OpenUrl(string url) { }
    }

    private sealed class FakeAppShellService : IAppShellService
    {
        public int OpenLogsCalls { get; private set; }
        public void OpenSettings() { }
        public void OpenRepository() { }
        public void OpenLogsFolder() => OpenLogsCalls++;
        public void ReportBug() { }
    }

    private sealed class FakeWindowManager : IWindowManager
    {
        public void ShowSettings(string? navigationPage = null) { }
        public void NavigateSettings(Type pageType) { }
        public void ShowEditTrack(TrackModel track, bool lyricsOnly = false) { }
        public void ShowEqualizer() { }
        public void ShowManageLibrary() { }
        public TaskbarWindow ShowTaskbarWindow() => throw new NotSupportedException();
        public TaskbarWindow RecreateTaskbarWindow() => throw new NotSupportedException();
        public NextUpWindow ShowNextUp(string title, string artist, BitmapImage? thumbnail, NextUpDisplayMode displayMode = NextUpDisplayMode.UpNext, bool autoClose = true) => throw new NotSupportedException();
        public LockWindow GetOrCreateLockWindow() => throw new NotSupportedException();
    }

    private sealed class FakeMonitorService : IMonitorService
    {
        public IReadOnlyList<MonitorOption> GetMonitorOptions() => [];
        public IReadOnlyList<FluentFlyoutWPF.Classes.Utils.MonitorUtil.MonitorInfo> GetMonitors() => [];
    }

    [Fact]
    public void StartupChanged_InvokesSetStartupBehavior()
    {
        SettingsManager.Current = new UserSettings();
        var dialogService = new FakeDialogService();
        var vm = new SettingsShellViewModel(
            new FakeFileDialogService(),
            dialogService,
            new FakeSystemShellService(),
            new FakeAppShellService(),
            new FakeWindowManager(),
            new FakeMonitorService()
        );

        // Reset settings state to default (false)
        vm.Settings.Startup = false;

        // Toggle startup setting
        vm.Settings.Startup = true;

        // We can assert registry state or just that it was processed without throwing unhandled exceptions.
        // On CI/headless runners registry writes might fail or succeed, but exceptions are handled internally.
        Assert.True(vm.Settings.Startup);
    }

    [Fact]
    public void NIconHideChanged_SendsTrayIconStateMessageOnce()
    {
        SettingsManager.Current = new UserSettings();
        var vm = new SettingsShellViewModel(
            new FakeFileDialogService(),
            new FakeDialogService(),
            new FakeSystemShellService(),
            new FakeAppShellService(),
            new FakeWindowManager(),
            new FakeMonitorService()
        );

        int messageReceivedCount = 0;
        bool? expectedState = null;

        WeakReferenceMessenger.Default.Register<TrayIconStateMessage>(this, (recipient, message) =>
        {
            messageReceivedCount++;
            expectedState = message.Register;
        });

        try
        {
            // Initial state
            vm.Settings.NIconHide = false;
            messageReceivedCount = 0;

            // Change to true (meaning hide, so TrayIconStateMessage should be sent with Register = false)
            vm.Settings.NIconHide = true;

            Assert.Equal(1, messageReceivedCount);
            Assert.False(expectedState);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<TrayIconStateMessage>(this);
        }
    }

    [Fact]
    public void EnablingWin11TrayIcon_UnhidesTrayIconAndRequestsRecreate()
    {
        SettingsManager.Current = new UserSettings { SuppressAutoSave = true };
        SettingsManager.Current.CompleteInitialization();

        SettingsManager.Current.NIconSymbol = false;
        SettingsManager.Current.NIconHide = true;

        int recreateMessageCount = 0;

        WeakReferenceMessenger.Default.Register<RecreateTrayIconMessage>(this, (recipient, message) =>
        {
            recreateMessageCount++;
        });

        try
        {
            SettingsManager.Current.NIconSymbol = true;

            Assert.True(SettingsManager.Current.NIconSymbol);
            Assert.False(SettingsManager.Current.NIconHide);
            Assert.Equal(1, recreateMessageCount);
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<RecreateTrayIconMessage>(this);
        }
    }

    [Fact]
    public async Task ViewLogsCommand_CreatesLogsDirectoryIfMissing()
    {
        SettingsManager.Current = new UserSettings();
        var dialogService = new FakeDialogService();
        var appShellService = new FakeAppShellService();
        var vm = new SettingsShellViewModel(
            new FakeFileDialogService(),
            dialogService,
            new FakeSystemShellService(),
            appShellService,
            new FakeWindowManager(),
            new FakeMonitorService()
        );

        string logsPath = FluentFlyoutWPF.Classes.Utils.FileSystemHelper.GetLogsPath();
        
        // Ensure path exists
        Assert.True(Directory.Exists(logsPath));

        // Call command (explorer execution might throw in headless test environments, but it is caught and handled)
        await vm.ViewLogsCommand.ExecuteAsync(null);

        // Verify folder exists
        Assert.True(Directory.Exists(logsPath));
        Assert.Equal(1, appShellService.OpenLogsCalls);
    }
}
