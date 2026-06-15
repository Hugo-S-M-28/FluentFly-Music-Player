using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Pages;
using Microsoft.Win32;
using NLog;

namespace FluentFlyoutWPF.ViewModels;

public partial class SettingsShellViewModel : ObservableObject
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly IFileDialogService _fileDialogService;
    private readonly IDialogService _dialogService;
    private readonly ISystemShellService _systemShellService;
    private readonly IAppShellService _appShellService;
    private readonly IWindowManager _windowManager;

    public UserSettings Settings => SettingsManager.Current;
    public ObservableCollection<Models.MonitorOption> MonitorOptions { get; }

    public SettingsShellViewModel(
        IFileDialogService fileDialogService,
        IDialogService dialogService,
        ISystemShellService systemShellService,
        IAppShellService appShellService,
        IWindowManager windowManager,
        IMonitorService monitorService)
    {
        _fileDialogService = fileDialogService;
        _dialogService = dialogService;
        _systemShellService = systemShellService;
        _appShellService = appShellService;
        _windowManager = windowManager;
        MonitorOptions = new ObservableCollection<Models.MonitorOption>(monitorService.GetMonitorOptions());

        Settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(UserSettings.IsStoreVersion)) OnPropertyChanged(nameof(IsStoreVersion));
            if (e.PropertyName == nameof(UserSettings.IsPremiumUnlocked)) OnPropertyChanged(nameof(IsPremiumUnlocked));
            if (e.PropertyName == nameof(UserSettings.PremiumPrice)) OnPropertyChanged(nameof(PremiumPrice));
            if (e.PropertyName == nameof(UserSettings.TaskbarVisualizerPosition)) OnPropertyChanged(nameof(TaskbarVisualizerPosition));
        };
    }

    public bool IsStoreVersion => Settings.IsStoreVersion;
    public bool IsPremiumUnlocked => Settings.IsPremiumUnlocked;
    public string PremiumPrice => Settings.PremiumPrice;

    public int TaskbarVisualizerPosition
    {
        get => Settings.TaskbarVisualizerPosition;
        set
        {
            if (Settings.TaskbarVisualizerPosition != value)
            {
                Settings.TaskbarVisualizerPosition = value;
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    public async Task ExportSettingsAsync()
    {
        var filePath = _fileDialogService.SaveFile(
            LocalizationManager.GetString("ExportSettings"),
            LocalizationManager.GetString("Settings_LoadConfigFilter"),
            $"FluentFlyout_Settings_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.xml"
        );

        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                SettingsManager.SaveSettings(filePath);

                var successTitle = LocalizationManager.GetString("ExportSuccessful");
                var successContent = LocalizationManager.GetString("SettingsExportedSuccessfully");
                await _dialogService.ShowMessageAsync(successTitle, successContent);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error exporting settings");

                var failedTitle = LocalizationManager.GetString("ExportFailed");
                var failedContent = LocalizationManager.GetString("FailedToExportSettings");
                await _dialogService.ShowErrorAsync(failedTitle, failedContent);
            }
        }
    }

    [RelayCommand]
    public async Task ImportSettingsAsync()
    {
        var filePath = _fileDialogService.OpenFile(
            LocalizationManager.GetString("Settings_LoadConfigTitle"),
            LocalizationManager.GetString("Settings_LoadConfigFilter")
        );

        if (!string.IsNullOrEmpty(filePath))
        {
            var confirmTitle = LocalizationManager.GetString("ImportSettings");
            var confirmContent = LocalizationManager.GetString("ImportSettingsWarning");
            var confirmed = await _dialogService.ShowConfirmAsync(confirmTitle, confirmContent);

            if (confirmed)
            {
                try
                {
                    SettingsManager.RestoreSettings(filePath);
                    SettingsManager.SaveSettings();

                    var successTitle = LocalizationManager.GetString("ImportSuccessful");
                    var successContent = LocalizationManager.GetString("SettingsImportedSuccessfully");
                    await _dialogService.ShowMessageAsync(successTitle, successContent);

                    Application.Current.Shutdown();
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (exePath != null) System.Diagnostics.Process.Start(exePath);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error importing settings");

                    var failedTitle = LocalizationManager.GetString("ImportFailed");
                    var failedContent = LocalizationManager.GetString("FailedToImportSettings");
                    await _dialogService.ShowErrorAsync(failedTitle, failedContent);
                }
            }
        }
    }

    [ObservableProperty]
    private bool isUnlockingPremium;

    [RelayCommand]
    public void NavigateUrl(string url)
    {
        _systemShellService.OpenUrl(url);
    }

    [RelayCommand]
    public async Task UnlockPremiumAsync()
    {
        try
        {
            IsUnlockingPremium = true;

            (bool success, string result) = await FluentFlyout.Classes.LicenseManager.Instance.PurchasePremiumAsync();

            if (success)
            {
                SettingsManager.Current.IsPremiumUnlocked = true;

                var title = LocalizationManager.GetString("License_Success");
                var content = LocalizationManager.GetString("PremiumPurchaseSuccess");
                await _dialogService.ShowMessageAsync(title, content);
            }
            else
            {
                var title = LocalizationManager.GetString("License_PurchaseFailed");
                var content = $"{LocalizationManager.GetString("PremiumPurchaseFailed")} ({result})";
                await _dialogService.ShowErrorAsync(title, content);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error unlocking premium");
            await _dialogService.ShowErrorAsync(
                LocalizationManager.GetString("License_Error"),
                string.Format(LocalizationManager.GetString("General_UnexpectedError"), ex.Message));
        }
        finally
        {
            IsUnlockingPremium = false;
        }
    }

    [RelayCommand]
    public void SetStartup(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key == null) return;
            const string appName = "FluentFlyout";
            var executablePath = Environment.ProcessPath;

            if (enable)
            {
                if (File.Exists(executablePath))
                {
                    key.SetValue(appName, executablePath);
                }
                else
                {
                    throw new FileNotFoundException("Application executable not found");
                }
            }
            else
            {
                if (key.GetValue(appName) != null)
                {
                    key.DeleteValue(appName, false);
                }
            }
        }
        catch (Exception ex)
        {
            var title = LocalizationManager.GetString("Edit_ErrorTitle");
            var message = $"{LocalizationManager.GetString("Error_FailedSetStartup")}: {ex.Message}";
            _ = _dialogService.ShowErrorAsync(title, message);
        }
    }

    [RelayCommand]
    public void SetNIconHide(bool isChecked)
    {
        WeakReferenceMessenger.Default.Send(new TrayIconStateMessage(!isChecked));
    }

    [RelayCommand]
    public void ResetLibraryLayout()
    {
        Settings.LibraryGridItemSize = 160.0;
        Settings.LibraryTrackIconSize = 40.0;
        Settings.LibraryItemCornerRadius = 12.0;
    }

    [RelayCommand]
    public void ViewLogs()
    {
        _appShellService.OpenLogsFolder();
    }

    [RelayCommand]
    public void NavigateToAdvanced()
    {
        _windowManager.NavigateSettings(typeof(AdvancedPage));
    }
}
