using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Classes.Behaviors;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
using System.ComponentModel;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    public static readonly DependencyProperty BackdropImageProperty =
        DependencyProperty.Register(
            nameof(BackdropImage),
            typeof(ImageSource),
            typeof(TaskbarVisualizerControl),
            new PropertyMetadata(null, OnBackdropImageChanged));

    public ImageSource? BackdropImage
    {
        get => (ImageSource?)GetValue(BackdropImageProperty);
        set => SetValue(BackdropImageProperty, value);
    }

    public TaskbarVisualizerViewModel ViewModel { get; }

    // reference to main window for flyout functions
    private static Visualizer visualizer => Visualizer.Instance;

    public TaskbarVisualizerControl()
    {
        InitializeComponent();

        ViewModel = DesignerProperties.GetIsInDesignMode(this)
            ? new TaskbarVisualizerViewModel()
            : App.GetRequiredService<TaskbarVisualizerViewModel>();
        DataContext = ViewModel;

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
            WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, (r, m) =>
            {
                Dispatcher.Invoke(ApplyBackgroundBlur);
            });

            Unloaded += (s, e) => { SettingsManager.Current.PropertyChanged -= Settings_PropertyChanged; };
        }

        ApplyBackgroundBlur();
    }

    private static void OnBackdropImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TaskbarVisualizerControl control)
        {
            control.ViewModel.UpdateBackground(e.NewValue as ImageSource);
        }
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
        if (!ViewModel.IsClickable || !ViewModel.HasContent) return;

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

        // rare case where background is not a SolidColorBrush after SetupWindow or is frozen
        if (MainBorder.Background is not SolidColorBrush || MainBorder.Background.IsFrozen)
        {
            var currentBrush = MainBorder.Background as SolidColorBrush;
            var newBrush = new SolidColorBrush(currentBrush?.Color ?? Colors.Transparent);
            newBrush.Opacity = currentBrush?.Opacity ?? 0;
            MainBorder.Background = newBrush;
        }

        MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!ViewModel.IsClickable || !ViewModel.HasContent) return;
        
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

        if (MainBorder.Background is not SolidColorBrush || MainBorder.Background.IsFrozen)
        {
            var currentBrush = MainBorder.Background as SolidColorBrush;
            var newBrush = new SolidColorBrush(currentBrush?.Color ?? Colors.Transparent);
            newBrush.Opacity = currentBrush?.Opacity ?? 0;
            MainBorder.Background = newBrush;
        }

        MainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);

        TopBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // only continue when the visualizer is clickable and actually has content
        // otherwise it would show an empty container to click on which is weird
        if (ViewModel.OpenSettingsCommand.CanExecute(null))
        {
            ViewModel.OpenSettingsCommand.Execute(null);
        }
    }

    public void UpdateBackground(ImageSource? image)
    {
        if (Dispatcher.CheckAccess())
        {
            SetFallbackBackdropImage(image);
            ApplyBackgroundBlur();
            return;
        }

        _ = Dispatcher.InvokeAsync(() =>
        {
            SetFallbackBackdropImage(image);
            ApplyBackgroundBlur();
        });
    }

    private void SetFallbackBackdropImage(ImageSource? image)
    {
        if (BindingOperations.GetBindingExpression(this, BackdropImageProperty) == null)
        {
            BackdropImage = image;
        }

        ViewModel.UpdateBackground(image);
    }

    private void ApplyBackgroundBlur()
    {
        MediaBackdropBehavior.Refresh(BackgroundBlurImage);
    }

    private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettings.TaskbarVisualizerClickable) ||
            e.PropertyName == nameof(UserSettings.TaskbarVisualizerHasContent) ||
            e.PropertyName == nameof(UserSettings.MediaFlyoutBackgroundBlur) ||
            e.PropertyName == nameof(UserSettings.MediaFlyoutAcrylicWindowEnabled) ||
            e.PropertyName == nameof(UserSettings.AcrylicBlurOpacity))
        {
            ViewModel.RefreshFromSettings();
            Dispatcher.Invoke(ApplyBackgroundBlur);
        }
    }
}
