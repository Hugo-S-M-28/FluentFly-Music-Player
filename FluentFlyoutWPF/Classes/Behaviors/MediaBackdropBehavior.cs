using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyoutWPF.Classes.Utils;

namespace FluentFlyoutWPF.Classes.Behaviors;

public static class MediaBackdropBehavior
{
    private const double SizeRefreshThreshold = 8.0;

    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.RegisterAttached(
            "Source",
            typeof(ImageSource),
            typeof(MediaBackdropBehavior),
            new PropertyMetadata(null, OnBackdropPropertyChanged));

    public static readonly DependencyProperty PresetProperty =
        DependencyProperty.RegisterAttached(
            "Preset",
            typeof(int),
            typeof(MediaBackdropBehavior),
            new PropertyMetadata(0, OnBackdropPropertyChanged));

    public static readonly DependencyProperty SurfaceProperty =
        DependencyProperty.RegisterAttached(
            "Surface",
            typeof(MediaBackdropSurface),
            typeof(MediaBackdropBehavior),
            new PropertyMetadata(MediaBackdropSurface.Flyout, OnBackdropPropertyChanged));

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(MediaBackdropBehavior),
            new PropertyMetadata(true, OnBackdropPropertyChanged));

    private static readonly DependencyProperty LastRenderWidthProperty =
        DependencyProperty.RegisterAttached(
            "LastRenderWidth",
            typeof(double),
            typeof(MediaBackdropBehavior),
            new PropertyMetadata(0.0));

    private static readonly DependencyProperty LastRenderHeightProperty =
        DependencyProperty.RegisterAttached(
            "LastRenderHeight",
            typeof(double),
            typeof(MediaBackdropBehavior),
            new PropertyMetadata(0.0));

    public static ImageSource? GetSource(DependencyObject obj) =>
        (ImageSource?)obj.GetValue(SourceProperty);

    public static void SetSource(DependencyObject obj, ImageSource? value) =>
        obj.SetValue(SourceProperty, value);

    public static int GetPreset(DependencyObject obj) =>
        (int)obj.GetValue(PresetProperty);

    public static void SetPreset(DependencyObject obj, int value) =>
        obj.SetValue(PresetProperty, value);

    public static MediaBackdropSurface GetSurface(DependencyObject obj) =>
        (MediaBackdropSurface)obj.GetValue(SurfaceProperty);

    public static void SetSurface(DependencyObject obj, MediaBackdropSurface value) =>
        obj.SetValue(SurfaceProperty, value);

    public static bool GetIsEnabled(DependencyObject obj) =>
        (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) =>
        obj.SetValue(IsEnabledProperty, value);

    public static void Refresh(Image image)
    {
        Apply(image, force: true);
    }

    private static readonly DependencyProperty ActiveDownloadingImageProperty =
        DependencyProperty.RegisterAttached(
            "ActiveDownloadingImage",
            typeof(BitmapImage),
            typeof(MediaBackdropBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty DownloadCompletedHandlerProperty =
        DependencyProperty.RegisterAttached(
            "DownloadCompletedHandler",
            typeof(EventHandler),
            typeof(MediaBackdropBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty DownloadFailedHandlerProperty =
        DependencyProperty.RegisterAttached(
            "DownloadFailedHandler",
            typeof(EventHandler<ExceptionEventArgs>),
            typeof(MediaBackdropBehavior),
            new PropertyMetadata(null));

    private static void OnBackdropPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image image)
        {
            EnsureLayoutSubscriptions(image);

            // Clean up any old subscription
            var oldImage = (BitmapImage?)image.GetValue(ActiveDownloadingImageProperty);
            if (oldImage != null)
            {
                var oldCompleted = (EventHandler?)image.GetValue(DownloadCompletedHandlerProperty);
                var oldFailed = (EventHandler<ExceptionEventArgs>?)image.GetValue(DownloadFailedHandlerProperty);
                if (oldCompleted != null)
                {
                    oldImage.DownloadCompleted -= oldCompleted;
                }
                if (oldFailed != null)
                {
                    oldImage.DownloadFailed -= oldFailed;
                }
                image.ClearValue(ActiveDownloadingImageProperty);
                image.ClearValue(DownloadCompletedHandlerProperty);
                image.ClearValue(DownloadFailedHandlerProperty);
            }

            // Set up new subscription if the new source is downloading
            var newSource = GetSource(image) as BitmapImage;
            if (newSource != null && newSource.IsDownloading)
            {
                // Create handlers capturing the 'image' element
                EventHandler? completedHandler = null;
                EventHandler<ExceptionEventArgs>? failedHandler = null;

                completedHandler = (sender, args) =>
                {
                    var bi = (BitmapImage)sender!;
                    var activeCompleted = (EventHandler?)image.GetValue(DownloadCompletedHandlerProperty);
                    var activeFailed = (EventHandler<ExceptionEventArgs>?)image.GetValue(DownloadFailedHandlerProperty);
                    if (activeCompleted != null) bi.DownloadCompleted -= activeCompleted;
                    if (activeFailed != null) bi.DownloadFailed -= activeFailed;
                    image.ClearValue(ActiveDownloadingImageProperty);
                    image.ClearValue(DownloadCompletedHandlerProperty);
                    image.ClearValue(DownloadFailedHandlerProperty);

                    image.Dispatcher.Invoke(() => Apply(image, force: true));
                };

                failedHandler = (sender, args) =>
                {
                    var bi = (BitmapImage)sender!;
                    var activeCompleted = (EventHandler?)image.GetValue(DownloadCompletedHandlerProperty);
                    var activeFailed = (EventHandler<ExceptionEventArgs>?)image.GetValue(DownloadFailedHandlerProperty);
                    if (activeCompleted != null) bi.DownloadCompleted -= activeCompleted;
                    if (activeFailed != null) bi.DownloadFailed -= activeFailed;
                    image.ClearValue(ActiveDownloadingImageProperty);
                    image.ClearValue(DownloadCompletedHandlerProperty);
                    image.ClearValue(DownloadFailedHandlerProperty);

                    image.Dispatcher.Invoke(() => Apply(image, force: true));
                };

                image.SetValue(ActiveDownloadingImageProperty, newSource);
                image.SetValue(DownloadCompletedHandlerProperty, completedHandler);
                image.SetValue(DownloadFailedHandlerProperty, failedHandler);

                newSource.DownloadCompleted += completedHandler;
                newSource.DownloadFailed += failedHandler;
            }

            Apply(image, force: true);
        }
    }

    private static void EnsureLayoutSubscriptions(Image image)
    {
        image.Loaded -= ImageOnLoaded;
        image.Loaded += ImageOnLoaded;
        image.SizeChanged -= ImageOnSizeChanged;
        image.SizeChanged += ImageOnSizeChanged;
    }

    private static void ImageOnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            Apply(image, force: true);
        }
    }

    private static void ImageOnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is Image image)
        {
            Apply(image, force: false);
        }
    }

    private static void Apply(Image image, bool force)
    {
        if (!GetIsEnabled(image))
        {
            image.Source = null;
            MediaBackdropStyleService.ApplyPresetToImageStyle(image, 0, GetSurface(image));
            return;
        }

        var preset = GetPreset(image);
        if (preset == 0)
        {
            image.Source = null;
            MediaBackdropStyleService.ApplyPresetToImageStyle(image, preset, GetSurface(image));
            return;
        }

        var (targetWidth, targetHeight) = ResolveTargetSize(image);
        if (!force && !HasSignificantSizeChange(image, targetWidth, targetHeight))
        {
            return;
        }

        SetLastRenderSize(image, targetWidth, targetHeight);

        image.Source = MediaBackdropStyleService.CreateBackdropImageSource(
            preset,
            GetSurface(image),
            GetSource(image),
            targetWidth,
            targetHeight);
        MediaBackdropStyleService.ApplyPresetToImageStyle(image, preset, GetSurface(image));
    }

    private static (double Width, double Height) ResolveTargetSize(Image image)
    {
        double width = image.ActualWidth;
        double height = image.ActualHeight;

        if (width <= 0 || double.IsNaN(width))
        {
            width = image.RenderSize.Width;
        }

        if (height <= 0 || double.IsNaN(height))
        {
            height = image.RenderSize.Height;
        }

        if ((width <= 0 || double.IsNaN(width)) && image.Width > 0 && !double.IsNaN(image.Width))
        {
            width = image.Width;
        }

        if ((height <= 0 || double.IsNaN(height)) && image.Height > 0 && !double.IsNaN(image.Height))
        {
            height = image.Height;
        }

        return (width, height);
    }

    private static bool HasSignificantSizeChange(Image image, double width, double height)
    {
        double previousWidth = (double)image.GetValue(LastRenderWidthProperty);
        double previousHeight = (double)image.GetValue(LastRenderHeightProperty);

        return Math.Abs(previousWidth - width) >= SizeRefreshThreshold ||
               Math.Abs(previousHeight - height) >= SizeRefreshThreshold;
    }

    private static void SetLastRenderSize(Image image, double width, double height)
    {
        image.SetValue(LastRenderWidthProperty, width);
        image.SetValue(LastRenderHeightProperty, height);
    }
}
