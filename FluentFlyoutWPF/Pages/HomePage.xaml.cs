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
        bool useAccent = SettingsManager.Current.UseAlbumArtAsAccentColor && dominantBrush != null;
        
        var accentBrush = useAccent ? dominantBrush! : (Brush)Application.Current.TryFindResource("AccentFillColorDefaultBrush") ?? Brushes.DeepSkyBlue;
        var defaultBrush = Brushes.White;

        _viewModel.ApplyRepeatShuffleForegrounds(accentBrush, defaultBrush);
    }

    private void ApplyAccentColor(SolidColorBrush? brush)
    {
        bool useAccent = SettingsManager.Current.UseAlbumArtAsAccentColor && brush != null;

        Action action = () =>
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

        HomeCoverHostColumn.Width = new GridLength(1, GridUnitType.Star);
        HomeLyricsHostColumn.Width = new GridLength(lyricsWidthRatio, GridUnitType.Star);
        HomePlaylistHostColumn.Width = isPlaylistVisible
            ? new GridLength(380, GridUnitType.Pixel)
            : new GridLength(0, GridUnitType.Pixel);

        Grid.SetColumn(MainContentControl, 0);
        Grid.SetColumnSpan(MainContentControl, isPlaylistVisible ? 2 : 3);
        MainContentControl.Margin = isPlaylistVisible ? new Thickness(0, 0, 16, 0) : new Thickness(0);

        if (FindVisualChild<Grid>(MainContentControl, "NowPlayingLayoutGrid") is { ColumnDefinitions.Count: >= 2 } nowPlayingGrid)
        {
            nowPlayingGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            nowPlayingGrid.ColumnDefinitions[1].Width = new GridLength(lyricsWidthRatio, GridUnitType.Star);
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
