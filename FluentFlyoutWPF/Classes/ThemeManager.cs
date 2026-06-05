using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Helpers;
using MicaWPF.Core.Services;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Appearance;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;

namespace FluentFlyout.Classes;

/// <summary>
/// Manages the application theme settings and applies the selected theme.
/// </summary>
internal static class ThemeManager
{
    static ThemeManager()
    {
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
    }

    private static void OnApplicationThemeChanged(ApplicationTheme currentApplicationTheme, System.Windows.Media.Color systemAccent)
    {
        SyncThemeResources(currentApplicationTheme);

        // 1. Sync MicaWPF theme
        var micaTheme = currentApplicationTheme switch
        {
            ApplicationTheme.Light => WindowsTheme.Light,
            ApplicationTheme.Dark => WindowsTheme.Dark,
            _ => WindowsTheme.Auto
        };
        MicaWPFServiceUtility.ThemeService.ChangeTheme(micaTheme);

        // 2. Refresh accent color to its counterpart after theme changes
        MicaWPFServiceUtility.AccentColorService.RefreshAccentsColors();

        // 3. Refresh window backdrops (Acrylic/Mica tints)
        WindowBlurHelper.RefreshAllWindowBackdrops();

        // 4. Update taskbar widget theme
        UpdateTaskbarWidget();

        // 5. Update tray icon
        UpdateTrayIcon();

        // 6. Notify all subscribers that the theme has changed so they can
        //    refresh any hardcoded/cached brushes (e.g. ShuffleForeground).
        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(currentApplicationTheme));

    }

    private static void SyncThemeResources(ApplicationTheme theme)
    {
        if (Application.Current == null)
            return;

        ApplyThemeToDictionary(Application.Current.Resources, theme);

        foreach (Window window in Application.Current.Windows)
        {
            ApplyThemeToDictionary(window.Resources, theme);
            ApplyThemeToTree(window, theme);
        }

        // Sincronizar colores del tocadiscos dinámicamente según el tema
        var tonearmColor = theme == ApplicationTheme.Dark ? "#D7D7D7" : "#4A4A4A";
        var needleColor = theme == ApplicationTheme.Dark ? "#4A4A4A" : "#8C8C8C";
        var overlayColor = theme == ApplicationTheme.Dark ? "#19000000" : "#0D000000";

        var converter = new System.Windows.Media.BrushConverter();
        if (converter.ConvertFromString(tonearmColor) is System.Windows.Media.Brush tonearmBrush)
            Application.Current.Resources["PlayerTurntableTonearmBrush"] = tonearmBrush;
        if (converter.ConvertFromString(needleColor) is System.Windows.Media.Brush needleBrush)
            Application.Current.Resources["PlayerTurntableNeedleBrush"] = needleBrush;
        if (converter.ConvertFromString(overlayColor) is System.Windows.Media.Brush overlayBrush)
            Application.Current.Resources["PlayerTurntableOverlayBrush"] = overlayBrush;
    }

    private static void ApplyThemeToTree(DependencyObject root, ApplicationTheme theme)
    {
        if (root == null) return;

        if (root is FrameworkElement frameworkElement)
        {
            // Evitar inspeccionar y recorrer elementos hoja comunes para mejorar radicalmente el rendimiento
            if (frameworkElement is Border ||
                frameworkElement is TextBlock ||
                frameworkElement is TextBox ||
                frameworkElement is Image ||
                frameworkElement is System.Windows.Shapes.Shape ||
                frameworkElement is Button ||
                frameworkElement is ProgressBar ||
                frameworkElement is Slider)
            {
                return;
            }

            if (frameworkElement is ItemsControl)
            {
                if (frameworkElement.Resources != null && frameworkElement.Resources.MergedDictionaries.Count > 0)
                {
                    ApplyThemeToDictionary(frameworkElement.Resources, theme);
                }
                return; // Evitar recorrer los elementos/hijos de controles de lista
            }

            if (frameworkElement.Resources != null && frameworkElement.Resources.MergedDictionaries.Count > 0)
            {
                ApplyThemeToDictionary(frameworkElement.Resources, theme);
            }
        }
        else if (root is FrameworkContentElement frameworkContentElement)
        {
            if (frameworkContentElement.Resources != null && frameworkContentElement.Resources.MergedDictionaries.Count > 0)
            {
                ApplyThemeToDictionary(frameworkContentElement.Resources, theme);
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject)
            {
                ApplyThemeToTree(dependencyObject, theme);
            }
        }
    }

    private static void ApplyThemeToDictionary(ResourceDictionary dictionary, ApplicationTheme theme)
    {
        foreach (var mergedDictionary in dictionary.MergedDictionaries)
        {
            if (mergedDictionary.GetType().Name == "ThemesDictionary")
            {
                var themeProperty = mergedDictionary.GetType().GetProperty("Theme");
                themeProperty?.SetValue(mergedDictionary, theme);
            }

            if (mergedDictionary.MergedDictionaries.Count > 0)
            {
                ApplyThemeToDictionary(mergedDictionary, theme);
            }
        }
    }

    /// <summary>
    /// Applies the theme saved in the application settings. Used at application startup.
    /// </summary>
    /// <inheritdoc cref="ApplyTheme"/>
    public static void ApplySavedTheme()
    {
        ApplyTheme(SettingsManager.Current.AppTheme);
    }

    /// <summary>
    /// Applies the specified theme and saves it to the application settings.
    /// </summary>
    /// <inheritdoc cref="ApplyTheme"/>
    public static void ApplyAndSaveTheme(int theme)
    {
        ApplyTheme(theme);
        SettingsManager.Current.AppTheme = theme;
        SettingsManager.SaveSettings();
    }

    /// <summary>
    /// Applies the specified theme.
    /// </summary>
    /// <param name="theme">The theme to apply. 1 for Light, 2 for Dark, 0 or any other value for System Default.</param>
    private static void ApplyTheme(int theme)
    {
        switch (theme)
        {
            case 1:
                UnWatchThemeChanges();
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case 2:
                UnWatchThemeChanges();
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                WatchThemeChanges();
                ApplicationThemeManager.ApplySystemTheme();
                break;
        }
    }

    private static void WatchThemeChanges()
    {
        SystemThemeWatcher.Watch(Application.Current.MainWindow/*, WindowBackdropType.Mica, true*/);
    }

    /// <summary>
    /// Stops watching for system theme changes. (just a wrapper for <see cref="SystemThemeWatcher.UnWatch"/>)
    /// </summary>
    /// <remarks>This function was not necessary because the theme was managed by MicaWPF.</remarks>
    private static void UnWatchThemeChanges()
    {
        // check if window is loaded
        if (Application.Current.MainWindow?.IsLoaded != true) return;

        SystemThemeWatcher.UnWatch(Application.Current.MainWindow);
    }

    /// <summary>
    /// Changes the tray icon according to the specified app theme and setting.
    /// </summary>
    public static void UpdateTrayIcon()
    {
        WeakReferenceMessenger.Default.Send(new RecreateTrayIconMessage());
    }

    /// <summary>
    /// Updates the taskbar widget theme to match the current Windows theme.
    /// </summary>
    public static void UpdateTaskbarWidget()
    {
        WeakReferenceMessenger.Default.Send(new ApplyTaskbarWidgetThemeMessage());
    }
}
