using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF;
using MicaWPF.Core.Enums;
using MicaWPF.Core.Helpers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Wpf.Ui.Controls;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Behaviors;
using FluentFlyoutWPF.Classes.Utils;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.ViewModels;
using System.ComponentModel;

namespace FluentFlyout.Controls;

/// <summary>
/// Interaction logic for TaskbarWidgetControl.xaml
/// </summary>
public partial class TaskbarWidgetControl : UserControl
{
    public static readonly DependencyProperty BackdropImageProperty =
        DependencyProperty.Register(
            nameof(BackdropImage),
            typeof(ImageSource),
            typeof(TaskbarWidgetControl),
            new PropertyMetadata(null, OnBackdropImageChanged));

    public ImageSource? BackdropImage
    {
        get => (ImageSource?)GetValue(BackdropImageProperty);
        set => SetValue(BackdropImageProperty, value);
    }

    public TaskbarWidgetViewModel ViewModel { get; }

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private const double WidgetScale = 0.9;
    private const double OuterHorizontalMargin = 8;
    private const double CoverWidth = 36;
    private const double CoverTextGap = 8;
    private const double TextControlsGap = 8;
    private const double ControlButtonWidth = 32;
    private const int ControlCount = 3;
    private const double MaxVisualWidgetWidth = 216;

    // Cached width calculations
    private string _cachedTitleText = string.Empty;
    private string _cachedArtistText = string.Empty;
    private double _cachedTitleWidth = 0;
    private double _cachedArtistWidth = 0;

    public TaskbarWidgetControl()
    {
        InitializeComponent();

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            ViewModel = App.GetRequiredService<TaskbarWidgetViewModel>();
            DataContext = ViewModel;
        }
        else
        {
            ViewModel = new TaskbarWidgetViewModel();
            DataContext = ViewModel;
        }

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TaskbarWidgetViewModel.ProgressRatio))
            {
                Dispatcher.Invoke(UpdateProgressVisual);
            }
        };

        MainBorder.SizeChanged += (s, e) =>
        {
            var rect = new RectangleGeometry(new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight), 6, 6);
            MainBorder.Clip = rect;
            UpdateProgressVisual();
        };

        Unloaded += TaskbarWidgetControl_Unloaded;

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        
        // Initialize control order
        ReorderControls();
    }

    private static void OnBackdropImageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TaskbarWidgetControl control)
        {
            control.ViewModel.BackdropImage = e.NewValue as ImageSource;
        }
    }

    private void TaskbarWidgetControl_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Dispose();
        Unloaded -= TaskbarWidgetControl_Unloaded;
    }

    public void ReorderControls()
    {
        bool controlsEnabled = SettingsManager.Current.TaskbarWidgetControlsEnabled;

        if (SettingsManager.Current.TaskbarWidgetControlsPosition == 0)
        {
            // Left: Controls, Image, Info
            Grid.SetColumn(ControlsStackPanel, 0);
            LeftControlsColumn.Width = new GridLength(controlsEnabled ? 96 : 0);
            LeftControlsGapColumn.Width = new GridLength(controlsEnabled ? 8 : 0);
            RightControlsColumn.Width = new GridLength(0);
            TextControlsGapColumn.Width = new GridLength(0);
        }
        else
        {
            // Right: Image, Info, Controls
            Grid.SetColumn(ControlsStackPanel, 6);
            LeftControlsColumn.Width = new GridLength(0);
            LeftControlsGapColumn.Width = new GridLength(0);
            RightControlsColumn.Width = new GridLength(controlsEnabled ? 96 : 0);
            TextControlsGapColumn.Width = new GridLength(controlsEnabled ? 8 : 0);
        }
    }


    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

        SolidColorBrush targetBackgroundBrush = Application.Current.TryFindResource("ControlFillColorSecondaryBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
        SolidColorBrush targetBorderBrush = Application.Current.TryFindResource("SurfaceStrokeColorDefaultBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };

        if (!SettingsManager.Current.TaskbarWidgetAnimated)
        {
            // Clear any active animations first
            MainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, null);
            MainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, null);
            
            var newBrush = new SolidColorBrush(targetBackgroundBrush.Color);
            newBrush.Opacity = targetBackgroundBrush.Opacity;
            MainBorder.Background = newBrush;
            TopBorder.BorderBrush = targetBorderBrush;
            return;
        }

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
        
        TopBorder.BorderBrush = targetBorderBrush;
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

        if (!SettingsManager.Current.TaskbarWidgetAnimated)
        {
            // Clear any active animations first
            MainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, null);
            MainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, null);

            var newBrush = new SolidColorBrush(Colors.Transparent);
            newBrush.Opacity = 0;
            MainBorder.Background = newBrush;
            TopBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
            return;
        }

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

        if (ViewModel.OpenFlyoutCommand.CanExecute(null))
        {
            ViewModel.OpenFlyoutCommand.Execute(null);
        }
    }

    public (double logicalWidth, double logicalHeight) CalculateSize(double dpiScale)
    {
        // calculate widget width - use cached values if text hasn't changed
        string currentTitle = ViewModel.Title;
        string currentArtist = ViewModel.Artist;

        if (!string.Equals(currentTitle, _cachedTitleText, StringComparison.Ordinal))
        {
            _cachedTitleWidth = StringWidth.GetStringWidth(currentTitle, 400);
            _cachedTitleText = currentTitle;
        }
        if (!string.Equals(currentArtist, _cachedArtistText, StringComparison.Ordinal))
        {
            _cachedArtistWidth = StringWidth.GetStringWidth(currentArtist, 400);
            _cachedArtistText = currentArtist;
        }

        bool controlsEnabled = SettingsManager.Current.TaskbarWidgetControlsEnabled && ViewModel.ControlsVisibility == Visibility.Visible;
        double textWidth = Math.Max(_cachedTitleWidth, _cachedArtistWidth);
        double controlsWidth = controlsEnabled ? (ControlButtonWidth * ControlCount) : 0;

        double reservedWidth = OuterHorizontalMargin + CoverWidth + CoverTextGap + controlsWidth + (controlsEnabled ? TextControlsGap : 0);
        double visualWidth = Math.Min(textWidth + reservedWidth, MaxVisualWidgetWidth);
        double availableTextWidth = Math.Max(0, visualWidth - reservedWidth);

        SongTitle.Width = availableTextWidth;
        SongArtist.Width = availableTextWidth;

        double logicalWidth = visualWidth / WidgetScale;
        double logicalHeight = 40; // default height

        return (logicalWidth, logicalHeight);
    }

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
    {
        if (title == "-" && artist == "-")
        {
            // no media playing, hide UI
            Dispatcher.Invoke(() =>
            {
                ViewModel.ClearPlayback();
                if (SettingsManager.Current.TaskbarWidgetHideCompletely)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
                TopBorder.BorderBrush = Brushes.Transparent;
                ProgressLine.Width = 0;

                Visibility = Visibility.Visible;
            });
            return;
        }

        Dispatcher.Invoke(() =>
        {
            bool trackChanged = ViewModel.UpdatePlayback(title, artist, icon, playbackStatus, playbackControls);
            if (trackChanged && SettingsManager.Current.TaskbarWidgetAnimated)
            {
                AnimateEntrance();
            }

            ViewModel.RefreshProgressFromPlaybackSource();
            UpdateProgressVisual();
            Visibility = Visibility.Visible;
        });
    }

    private void AnimateEntrance()
    {
        try
        {
            int msDuration = FlyoutAnimationService.GetDuration();

            // opacity and left to right animation for SongInfoStackPanel
            DoubleAnimation opacityAnimation1 = new()
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation translateAnimation1 = new()
            {
                From = -10,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Apply animations to SongInfoStackPanel
            SongInfoStackPanel.BeginAnimation(OpacityProperty, opacityAnimation1);
            TranslateTransform translateTransform1 = new();
            SongInfoStackPanel.RenderTransform = translateTransform1;
            translateTransform1.BeginAnimation(TranslateTransform.XProperty, translateAnimation1);

            // don't play ControlsStackPanel animation if it's not enabled
            if (!SettingsManager.Current.TaskbarWidgetControlsEnabled)
                return;

            // Separate animation instances for ControlsStackPanel
            DoubleAnimation opacityAnimation2 = new()
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation translateAnimation2 = new()
            {
                From = -10,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            ControlsStackPanel.BeginAnimation(OpacityProperty, opacityAnimation2);
            TranslateTransform translateTransform2 = new();
            ControlsStackPanel.RenderTransform = translateTransform2;
            translateTransform2.BeginAnimation(TranslateTransform.XProperty, translateAnimation2);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during entrance animation");
        }
    }

    public void UpdateProgress(double currentSeconds, double totalSeconds)
    {
        ViewModel.UpdateProgress(currentSeconds, totalSeconds);
        Dispatcher.Invoke(UpdateProgressVisual);
    }

    private void UpdateProgressVisual()
    {
        double availableWidth = RootGrid.ActualWidth > 0
            ? RootGrid.ActualWidth
            : (ActualWidth > 0 ? ActualWidth : MainBorder.ActualWidth);

        if (ViewModel.ProgressRatio <= 0 || availableWidth <= 0)
        {
            ProgressLine.Width = 0;
            return;
        }

        ProgressLine.Width = availableWidth * ViewModel.ProgressRatio;
    }
}
