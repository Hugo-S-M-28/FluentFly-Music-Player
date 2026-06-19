using System;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyoutWPF.Classes.Utils;
using Xunit;

namespace FluentFlyout.Tests;

public class MediaBackdropStyleServiceTests
{
    [Fact]
    public void CreateBackdropImageSource_WithOptionIndexZero_ReturnsNull()
    {
        var result = MediaBackdropStyleService.CreateBackdropImageSource(
            0,
            MediaBackdropSurface.Flyout,
            null,
            200,
            200);

        Assert.Null(result);
    }

    [Fact]
    public void CreateBackdropImageSource_WithNullImageSource_ReturnsFallback()
    {
        var result = MediaBackdropStyleService.CreateBackdropImageSource(
            1,
            MediaBackdropSurface.Flyout,
            null,
            200,
            200);

        Assert.NotNull(result);
    }

    [Fact]
    public void CreateBackdropImageSource_WithDownloadingImageSource_ReturnsFallback()
    {
        var bitmapImage = new BitmapImage();
        typeof(BitmapImage)
            .GetField("_isDownloading", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(bitmapImage, true);

        var result = MediaBackdropStyleService.CreateBackdropImageSource(
            1,
            MediaBackdropSurface.Flyout,
            bitmapImage,
            200,
            200);

        Assert.NotNull(result);
    }
}
