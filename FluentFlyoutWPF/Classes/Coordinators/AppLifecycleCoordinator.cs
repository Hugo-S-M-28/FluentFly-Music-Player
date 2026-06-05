using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Windows.ApplicationModel;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes;
using FluentFlyoutWPF.ViewModels;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;

namespace FluentFlyoutWPF.Classes.Coordinators;

public class AppLifecycleCoordinator : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly Mutex _singletonMutex;
    private readonly CancellationTokenSource _settingsListenerCts = new();
    private bool _isDisposed;

    public AppLifecycleCoordinator()
    {
        _singletonMutex = new Mutex(true, "FluentFlyout", out bool isNewInstance);

        if (!isNewInstance)
        {
            // Signal the existing instance to open settings
            Task.Run(() =>
            {
                try
                {
                    using (EventWaitHandle settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "FluentFlyout_OpenSettings"))
                    {
                        settingsEvent.Set();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to signal existing instance");
                }
            });

            Environment.Exit(0);
        }
    }

    public void StartSettingsSignalListener()
    {
        Task.Run(() =>
        {
            try
            {
                using (EventWaitHandle settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "FluentFlyout_OpenSettings"))
                {
                    WaitHandle[] waitHandles = [settingsEvent, _settingsListenerCts.Token.WaitHandle];
                    while (!_settingsListenerCts.IsCancellationRequested)
                    {
                        int index = WaitHandle.WaitAny(waitHandles, 100);
                        if (index == 0) // settingsEvent
                        {
                            Application.Current.Dispatcher.Invoke(() => App.GetRequiredService<IWindowManager>().ShowSettings());
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown, ignore
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Settings event listener error");
            }
        }, _settingsListenerCts.Token);
    }

    public void ManageWindowsStartup()
    {
        if (SettingsManager.Current.Startup == true)
        {
            try
            {
                RegistryKey? key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                string? executablePath = Environment.ProcessPath;
                if (executablePath != null)
                {
                    key?.SetValue("FluentFlyout", executablePath);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to set startup registry key");
            }
        }
    }

    public void InitializeVersion()
    {
        string previousVersion = SettingsManager.Current.LastKnownVersion;
        try
        {
            var version = Package.Current.Id.Version;
            SettingsManager.Current.LastKnownVersion = $"v{version.Major}.{version.Minor}.{version.Build}";
        }
        catch (InvalidOperationException ex)
        {
            Logger.Warn(ex, "Failed to detect package version (running outside MSIX container)");
            SettingsManager.Current.LastKnownVersion = "debug";
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unexpected error detecting package version");
            SettingsManager.Current.LastKnownVersion = "debug";
        }

        Logger.Info($"Current version: {SettingsManager.Current.LastKnownVersion}");
        Notifications.ShowFirstOrUpdateNotification(previousVersion, SettingsManager.Current.LastKnownVersion);
    }

    public async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result = await UpdateChecker.CheckForUpdatesAsync(SettingsManager.Current.LastKnownVersion);

            if (result.Success)
            {
                UpdateState.Current.IsUpdateAvailable = result.IsUpdateAvailable;
                UpdateState.Current.NewestVersion = result.NewestVersion;
                UpdateState.Current.UpdateUrl = result.UpdateUrl;
                UpdateState.Current.LastUpdateCheck = result.CheckedAt;

                if (result.IsUpdateAvailable)
                {
                    Notifications.ShowUpdateAvailableNotification(result.NewestVersion, result.UpdateUrl);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to check for updates on startup");
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _settingsListenerCts?.Cancel();
            _settingsListenerCts?.Dispose();
            _singletonMutex?.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }
    }
}
