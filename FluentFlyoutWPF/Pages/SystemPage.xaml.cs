using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Utils;
using Microsoft.Win32;
using NLog;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
using MessageBox = Wpf.Ui.Controls.MessageBox;

namespace FluentFlyoutWPF.Pages;

public partial class SystemPage : Page
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public SystemPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
        UpdateMonitorList();
    }

    private void StartupSwitch_Click(object sender, RoutedEventArgs e)
    {
        SetStartup(StartupSwitch.IsChecked ?? false);
    }

    private void SetStartup(bool enable)
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
            MessageBox messageBox = new()
            {
                Title = Application.Current.FindResource("Edit_ErrorTitle")?.ToString() ?? "Error",
                Content = $"{Application.Current.FindResource("Error_FailedSetStartup") ?? "Failed to set startup"}: {ex.Message}",
                CloseButtonText = Application.Current.FindResource("General_Ok")?.ToString() ?? "OK",
            };

            _ = messageBox.ShowDialogAsync();
        }
    }

    private void StartupHyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void ToggleSwitch_Click(object sender, RoutedEventArgs e)
    {
        bool isChecked = NIconHideSwitch.IsChecked == true;
        WeakReferenceMessenger.Default.Send(new TrayIconStateMessage(!isChecked));
    }

    private void UpdateMonitorList()
    {
        MonitorUtil.UpdateMonitorList(
            FlyoutSelectedMonitorComboBox,
            () => SettingsManager.Current.FlyoutSelectedMonitor,
            value => SettingsManager.Current.FlyoutSelectedMonitor = value);
    }

    private void UnlockPremiumButton_Click(object sender, RoutedEventArgs e)
    {
        FluentFlyout.Classes.LicenseManager.UnlockPremium(sender);
    }


    private async void ExportButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var saveFileDialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"FluentFlyout_Settings_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}",
            DefaultExt = ".xml",
            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                SettingsManager.SaveSettings(saveFileDialog.FileName);

                Wpf.Ui.Controls.MessageBox messageBox = new()
                {
                    Title = Application.Current.FindResource("ExportSuccessful")?.ToString() ?? "Success",
                    Content = Application.Current.FindResource("SettingsExportedSuccessfully")?.ToString() ?? "Settings exported.",
                    CloseButtonText = Application.Current.FindResource("General_Ok")?.ToString() ?? "OK",
                };

                _ = messageBox.ShowDialogAsync();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error exporting settings");

                Wpf.Ui.Controls.MessageBox messageBox = new()
                {
                    Title = Application.Current.FindResource("ExportFailed")?.ToString() ?? "Error",
                    Content = Application.Current.FindResource("FailedToExportSettings")?.ToString() ?? "Failed to export.",
                    CloseButtonText = Application.Current.FindResource("General_Ok")?.ToString() ?? "OK",
                };

                _ = messageBox.ShowDialogAsync();
            }
        }
    }

    private async void ImportButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            DefaultExt = ".xml",
            Filter = "XML Files (*.xml)|*.xml|All Files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            Wpf.Ui.Controls.MessageBox confirmBox = new()
            {
                Title = Application.Current.FindResource("ImportSettings")?.ToString() ?? "Import",
                Content = Application.Current.FindResource("ImportSettingsWarning")?.ToString() ?? "This will overwrite current settings.",
                CloseButtonText = Application.Current.FindResource("General_No")?.ToString() ?? "No",
                SecondaryButtonText = Application.Current.FindResource("General_Yes")?.ToString() ?? "Yes",
            };

            var result = await confirmBox.ShowDialogAsync();

            if (result == Wpf.Ui.Controls.MessageBoxResult.Secondary)
            {
                try
                {
                    SettingsManager.RestoreSettings(openFileDialog.FileName);
                    SettingsManager.SaveSettings();

                    Wpf.Ui.Controls.MessageBox messageBox = new()
                    {
                        Title = Application.Current.FindResource("ImportSuccessful")?.ToString() ?? "Success",
                        Content = Application.Current.FindResource("SettingsImportedSuccessfully")?.ToString() ?? "Settings imported.",
                        CloseButtonText = Application.Current.FindResource("General_Ok")?.ToString() ?? "OK",
                    };

                    _ = messageBox.ShowDialogAsync();

                    // Restart the application
                    Application.Current.Shutdown();
                    var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                    if (exePath != null) System.Diagnostics.Process.Start(exePath);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error importing settings");

                    Wpf.Ui.Controls.MessageBox messageBox = new()
                    {
                        Title = Application.Current.FindResource("ImportFailed")?.ToString() ?? "Error",
                        Content = Application.Current.FindResource("FailedToImportSettings")?.ToString() ?? "Failed to import.",
                        CloseButtonText = Application.Current.FindResource("General_Ok")?.ToString() ?? "OK",
                    };

                    _ = messageBox.ShowDialogAsync();
                }
            }
        }
    }

    private void Advanced_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SettingsWindow.NavigateToPage(typeof(AdvancedPage));
    }

    private void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        Classes.TrayIconService.Instance.OpenLogsFolder();
    }
}
