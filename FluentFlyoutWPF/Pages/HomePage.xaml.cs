using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Input;
using System.Windows.Threading;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
using System.Linq;
using System.Windows.Media.Effects;
using System.ComponentModel;

namespace FluentFlyoutWPF.Pages;

public partial class HomePage : Page
{
    private bool _isDraggingSeekbar;
    private readonly NowPlayingViewModel _viewModel = new();

    // Lyrics scroll management
    private bool _isAutoScrollPaused;
    private DateTime _lastScrollTime;
    private readonly DispatcherTimer _resumeAutoScrollTimer;

    public HomePage()
    {
        _viewModel.Volume = MusicPlayerService.Instance.Volume;
        InitializeComponent();
        DataContext = _viewModel;

        // Inicializar el contenido del reproductor con la vista de reproducción actual
        MainContentControl.Content = _viewModel;
        MainContentControl.ContentTemplate = (DataTemplate)MainContentControl.Resources["NowPlayingTemplate"];

        WeakReferenceMessenger.Default.Register<UpdateAccentColorMessage>(this, (r, m) =>
        {
            Dispatcher.Invoke(() => ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault()));
        });

        _resumeAutoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _resumeAutoScrollTimer.Tick += (s, e) => ResumeAutoScroll();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Deshabilitar el scroll del contenedor padre si existe, para que solo 
        // los elementos internos de la página puedan scrollear.
        DependencyObject parent = VisualTreeHelper.GetParent(this);
        while (parent != null)
        {
            if (parent is ScrollViewer sv)
            {
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                break;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }

        MusicPlayerService.Instance.PropertyChanged += MusicPlayerService_PropertyChanged;
        MusicPlayerService.Instance.TrackChanged += MusicPlayerService_TrackChanged;
        MusicPlayerService.Instance.PlaybackError += MusicPlayerService_PlaybackError;
        
        Visualizer.Instance.AddClient();
        VisualizerContainer.Source = Visualizer.Instance.Bitmap;

        SettingsManager.Current.PropertyChanged += Settings_PropertyChanged;
        LibraryManager.Instance.TrackMetadataUpdated += LibraryManager_TrackMetadataUpdated;

        UpdateViewModel();
        UpdateBackgroundBlur();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        MusicPlayerService.Instance.PropertyChanged -= MusicPlayerService_PropertyChanged;
        MusicPlayerService.Instance.TrackChanged -= MusicPlayerService_TrackChanged;
        MusicPlayerService.Instance.PlaybackError -= MusicPlayerService_PlaybackError;

        SettingsManager.Current.PropertyChanged -= Settings_PropertyChanged;
        LibraryManager.Instance.TrackMetadataUpdated -= LibraryManager_TrackMetadataUpdated;

        Visualizer.Instance.RemoveClient();
    }

    private void MusicPlayerService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(MusicPlayerService.CurrentTrack):
                case nameof(MusicPlayerService.IsPlaying):
                    UpdateViewModel();
                    break;
                case nameof(MusicPlayerService.CurrentPosition):
                    if (!_isDraggingSeekbar)
                    {
                        UpdateSeekbar();
                    }
                    break;
                case nameof(MusicPlayerService.CurrentLyricLine):
                    UpdateLyrics();
                    break;
                case nameof(MusicPlayerService.Volume):
                    _viewModel.Volume = MusicPlayerService.Instance.Volume;
                    break;
            }
        });
    }

    private void MusicPlayerService_TrackChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateViewModel();
            UpdateBackgroundBlur();
        });
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserSettings.MediaFlyoutBackgroundBlur))
        {
            Dispatcher.Invoke(UpdateBackgroundBlur);
        }
    }

    private void LibraryManager_TrackMetadataUpdated(object? sender, TrackModel e)
    {
        if (e.FilePath == MusicPlayerService.Instance.CurrentTrack?.FilePath)
        {
            Dispatcher.Invoke(UpdateViewModel);
        }
    }

    private void UpdateViewModel()
    {
        var track = MusicPlayerService.Instance.CurrentTrack;
        if (track != null)
        {
            _viewModel.Title = track.Title;
            _viewModel.Artist = track.FullArtistDisplay;
            _viewModel.HasTrack = true;
            _viewModel.CurrentTrack = track;

            var art = LibraryManager.Instance.GetAlbumArt(track);
            _viewModel.CoverImage = art;
            _viewModel.CoverUrl = track.CoverUrl;

            _viewModel.TotalTime = track.Duration.ToString(@"m\:ss");
            _viewModel.SeekMaximum = track.Duration.TotalSeconds;
            _viewModel.SeekValue = MusicPlayerService.Instance.CurrentPosition.TotalSeconds;
            _viewModel.CurrentTime = MusicPlayerService.Instance.CurrentPosition.ToString(@"m\:ss");
            
            BackgroundBlurImage.Source = art;
        }
        else
        {
            _viewModel.Title = Application.Current.TryFindResource("Player_NoTrack")?.ToString() ?? "No track playing";
            _viewModel.Artist = Application.Current.TryFindResource("Player_SelectTrackMsg")?.ToString() ?? "Select a track from the library";
            _viewModel.CoverImage = null;
            _viewModel.CoverUrl = string.Empty;
            _viewModel.HasTrack = false;
            _viewModel.CurrentTrack = null;
            _viewModel.TotalTime = "0:00";
            _viewModel.SeekMaximum = 100;
            _viewModel.SeekValue = 0;
            _viewModel.CurrentTime = "0:00";
            
            BackgroundBlurImage.Source = null;
        }

        // Sync lyrics collection
        var currentLyrics = MusicPlayerService.Instance.CurrentLyrics;
        _viewModel.HasLyrics = currentLyrics.Count > 0;
        
        if (_viewModel.LyricLines.Count != currentLyrics.Count)
        {
            _viewModel.LyricLines.Clear();
            foreach (var l in currentLyrics)
            {
                _viewModel.LyricLines.Add(l);
            }
        }

        _viewModel.IsPlaying = MusicPlayerService.Instance.IsPlaying;
        _viewModel.IsShuffleEnabled = MusicPlayerService.Instance.IsShuffleEnabled;
        _viewModel.RepeatMode = MusicPlayerService.Instance.RepeatMode;
        _viewModel.Volume = MusicPlayerService.Instance.Volume;

        UpdateLyrics();

        PlayPauseIcon.Symbol = MusicPlayerService.Instance.IsPlaying
            ? Wpf.Ui.Controls.SymbolRegular.Pause24
            : Wpf.Ui.Controls.SymbolRegular.Play24;

        UpdateShuffleRepeatVisuals();
        UpdateLyrics();
        ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault());
    }

    private void EditLyrics_Click(object sender, RoutedEventArgs e)
    {
        var track = MusicPlayerService.Instance.CurrentTrack;
        if (track != null)
        {
            Windows.EditTrackWindow.ShowInstance(track, Window.GetWindow(this), true);
        }
    }

    private void EqualizerButton_Click(object sender, RoutedEventArgs e)
    {
        Windows.EqualizerWindow.ShowInstance(Window.GetWindow(this));
    }

    private void UpdateShuffleRepeatVisuals()
    {
        var isShuffle = MusicPlayerService.Instance.IsShuffleEnabled;
        var mode = MusicPlayerService.Instance.RepeatMode;
        
        var dominantBrush = BitmapHelper.SavedDominantColors.FirstOrDefault();
        bool useAccent = SettingsManager.Current.UseAlbumArtAsAccentColor && dominantBrush != null;
        
        var accentBrush = useAccent ? dominantBrush! : (Brush)Application.Current.TryFindResource("AccentFillColorDefaultBrush") ?? Brushes.DeepSkyBlue;
        var defaultBrush = Brushes.White;

        // Shuffle Visuals
        if (ShuffleButton.Content is Wpf.Ui.Controls.SymbolIcon shuffleIcon)
        {
            shuffleIcon.Symbol = isShuffle ? Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24 : Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24;
            shuffleIcon.Foreground = isShuffle ? accentBrush : defaultBrush;
            ShuffleButton.Opacity = isShuffle ? 1.0 : 0.5;
        }

        // Repeat Visuals
        if (RepeatButton.Content is Wpf.Ui.Controls.SymbolIcon repeatIcon)
        {
            switch (mode)
            {
                case Classes.RepeatMode.One:
                    repeatIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeat124;
                    repeatIcon.Foreground = accentBrush;
                    RepeatButton.Opacity = 1.0;
                    break;
                case Classes.RepeatMode.All:
                    repeatIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAll24;
                    repeatIcon.Foreground = accentBrush;
                    RepeatButton.Opacity = 1.0;
                    break;
                case Classes.RepeatMode.None:
                default:
                    repeatIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowRepeatAllOff24;
                    repeatIcon.Foreground = defaultBrush;
                    RepeatButton.Opacity = 0.5;
                    break;
            }
        }
    }

    public static readonly DependencyProperty ScrollOffsetProperty =
        DependencyProperty.Register("ScrollOffset", typeof(double), typeof(HomePage),
            new PropertyMetadata(0.0, OnScrollOffsetChanged));

    public double ScrollOffset
    {
        get => (double)GetValue(ScrollOffsetProperty);
        set => SetValue(ScrollOffsetProperty, value);
    }

    private ScrollViewer? _currentScrollViewer;

    private static void OnScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var page = d as HomePage;
        if (page?._currentScrollViewer != null)
        {
            page._currentScrollViewer.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    private void UpdateLyrics()
    {
        var line = MusicPlayerService.Instance.CurrentLyricLine;
        _viewModel.ActiveLyricLine = line;
        
        // Also update the legacy text property for safety
        _viewModel.Lyrics = line?.Text ?? "";

        // Auto-scroll to current line smoothly
        if (line != null && !_isAutoScrollPaused)
        {
            var lyricsList = FindVisualChild<ListBox>(this, "LyricsList") ?? FindVisualChild<ListBox>(this, "FullLyricsList");
            if (lyricsList != null)
            {
                // Ensure the ListBox selection is synced
                if (!Equals(lyricsList.SelectedItem, line))
                {
                    lyricsList.SelectedItem = line;
                }

                if (lyricsList.SelectedItem != null)
                {
                    if (_currentScrollViewer == null || !lyricsList.IsAncestorOf(_currentScrollViewer))
                    {
                        _currentScrollViewer = FindVisualChild<ScrollViewer>(lyricsList, null!);
                    }

                    if (_currentScrollViewer != null)
                    {
                        // Ensure containers are generated
                        lyricsList.UpdateLayout();
                        if (lyricsList.ItemContainerGenerator.ContainerFromItem(lyricsList.SelectedItem) is FrameworkElement item)
                        {
                            var transform = item.TransformToAncestor(_currentScrollViewer);
                            var positionInScrollViewer = transform.Transform(new Point(0, 0));
                            
                            double targetOffset = _currentScrollViewer.VerticalOffset 
                                                  + positionInScrollViewer.Y 
                                                  - (_currentScrollViewer.ViewportHeight / 2) 
                                                  + (item.ActualHeight / 2);

                            if (targetOffset < 0) targetOffset = 0;
                            if (targetOffset > _currentScrollViewer.ScrollableHeight) targetOffset = _currentScrollViewer.ScrollableHeight;

                            if (Math.Abs(_currentScrollViewer.VerticalOffset - targetOffset) > 1.0)
                            {
                                this.ScrollOffset = _currentScrollViewer.VerticalOffset;
                                var anim = new System.Windows.Media.Animation.DoubleAnimation
                                {
                                    To = targetOffset,
                                    Duration = TimeSpan.FromSeconds(0.6),
                                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                                };
                                this.BeginAnimation(ScrollOffsetProperty, anim);
                            }
                        }
                        else
                        {
                            lyricsList.ScrollIntoView(lyricsList.SelectedItem);
                        }
                    }
                    else
                    {
                        lyricsList.ScrollIntoView(lyricsList.SelectedItem);
                    }
                }
            }
        }
    }

    private void LyricsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox)
        {
            var element = e.OriginalSource as FrameworkElement;
            if (element?.DataContext is LyricLine line)
            {
                MusicPlayerService.Instance.Seek(line.Time);
                ResumeAutoScroll();
            }
            else if (element?.DataContext is LyricWord word)
            {
                MusicPlayerService.Instance.Seek(word.Time);
                ResumeAutoScroll();
            }
        }
    }

    private void LyricsList_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        PauseAutoScroll();
    }

    private void LyricsList_PreviewTouchDown(object sender, System.Windows.Input.TouchEventArgs e)
    {
        PauseAutoScroll();
    }

    private void PauseAutoScroll()
    {
        _isAutoScrollPaused = true;
        _lastScrollTime = DateTime.Now;
        _resumeAutoScrollTimer.Stop();
        _resumeAutoScrollTimer.Start();

        if (FindVisualChild<Wpf.Ui.Controls.Button>(this, "ResumeSyncButton") is { } btn) btn.Visibility = Visibility.Visible;
        
        if (FindVisualChild<Wpf.Ui.Controls.Button>(this, "ResumeSyncButtonFull") is { } btnFull) btnFull.Visibility = Visibility.Visible;
    }

    private void ResumeAutoScroll()
    {
        _isAutoScrollPaused = false;
        _resumeAutoScrollTimer.Stop();
        
        if (FindVisualChild<Wpf.Ui.Controls.Button>(this, "ResumeSyncButton") is { } btn) btn.Visibility = Visibility.Collapsed;

        if (FindVisualChild<Wpf.Ui.Controls.Button>(this, "ResumeSyncButtonFull") is { } btnFull) btnFull.Visibility = Visibility.Collapsed;

        UpdateLyrics();
    }

    private void ResumeSyncButton_Click(object sender, RoutedEventArgs e)
    {
        ResumeAutoScroll();
    }

    private T? FindVisualChild<T>(DependencyObject obj, string? name) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(obj, i);
            if (child != null && child is T t && (string.IsNullOrEmpty(name) || (child as FrameworkElement)?.Name == name))
                return t;
            else
            {
                T? childOfChild = FindVisualChild<T>(child!, name);
                if (childOfChild != null)
                    return childOfChild;
            }
        }
        return null;
    }



    private void UpdateSeekbar()
    {
        var position = MusicPlayerService.Instance.CurrentPosition;
        _viewModel.SeekValue = position.TotalSeconds;
        _viewModel.CurrentTime = position.ToString(@"m\:ss");
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        MusicPlayerService.Instance.TogglePlayPause();
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        MusicPlayerService.Instance.PlayPrevious();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        MusicPlayerService.Instance.PlayNext();
    }

    private void ShuffleButton_Click(object sender, RoutedEventArgs e)
    {
        MusicPlayerService.Instance.IsShuffleEnabled = !MusicPlayerService.Instance.IsShuffleEnabled;
        _viewModel.IsShuffleEnabled = MusicPlayerService.Instance.IsShuffleEnabled;
        UpdateShuffleRepeatVisuals();
    }

    private void RepeatButton_Click(object sender, RoutedEventArgs e)
    {
        var mode = MusicPlayerService.Instance.RepeatMode;
        MusicPlayerService.Instance.RepeatMode = mode switch
        {
            Classes.RepeatMode.None => Classes.RepeatMode.All,
            Classes.RepeatMode.All => Classes.RepeatMode.One,
            Classes.RepeatMode.One => Classes.RepeatMode.None,
            _ => Classes.RepeatMode.None
        };
        _viewModel.RepeatMode = MusicPlayerService.Instance.RepeatMode;
        UpdateShuffleRepeatVisuals();
    }

    private void SeekSlider_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDraggingSeekbar = true;
    }

    private void SeekSlider_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDraggingSeekbar = false;
        if (MusicPlayerService.Instance.CurrentTrack != null)
        {
            MusicPlayerService.Instance.Seek(TimeSpan.FromSeconds(_viewModel.SeekValue));
        }
    }

    private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSeekbar)
        {
            _viewModel.CurrentTime = TimeSpan.FromSeconds(_viewModel.SeekValue).ToString(@"m\:ss");
        }
    }

    private void MusicPlayerService_PlaybackError(object? sender, string message)
    {
        Dispatcher.Invoke(() =>
        {
            Wpf.Ui.Controls.MessageBox messageBox = new()
            {
                Title = Application.Current.TryFindResource("Edit_ErrorTitle")?.ToString() ?? "Error",
                Content = message,
                CloseButtonText = Application.Current.TryFindResource("General_Ok")?.ToString() ?? "OK"
            };
            _ = messageBox.ShowDialogAsync();
        });
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        MusicPlayerService.Instance.Volume = (float)VolumeSlider.Value;
    }

    private void ApplyAccentColor(SolidColorBrush? brush)
    {
        bool useAccent = SettingsManager.Current.UseAlbumArtAsAccentColor && brush != null;

        Dispatcher.Invoke(() =>
        {
            if (useAccent && brush != null)
            {
                // Play/Pause Button
                PlayPauseButton.Background = brush;
                
                // Icons
                PreviousIcon.Foreground = brush;
                NextIcon.Foreground = brush;
                
                // Seekbar
                SeekSlider.Foreground = VolumeSlider.Foreground = brush;

                // Shadow/Glow (inside template)
                var shadow = FindVisualChild<DropShadowEffect>(MainContentControl, "AlbumArtShadow");
                if (shadow != null)
                {
                    shadow.Color = brush.Color;
                    shadow.Opacity = 0.5;
                    shadow.BlurRadius = 60;
                    shadow.ShadowDepth = 0;
                }
                
                // Subtle backgrounds for other controls
                var subtleBrush = brush.Clone();
                subtleBrush.Opacity = 0.15;
                subtleBrush.Freeze();
                ShuffleButton.Background = RepeatButton.Background = subtleBrush;
            }
            else
            {
                // Reset
                PlayPauseButton.ClearValue(BackgroundProperty);
                PreviousIcon.ClearValue(ForegroundProperty);
                NextIcon.ClearValue(ForegroundProperty);
                SeekSlider.ClearValue(ForegroundProperty);
                ShuffleButton.ClearValue(BackgroundProperty);
                RepeatButton.ClearValue(BackgroundProperty);

                var shadow = FindVisualChild<DropShadowEffect>(MainContentControl, "AlbumArtShadow");
                if (shadow != null)
                {
                    shadow.Color = Colors.Black;
                    shadow.Opacity = 0.4;
                    shadow.BlurRadius = 40;
                    shadow.ShadowDepth = 12;
                }
            }
            
            // Re-run visuals to ensure active states are correct
            UpdateShuffleRepeatVisuals();
        });
    }

    private void UpdateBackgroundBlur()
    {
        var blurOption = SettingsManager.Current.MediaFlyoutBackgroundBlur;
        
        if (blurOption == 0) // None
        {
            BackgroundBlurImage.Visibility = Visibility.Collapsed;
            return;
        }

        BackgroundBlurImage.Visibility = Visibility.Visible;
        
        if (BackgroundBlurImage.Effect is BlurEffect blurEffect)
        {
            switch (blurOption)
            {
                case 1: // Style 1: Heavy Blur (from MainWindow)
                    BackgroundBlurImage.Opacity = 0.6;
                    blurEffect.Radius = 150;
                    break;
                case 2: // Style 2: Medium Blur
                    BackgroundBlurImage.Opacity = 0.45;
                    blurEffect.Radius = 100;
                    break;
                case 3: // Style 3: Subtle Blur
                    BackgroundBlurImage.Opacity = 0.3;
                    blurEffect.Radius = 40;
                    break;
                case 4: // Glassmorphism / Acrylic (if any)
                default:
                    BackgroundBlurImage.Opacity = 0.4;
                    blurEffect.Radius = 100;
                    break;
            }
        }
    }

}
