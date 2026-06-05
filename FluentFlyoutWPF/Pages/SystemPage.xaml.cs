using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluentFlyoutWPF.Pages;

public partial class SystemPage : Page
{
    public SystemPage()
    {
        InitializeComponent();
        DataContext = DesignerProperties.GetIsInDesignMode(this)
            ? DesignTimeViewModelFactory.CreateSettingsShellViewModel()
            : App.GetRequiredService<SettingsShellViewModel>();

        // Subscribe to color changes to keep the swatch button in sync
        SettingsManager.Current.PropertyChanged += Settings_PropertyChanged;
        Loaded += (_, _) => UpdateSwatchColor(SettingsManager.Current.CustomAccentColorHex);
        Unloaded += (_, _) => SettingsManager.Current.PropertyChanged -= Settings_PropertyChanged;
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettings.CustomAccentColorHex))
        {
            UpdateSwatchColor(SettingsManager.Current.CustomAccentColorHex);
        }
    }

    private void UpdateSwatchColor(string? hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && hex.StartsWith('#'))
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                if (ColorPickerToggleButton != null)
                {
                    ColorPickerToggleButton.Background = brush;
                }
            }
        }
        catch
        {
            // Invalid hex — leave swatch color unchanged
        }
    }

    private void ColorPickerToggleButton_Click(object sender, RoutedEventArgs e)
    {
        ColorPickerPopup.IsOpen = !ColorPickerPopup.IsOpen;
    }
}
