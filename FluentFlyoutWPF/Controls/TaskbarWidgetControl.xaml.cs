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
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly double _scale = 0.9;
    private readonly int _nativeWidgetsPadding = 216;

    // Cached width calculations
    private string _cachedTitleText = string.Empty;
    private string _cachedArtistText = string.Empty;
    private double _cachedTitleWidth = 0;
    private double _cachedArtistWidth = 0;
    private double _lastProgressCurrentSeconds;
    private double _lastProgressTotalSeconds;
    private readonly DispatcherTimer _progressTimer;

    private bool _isPaused;

    public TaskbarWidgetControl()
    {
        InitializeComponent();

        if (!DesignerProperties.GetIsInDesignMode(this))
        {
            // Keep runtime bindings aligned with the shared settings view model.
            DataContext = App.GetRequiredService<SettingsShellViewModel>();
        }

        WeakReferenceMessenger.Default.Register<UpdateAccentColorMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(() => ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault()));
        });

        MainBorder.SizeChanged += (s, e) =>
        {
            var rect = new RectangleGeometry(new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight), 6, 6);
            MainBorder.Clip = rect;
            UpdateProgressVisual();
        };

        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _progressTimer.Tick += ProgressTimer_Tick;
        _progressTimer.Start();
        Unloaded += TaskbarWidgetControl_Unloaded;

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)); ;
        
        // Initialize control order
        ReorderControls();
    }

    private void ProgressTimer_Tick(object? sender, EventArgs e)
    {
        RefreshProgressFromPlaybackSource();
    }

    private void TaskbarWidgetControl_Unloaded(object sender, RoutedEventArgs e)
    {
        _progressTimer.Stop();
        _progressTimer.Tick -= ProgressTimer_Tick;
        Unloaded -= TaskbarWidgetControl_Unloaded;
    }

    public void ReorderControls()
    {
        // Remove ControlsStackPanel from MainStackPanel
        MainStackPanel.Children.Remove(ControlsStackPanel);

        // Reorder based on position setting
        if (SettingsManager.Current.TaskbarWidgetControlsPosition == 0)
        {
            // Left: Controls, Image, Info
            MainStackPanel.Children.Insert(0, ControlsStackPanel);
            ControlsStackPanel.Margin = new Thickness(2, 0, 6, 0); // for some reason margins are weird on left side
        }
        else
        {
            // Right: Image, Info, Controls
            MainStackPanel.Children.Add(ControlsStackPanel);
            ControlsStackPanel.Margin = new Thickness(8, 0, 0, 0);
        }
    }


    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

        SolidColorBrush targetBackgroundBrush = Application.Current.TryFindResource("ControlFillColorSecondaryBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
        SolidColorBrush targetBorderBrush = Application.Current.TryFindResource("SurfaceStrokeColorDefaultBrush") as SolidColorBrush ?? new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };

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
        
        TopBorder.BorderBrush = targetBorderBrush;
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

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

        // toggle main flyout when clicked
        WeakReferenceMessenger.Default.Send(new ShowMediaFlyoutMessage(toggleMode: true, forceShow: true));
    }

    public (double logicalWidth, double logicalHeight) CalculateSize(double dpiScale)
    {
        // calculate widget width - use cached values if text hasn't changed
        string currentTitle = SongTitle.Text;
        string currentArtist = SongArtist.Text;

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

        double logicalWidth = Math.Max(_cachedTitleWidth, _cachedArtistWidth) + 55; // add margin for cover image
        // maximum width limit, same as Windows native widget
        logicalWidth = Math.Min(logicalWidth, _nativeWidgetsPadding / _scale);

        SongTitle.Width = Math.Max(logicalWidth - 58, 0);
        SongArtist.Width = Math.Max(logicalWidth - 58, 0);

        // add space for playback controls if enabled and visible
        if (SettingsManager.Current.TaskbarWidgetControlsEnabled && ControlsStackPanel.Visibility == Visibility.Visible)
        {
            logicalWidth += (int)(102);
        }


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
                if (SettingsManager.Current.TaskbarWidgetHideCompletely)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                ControlsStackPanel.Visibility = Visibility.Collapsed;
                SongTitle.Text = string.Empty;
                SongArtist.Text = string.Empty;
                SongInfoStackPanel.Visibility = Visibility.Collapsed;
                SongInfoStackPanel.ToolTip = string.Empty;
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
                TopBorder.BorderBrush = Brushes.Transparent;
                _lastProgressCurrentSeconds = 0;
                _lastProgressTotalSeconds = 0;
                ProgressLine.Width = 0;
                ProgressLine.Visibility = Visibility.Collapsed;

                Visibility = Visibility.Visible;
            });
            return;
        }

        _isPaused = false;
        if (playbackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            _isPaused = true;
        }

        // adjust UI based on available controls
        Dispatcher.Invoke(() =>
        {
            if (SettingsManager.Current.TaskbarWidgetControlsEnabled && playbackControls != null)
            {
                PreviousButton.IsHitTestVisible = playbackControls.IsPreviousEnabled;
                PlayPauseButton.IsHitTestVisible = playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled;
                NextButton.IsHitTestVisible = playbackControls.IsNextEnabled;

                PreviousButton.Opacity = playbackControls.IsPreviousEnabled ? 1 : 0.5;
                PlayPauseButton.Opacity = (playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled) ? 1 : 0.5;
                NextButton.Opacity = playbackControls.IsNextEnabled ? 1 : 0.5;
            }
            else if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
            {
                // Fallback for the internal player
                bool hasPrevious = MusicPlayerService.Instance.CanGoPrevious;
                bool hasNext = MusicPlayerService.Instance.CanGoNext;

                PreviousButton.IsHitTestVisible = hasPrevious;
                PlayPauseButton.IsHitTestVisible = true; // Internal player can always toggle play/pause
                NextButton.IsHitTestVisible = hasNext;

                PreviousButton.Opacity = hasPrevious ? 1 : 0.5;
                PlayPauseButton.Opacity = 1;
                NextButton.Opacity = hasNext ? 1 : 0.5;
            }
            else
            {
                PreviousButton.IsHitTestVisible = false;
                PlayPauseButton.IsHitTestVisible = false;
                NextButton.IsHitTestVisible = false;

                PreviousButton.Opacity = 0.5;
                NextButton.Opacity = 0.5;
                PlayPauseButton.Opacity = 0.5;
            }
        });

        Dispatcher.Invoke(() =>
        {
            if (SongTitle.Text != title && SongArtist.Text != artist)
            {
                // changed info
                if (SettingsManager.Current.TaskbarWidgetAnimated)
                {
                    AnimateEntrance();
                }
            }

            SongTitle.Text = !string.IsNullOrEmpty(title) ? title : "-";
            SongArtist.Text = !string.IsNullOrEmpty(artist) ? artist : "-";

            // Update tooltip with song info
            SongInfoStackPanel.ToolTip = string.Empty;
            SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(title) ? title : string.Empty;
            SongInfoStackPanel.ToolTip += !string.IsNullOrEmpty(artist) ? "\n\n" + artist : string.Empty;

            if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
            {
                PlayPauseIcon.Symbol = _isPaused ? SymbolRegular.Play24 : SymbolRegular.Pause24;
            }

            ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault());

            // Handled in ApplyAccentColor

            if (icon != null)
            {
                if (_isPaused)
                { // show pause icon overlay
                    SongImagePlaceholder.Symbol = SymbolRegular.Pause24;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.Opacity = 0.4;
                }
                else
                {
                    SongImagePlaceholder.Visibility = Visibility.Collapsed;
                    SongImage.Opacity = 1;
                }
                SongImage.ImageSource = icon;
                BackgroundImage.Source = icon;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -2); // align image better when cover is present
            }
            else
            {
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
            }

            SongTitle.Visibility = Visibility.Visible;
            SongArtist.Visibility = !string.IsNullOrEmpty(artist) ? Visibility.Visible : Visibility.Collapsed; // hide artist if it's not available
            SongInfoStackPanel.Visibility = Visibility.Visible;
            BackgroundImage.Visibility = SettingsManager.Current.TaskbarWidgetBackgroundBlur ? Visibility.Visible : Visibility.Collapsed;

            // on top of XAML visibility binding (XAML binding only hides when disabled in settings)
            ControlsStackPanel.Visibility = SettingsManager.Current.TaskbarWidgetControlsEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;

            RefreshProgressFromPlaybackSource();
            Visibility = Visibility.Visible;
        });
    }

    private void AnimateEntrance()
    {
        try
        {
            int msDuration = FlyoutAnimationService.GetDuration();

            // opacity and left to right animation for SongInfoStackPanel
            DoubleAnimation opacityAnimation = new()
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation translateAnimation = new()
            {
                From = -10,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Apply animations
            SongInfoStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            TranslateTransform translateTransform = new();
            SongInfoStackPanel.RenderTransform = translateTransform;
            translateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);

            // don't play ControlsStackPanel animation if it's not enabled
            if (!SettingsManager.Current.TaskbarWidgetControlsEnabled)
                return;

            ControlsStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            TranslateTransform translateTransform2 = new();
            ControlsStackPanel.RenderTransform = translateTransform2;
            translateTransform2.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during entrance animation");
        }
    }

    // event handlers for media control buttons
    private async void Previous_Click(object sender, RoutedEventArgs e)
    {

        var focusedSession = ExternalMediaService.Instance.GetPreferredSession();
        if (focusedSession != null && !ExternalMediaService.Instance.IsInternalSession(focusedSession))
        {
            await focusedSession.ControlSession.TrySkipPreviousAsync();
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.PlayPrevious();
        }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {

        var focusedSession = ExternalMediaService.Instance.GetPreferredSession();
        if (focusedSession != null && !ExternalMediaService.Instance.IsInternalSession(focusedSession))
        {
            if (_isPaused) await focusedSession.ControlSession.TryPlayAsync();
            else await focusedSession.ControlSession.TryPauseAsync();
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.TogglePlayPause();
        }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {

        var focusedSession = ExternalMediaService.Instance.GetPreferredSession();
        if (focusedSession != null && !ExternalMediaService.Instance.IsInternalSession(focusedSession))
        {
            await focusedSession.ControlSession.TrySkipNextAsync();
        }
        else if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.PlayNext();
        }
    }

    private void ApplyAccentColor(SolidColorBrush? brush)
    {
        bool useAccent = AccentColorResolver.ShouldUseAccent(brush);
        var activeBrush = AccentColorResolver.ResolveAccentBrush(brush);
        var neutralBrush = ThemeResourceHelper.GetSecondaryTextSolidBrush();
        var displayBrush = useAccent ? activeBrush : neutralBrush;

        Action action = () =>
        {
            PreviousIcon.Foreground = displayBrush;
            PlayPauseIcon.Foreground = displayBrush;
            NextIcon.Foreground = displayBrush;
            SongImagePlaceholder.Foreground = displayBrush;
            ProgressLine.Fill = displayBrush;
        };

        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = Dispatcher.InvokeAsync(action);
        }
    }

    public void UpdateProgress(double currentSeconds, double totalSeconds)
    {
        _lastProgressCurrentSeconds = Math.Max(0, currentSeconds);
        _lastProgressTotalSeconds = Math.Max(0, totalSeconds);

        if (_lastProgressTotalSeconds <= 0)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressLine.Width = 0;
                ProgressLine.Visibility = Visibility.Collapsed;
            });
            return;
        }

        Dispatcher.Invoke(UpdateProgressVisual);
    }

    private void RefreshProgressFromPlaybackSource()
    {
        if (!IsVisible || string.IsNullOrEmpty(SongTitle.Text))
            return;

        var preferredSession = ExternalMediaService.Instance.GetPreferredSession();
        if (preferredSession != null && !ExternalMediaService.Instance.IsInternalSession(preferredSession))
        {
            var timeline = preferredSession.ControlSession.GetTimelineProperties();
            var playbackInfo = preferredSession.ControlSession.GetPlaybackInfo();
            var currentPosition = timeline.Position;

            if (playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                currentPosition += DateTime.Now - timeline.LastUpdatedTime.DateTime;
            }

            var totalDuration = timeline.MaxSeekTime.TotalSeconds > 0
                ? timeline.MaxSeekTime
                : timeline.EndTime;

            if (totalDuration <= TimeSpan.Zero)
            {
                UpdateProgress(0, 0);
                return;
            }

            if (currentPosition > totalDuration)
            {
                currentPosition = totalDuration;
            }

            UpdateProgress(currentPosition.TotalSeconds, totalDuration.TotalSeconds);
            return;
        }

        if (SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null)
        {
            UpdateProgress(
                MusicPlayerService.Instance.CurrentPosition.TotalSeconds,
                MusicPlayerService.Instance.TotalDuration.TotalSeconds);
            return;
        }

        UpdateProgress(0, 0);
    }

    private void UpdateProgressVisual()
    {
        double availableWidth = RootGrid.ActualWidth > 0
            ? RootGrid.ActualWidth
            : (ActualWidth > 0 ? ActualWidth : MainBorder.ActualWidth);

        if (_lastProgressTotalSeconds <= 0 || availableWidth <= 0)
        {
            ProgressLine.Width = 0;
            ProgressLine.Visibility = Visibility.Collapsed;
            return;
        }

        if (ProgressLine.Visibility != Visibility.Visible)
            ProgressLine.Visibility = Visibility.Visible;

        double ratio = Math.Clamp(_lastProgressCurrentSeconds / _lastProgressTotalSeconds, 0, 1);
        ProgressLine.Width = availableWidth * ratio;
    }
}
