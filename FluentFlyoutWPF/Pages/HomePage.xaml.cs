using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;

namespace FluentFlyoutWPF.Pages;

public partial class HomePage : Page
{
    private readonly NowPlayingViewModel _viewModel;

    public HomePage() : this(App.GetRequiredService<NowPlayingViewModel>())
    {
    }

    public HomePage(NowPlayingViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;

        // Inicializar el contenido del reproductor con la vista de reproducción actual
        MainContentControl.Content = _viewModel;
        MainContentControl.ContentTemplate = (DataTemplate)MainContentControl.Resources["NowPlayingTemplate"];

        WeakReferenceMessenger.Default.Register<UpdateAccentColorMessage>(this, (r, m) =>
        {
            Dispatcher.InvokeAsync(() => ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault()));
        });

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(NowPlayingViewModel.IsShuffleEnabled) ||
                e.PropertyName == nameof(NowPlayingViewModel.RepeatMode))
            {
                Dispatcher.Invoke(UpdateShuffleRepeatVisuals);
            }
            else if (e.PropertyName == nameof(NowPlayingViewModel.BackdropImage))
            {
                Dispatcher.Invoke(UpdateBackgroundBlur);
            }
            else if (e.PropertyName == nameof(NowPlayingViewModel.IsPlaylistVisible))
            {
                Dispatcher.Invoke(UpdateMainContentLayout);
            }
        };
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        Visualizer.Instance.AddClient();
        VisualizerContainer.Source = Visualizer.Instance.Bitmap;

        SettingsManager.Current.PropertyChanged += Settings_PropertyChanged;
        LibraryManager.Instance.TrackMetadataUpdated += LibraryManager_TrackMetadataUpdated;

        _viewModel.RefreshFromServices();
        ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault());
        UpdateBackgroundBlur();
        UpdateMainContentLayout();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.PropertyChanged -= Settings_PropertyChanged;
        LibraryManager.Instance.TrackMetadataUpdated -= LibraryManager_TrackMetadataUpdated;

        Visualizer.Instance.RemoveClient();
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
            Dispatcher.Invoke(_viewModel.RefreshFromServices);
        }
    }

    private void UpdateShuffleRepeatVisuals()
    {
        var isShuffle = MusicPlayerService.Instance.IsShuffleEnabled;
        var mode = MusicPlayerService.Instance.RepeatMode;
        
        var dominantBrush = BitmapHelper.SavedDominantColors.FirstOrDefault();
        bool isDark = ThemeResourceHelper.IsDarkTheme();
        bool useAccent = AccentColorResolver.ShouldUseAccent(dominantBrush);
        
        var accentBrush = useAccent 
            ? AccentColorResolver.ResolveReadableAccentBrush(dominantBrush, isDark) 
            : (Brush)Application.Current.TryFindResource("AccentFillColorDefaultBrush") ?? Brushes.DeepSkyBlue;
        var defaultBrush = Brushes.White;

        _viewModel.ApplyRepeatShuffleForegrounds(accentBrush, defaultBrush);
    }

    private void ApplyAccentColor(SolidColorBrush? brush)
    {
        bool isDark = ThemeResourceHelper.IsDarkTheme();
        bool useAccent = AccentColorResolver.ShouldUseAccent(brush);
        var activeBrush = useAccent ? AccentColorResolver.ResolveReadableAccentBrush(brush, isDark) : null;

        Action action = () =>
        {
            if (useAccent && activeBrush != null)
            {
                var rawAccent = AccentColorResolver.ResolveAccentBrush(brush);
                // Play/Pause Button background gets raw accent, foreground gets contrast brush
                PlayPauseButton.Background = rawAccent;
                PlayPauseButton.Foreground = ThemeResourceHelper.GetContrastBrush(rawAccent.Color);
                
                // Icons
                PreviousIcon.Foreground = activeBrush;
                NextIcon.Foreground = activeBrush;
                
                // Seekbar
                SeekSlider.Foreground = VolumeSlider.Foreground = activeBrush;

                // Shadow/Glow (inside template)
                var shadow = FindVisualChild<DropShadowEffect>(MainContentControl, "AlbumArtShadow");
                if (shadow != null)
                {
                    shadow.Color = activeBrush.Color;
                    shadow.Opacity = 0.5;
                    shadow.BlurRadius = 60;
                    shadow.ShadowDepth = 0;
                }
                
                // Subtle backgrounds for other controls
                var subtleBrush = activeBrush.Clone();
                subtleBrush.Opacity = 0.15;
                subtleBrush.Freeze();
                ShuffleButton.Background = RepeatButton.Background = subtleBrush;
            }
            else
            {
                // Reset
                PlayPauseButton.ClearValue(BackgroundProperty);
                PlayPauseButton.ClearValue(ForegroundProperty);
                PreviousIcon.ClearValue(ForegroundProperty);
                NextIcon.ClearValue(ForegroundProperty);
                SeekSlider.ClearValue(ForegroundProperty);
                VolumeSlider.ClearValue(ForegroundProperty);
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

    private void UpdateBackgroundBlur()
    {
        var blurOption = SettingsManager.Current.MediaFlyoutBackgroundBlur;
        MediaBackdropStyleService.ApplyPresetToImage(
            BackgroundBlurImage,
            blurOption,
            MediaBackdropSurface.Home,
            BackgroundBlurImage.Source);
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateMainContentLayout();
    }

    private void UpdateMainContentLayout()
    {
        if (HomeCoverHostColumn == null || HomeLyricsHostColumn == null || HomePlaylistHostColumn == null || MainContentControl == null) return;
        
        bool isPlaylistVisible = _viewModel.IsPlaylistVisible;
        double lyricsWidthRatio = isPlaylistVisible ? 1.5 : 2.0;
        bool isCompact = ActualWidth < 768;

        if (isCompact)
        {
            HomeCoverHostColumn.Width = new GridLength(1, GridUnitType.Star);
            HomeLyricsHostColumn.Width = new GridLength(0, GridUnitType.Pixel);
            HomePlaylistHostColumn.Width = isPlaylistVisible
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0, GridUnitType.Pixel);

            Grid.SetColumn(MainContentControl, 0);
            Grid.SetColumnSpan(MainContentControl, isPlaylistVisible ? 1 : 3);
            MainContentControl.Margin = new Thickness(0);
        }
        else
        {
            HomeCoverHostColumn.Width = new GridLength(1, GridUnitType.Star);
            HomeLyricsHostColumn.Width = new GridLength(lyricsWidthRatio, GridUnitType.Star);
            HomePlaylistHostColumn.Width = isPlaylistVisible
                ? new GridLength(380, GridUnitType.Pixel)
                : new GridLength(0, GridUnitType.Pixel);

            Grid.SetColumn(MainContentControl, 0);
            Grid.SetColumnSpan(MainContentControl, isPlaylistVisible ? 2 : 3);
            MainContentControl.Margin = isPlaylistVisible ? new Thickness(0, 0, 16, 0) : new Thickness(0);
        }

        if (FindVisualChild<Grid>(MainContentControl, "NowPlayingLayoutGrid") is Grid nowPlayingGrid)
        {
            var coverSection = FindVisualChild<StackPanel>(nowPlayingGrid, "NowPlayingCoverSection");
            var lyricsPanel = FindVisualChild<Border>(nowPlayingGrid, "NowPlayingLyricsPanel");

            if (isCompact)
            {
                if (nowPlayingGrid.RowDefinitions.Count >= 2)
                {
                    nowPlayingGrid.RowDefinitions[0].Height = GridLength.Auto;
                    nowPlayingGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                }
                if (nowPlayingGrid.ColumnDefinitions.Count >= 2)
                {
                    nowPlayingGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                    nowPlayingGrid.ColumnDefinitions[1].Width = new GridLength(0, GridUnitType.Pixel);
                }

                if (coverSection != null)
                {
                    Grid.SetRow(coverSection, 0);
                    Grid.SetColumn(coverSection, 0);
                    Grid.SetColumnSpan(coverSection, 2);
                    coverSection.Margin = new Thickness(12, 8, 12, 12);
                }

                if (lyricsPanel != null)
                {
                    Grid.SetRow(lyricsPanel, 1);
                    Grid.SetColumn(lyricsPanel, 0);
                    Grid.SetColumnSpan(lyricsPanel, 2);
                    lyricsPanel.Margin = new Thickness(0, 8, 0, 0);
                    lyricsPanel.Padding = new Thickness(16);
                    lyricsPanel.MinHeight = 0;
                }
            }
            else
            {
                if (nowPlayingGrid.RowDefinitions.Count >= 2)
                {
                    nowPlayingGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                    nowPlayingGrid.RowDefinitions[1].Height = new GridLength(0, GridUnitType.Pixel);
                }
                if (nowPlayingGrid.ColumnDefinitions.Count >= 2)
                {
                    nowPlayingGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                    nowPlayingGrid.ColumnDefinitions[1].Width = new GridLength(lyricsWidthRatio, GridUnitType.Star);
                }

                if (coverSection != null)
                {
                    Grid.SetRow(coverSection, 0);
                    Grid.SetColumn(coverSection, 0);
                    Grid.SetColumnSpan(coverSection, 1);
                    coverSection.Margin = new Thickness(16, 16, 24, 16);
                }

                if (lyricsPanel != null)
                {
                    Grid.SetRow(lyricsPanel, 0);
                    Grid.SetColumn(lyricsPanel, 1);
                    Grid.SetColumnSpan(lyricsPanel, 1);
                    lyricsPanel.Margin = new Thickness(0);
                    lyricsPanel.Padding = new Thickness(28);
                    lyricsPanel.MinHeight = 420;
                }
            }
        }
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
}
