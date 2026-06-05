using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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

    // Delegates/Wrappers for direct bindings on SettingsShellViewModel
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
            "Guardar Configuración",
            "XML Files (*.xml)|*.xml|All Files (*.*)|*.*",
            $"FluentFlyout_Settings_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.xml"
        );

        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                SettingsManager.SaveSettings(filePath);

                var successTitle = Application.Current.FindResource("ExportSuccessful")?.ToString() ?? "Success";
                var successContent = Application.Current.FindResource("SettingsExportedSuccessfully")?.ToString() ?? "Settings exported.";
                await _dialogService.ShowMessageAsync(successTitle, successContent);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error exporting settings");

                var failedTitle = Application.Current.FindResource("ExportFailed")?.ToString() ?? "Error";
                var failedContent = Application.Current.FindResource("FailedToExportSettings")?.ToString() ?? "Failed to export.";
                await _dialogService.ShowErrorAsync(failedTitle, failedContent);
            }
        }
    }

    [RelayCommand]
    public async Task ImportSettingsAsync()
    {
        var filePath = _fileDialogService.OpenFile(
            "Cargar Configuración",
            "XML Files (*.xml)|*.xml|All Files (*.*)|*.*"
        );

        if (!string.IsNullOrEmpty(filePath))
        {
            var confirmTitle = Application.Current.FindResource("ImportSettings")?.ToString() ?? "Import";
            var confirmContent = Application.Current.FindResource("ImportSettingsWarning")?.ToString() ?? "This will overwrite current settings.";
            var confirmed = await _dialogService.ShowConfirmAsync(confirmTitle, confirmContent);

            if (confirmed)
            {
                try
                {
                    SettingsManager.RestoreSettings(filePath);
                    SettingsManager.SaveSettings();

                    var successTitle = Application.Current.FindResource("ImportSuccessful")?.ToString() ?? "Success";
                    var successContent = Application.Current.FindResource("SettingsImportedSuccessfully")?.ToString() ?? "Settings imported.";
                    await _dialogService.ShowMessageAsync(successTitle, successContent);

                    // Restart the application
                    Application.Current.Shutdown();
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (exePath != null) System.Diagnostics.Process.Start(exePath);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error importing settings");

                    var failedTitle = Application.Current.FindResource("ImportFailed")?.ToString() ?? "Error";
                    var failedContent = Application.Current.FindResource("FailedToImportSettings")?.ToString() ?? "Failed to import.";
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
                
                var title = "Success";
                var content = Application.Current.TryFindResource("PremiumPurchaseSuccess")?.ToString() ?? "Premium features successfully unlocked!";
                await _dialogService.ShowMessageAsync(title, content);
            }
            else
            {
                var title = "Purchase Failed";
                var content = $"{Application.Current.TryFindResource("PremiumPurchaseFailed")?.ToString() ?? "Purchase failed."} ({result})";
                await _dialogService.ShowErrorAsync(title, content);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error unlocking premium");
            await _dialogService.ShowErrorAsync("Error", $"An error occurred: {ex.Message}");
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
            var title = Application.Current.FindResource("Edit_ErrorTitle")?.ToString() ?? "Error";
            var message = $"{(Application.Current.FindResource("Error_FailedSetStartup") ?? "Failed to set startup")}: {ex.Message}";
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
