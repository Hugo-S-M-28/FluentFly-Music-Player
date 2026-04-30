using FluentFlyout.Classes;
using Microsoft.Toolkit.Uwp.Notifications;
using System.Windows;

namespace FluentFlyoutWPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        // Restore settings as early as possible so that any UI created later has the correct data
        FluentFlyout.Classes.Settings.SettingsManager.RestoreSettings();

        // log unhandled exceptions before crashing
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            NLog.LogManager.GetCurrentClassLogger().Error(args.ExceptionObject as Exception, "Unhandled exception occurred");
            NLog.LogManager.Flush(); // Ensure logs are written before application dies
        };

        // Register toast notification activation
        ToastNotificationManagerCompat.OnActivated += Notifications.HandleNotificationActivation;
        
        // Apply localization before any windows are created
        LocalizationManager.ApplyLocalization();
        
        base.OnStartup(e);
    }
}