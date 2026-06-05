using FluentFlyout.Classes;
using FluentFlyout.Windows;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using FluentFlyoutWPF.Windows;
using FluentFlyoutWPF.Pages;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Windows;

namespace FluentFlyoutWPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static T GetRequiredService<T>() where T : class
    {
        return Services.GetRequiredService<T>();
    }

    protected override void OnStartup(StartupEventArgs e)
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

        Services = ConfigureServices();
        ConfigureLegacyServiceLocator();
        
        base.OnStartup(e);

        AccentResourceHelper.RefreshAccentResources();

        WeakReferenceMessenger.Default.Register<UpdateAccentColorMessage>(this, static (recipient, _) =>
        {
            if (recipient is App app)
            {
                app.Dispatcher.InvokeAsync(AccentResourceHelper.RefreshAccentResources);
            }
        });

        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, static (recipient, _) =>
        {
            if (recipient is App app)
            {
                app.Dispatcher.InvokeAsync(AccentResourceHelper.RefreshAccentResources);
            }
        });

        MainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IFolderPickerService, FolderPickerService>();
        services.AddSingleton<ISystemShellService, SystemShellService>();
        services.AddSingleton<IMonitorService, MonitorService>();
        services.AddSingleton<ILibraryService, LibraryService>();
        services.AddSingleton<IPlaybackService, PlaybackService>();
        services.AddSingleton<IWindowManager, WindowManager>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IAppShellService, AppShellService>();

        services.AddSingleton<SettingsShellViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddSingleton<NowPlayingViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<EqualizerViewModel>();
        services.AddTransient<ManageLibraryViewModel>();

        services.AddSingleton<MainWindow>();
        services.AddTransient<SettingsWindow>();
        services.AddTransient<ManageLibraryWindow>();
        services.AddTransient<TaskbarWindow>();
        services.AddTransient<EqualizerWindow>();
        services.AddTransient<LockWindow>();
        services.AddTransient<HomePage>();

        return services.BuildServiceProvider();
    }

    private static void ConfigureLegacyServiceLocator()
    {
        ServiceLocator.Dialog = Services.GetRequiredService<IDialogService>();
        ServiceLocator.FileDialog = Services.GetRequiredService<IFileDialogService>();
        ServiceLocator.Windows = Services.GetRequiredService<IWindowService>();
        ServiceLocator.Shell = Services.GetRequiredService<ISystemShellService>();
        ServiceLocator.AppShell = Services.GetRequiredService<IAppShellService>();
    }
}
