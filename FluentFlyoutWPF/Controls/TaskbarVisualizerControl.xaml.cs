using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.ViewModels;
using System.ComponentModel;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace FluentFlyout.Controls;

/// <summary>
/// Interaction logic for TaskbarVisualizerControl.xaml
/// </summary>
public partial class TaskbarVisualizerControl : UserControl
{
    // reference to main window for flyout functions
    private static Visualizer visualizer => Visualizer.Instance;

    public TaskbarVisualizerControl()
    {
        InitializeComponent();

        // Set DataContext for bindings
        DataContext = DesignerProperties.GetIsInDesignMode(this)
            ? DesignTimeViewModelFactory.CreateSettingsShellViewModel()
            : App.GetRequiredService<SettingsShellViewModel>();

        if (!DesignerProperties.GetIsInDesignMode(this) && SettingsManager.Current.TaskbarVisualizerEnabled)
        {
            visualizer.Start();
        }

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            VisualizerContainer.Source = visualizer.Bitmap;
        }

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            SettingsManager.Current.PropertyChanged += Settings_PropertyChanged;
            Unloaded += (s, e) => { SettingsManager.Current.PropertyChanged -= Settings_PropertyChanged; };
        }

        ApplyBackgroundBlur();
    }

    public static void OnTaskbarVisualizerEnabledChanged(bool value)
    {
        if (value)
        {
            visualizer.Start();
        }
        else if (!visualizer.ShouldBeRunning)
        {
            visualizer.Stop();
        }
    }

    public static void DisposeVisualizer()
    {
        visualizer.Dispose();
    }

    // TODO: The following mouse events are almost the same as the ones in TaskbarWidgetControl.xaml.cs.
    // We should find a way to unify these methods instead of duplicating them.

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

        SolidColorBrush targetBackgroundBrush = Application.Current.TryFindResource("ControlFillColorSecondaryBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
        SolidColorBrush targetBorderBrush = Application.Current.TryFindResource("SurfaceStrokeColorDefaultBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };

        TopBorder.BorderBrush = targetBorderBrush;

        // Animate background
        var backgroundAnimation = new ColorAnimation
        {
            To = targetBackgroundBrush.Color,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = targetBackgroundBrush.Opacity,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // rare case where background is not a SolidColorBrush after SetupWindow
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;
        
        // Animate back to transparent
        var backgroundAnimation = new ColorAnimation
        {
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        MainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);

        TopBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // only continue when the visualizer is clickable and actually has content
        // otherwise it would show an empty container to click on which is weird
        if (!SettingsManager.Current.TaskbarVisualizerClickable || !SettingsManager.Current.TaskbarVisualizerHasContent) return;

        // open settings when clicked
        App.GetRequiredService<IWindowManager>().ShowSettings("TaskbarVisualizerPage");
    }

    private ImageSource? _currentImage = null;

    public void UpdateBackground(ImageSource? image)
    {
        _currentImage = image;
        ApplyBackgroundBlur();
    }

    private void ApplyBackgroundBlur()
    {
        var blurOption = SettingsManager.Current.MediaFlyoutBackgroundBlur;
        MediaBackdropStyleService.ApplyPresetToImage(
            BackgroundBlurImage,
            blurOption,
            MediaBackdropSurface.Taskbar,
            _currentImage);
    }

    private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettings.MediaFlyoutBackgroundBlur))
        {
            Dispatcher.Invoke(ApplyBackgroundBlur);
        }
    }
}
