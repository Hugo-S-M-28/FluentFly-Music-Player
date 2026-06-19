using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using Xunit;

namespace FluentFlyout.Tests;

public class PlaybackAccentColorServiceTests
{
    private void RunInStaThread(Action action)
    {
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                throw new Exception("Error on STA thread", ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static BitmapImage CreateTestBitmapImage(Color startColor, Color endColor)
    {
        const int width = 96;
        const int height = 96;
        byte[] pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                double t = x / (double)(width - 1);
                byte r = (byte)(startColor.R + ((endColor.R - startColor.R) * t));
                byte g = (byte)(startColor.G + ((endColor.G - startColor.G) * t));
                byte b = (byte)(startColor.B + ((endColor.B - startColor.B) * t));
                int offset = ((y * width) + x) * 4;
                pixels[offset] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    [Fact]
    public void ResolveReadableAccentBrush_DarkTheme_EnforcesLegibilityForVeryDarkColor()
    {
        RunInStaThread(() =>
        {
            var darkColor = (Color)ColorConverter.ConvertFromString("#020202");
            var brush = new SolidColorBrush(darkColor);
            brush.Freeze();

            var settings = SettingsManager.Current;
            var previousSettings = SettingsManager.Current;
            try
            {
                SettingsManager.Current = new UserSettings
                {
                    UseAlbumArtAsAccentColor = true,
                    UseCustomAccentColor = false
                };

                // Inject active album art brush
                BitmapHelper.SetHasAlbumArt(true);
                BitmapHelper.SetSavedDominantColors([brush]);

                // Act - dark theme
                var resolved = AccentColorResolver.ResolveReadableAccentBrush(brush, isDarkTheme: true);

                // Assert: Very dark color should fall back to White
                Assert.Equal(Colors.White, resolved.Color);
            }
            finally
            {
                BitmapHelper.SetHasAlbumArt(false);
                BitmapHelper.SetSavedDominantColors([]);
                SettingsManager.Current = previousSettings;
            }
        });
    }

    [Fact]
    public void ResolveReadableAccentBrush_LightTheme_EnforcesLegibilityForVeryLightColor()
    {
        RunInStaThread(() =>
        {
            var lightColor = (Color)ColorConverter.ConvertFromString("#FDFDFD");
            var brush = new SolidColorBrush(lightColor);
            brush.Freeze();

            var previousSettings = SettingsManager.Current;
            try
            {
                SettingsManager.Current = new UserSettings
                {
                    UseAlbumArtAsAccentColor = true,
                    UseCustomAccentColor = false
                };

                BitmapHelper.SetHasAlbumArt(true);
                BitmapHelper.SetSavedDominantColors([brush]);

                // Act - light theme
                var resolved = AccentColorResolver.ResolveReadableAccentBrush(brush, isDarkTheme: false);

                // Assert: Very light color should fall back to Dark (18, 18, 18)
                Assert.Equal(Color.FromRgb(18, 18, 18), resolved.Color);
            }
            finally
            {
                BitmapHelper.SetHasAlbumArt(false);
                BitmapHelper.SetSavedDominantColors([]);
                SettingsManager.Current = previousSettings;
            }
        });
    }

    [Fact]
    public async Task ProcessPlaybackAccentColorAsync_CancelsObsoleteRequest()
    {
        var service = PlaybackAccentColorService.Instance;
        BitmapImage obsoleteImage = null!;
        BitmapImage currentImage = null!;

        RunInStaThread(() =>
        {
            obsoleteImage = CreateTestBitmapImage(Colors.Red, Colors.Goldenrod);
            currentImage = CreateTestBitmapImage(Colors.DeepSkyBlue, Colors.MediumPurple);
        });

        // Start request 1 (obsolete)
        var task1 = service.ProcessPlaybackAccentColorAsync(
            PlaybackSourceKind.Internal,
            "InternalPlayer",
            "ObsoleteTitle",
            "Artist1",
            null,
            obsoleteImage);

        // Immediately start request 2 (current)
        var task2 = service.ProcessPlaybackAccentColorAsync(
            PlaybackSourceKind.Internal,
            "InternalPlayer",
            "CurrentTitle",
            "Artist1",
            null,
            currentImage);

        var result1 = await task1;
        var result2 = await task2;

        // Assert: Result 1 is discarded/obsolete
        Assert.False(result1.IsCurrent);
        Assert.False(result1.HasReliableAlbumArtAccent);
        Assert.Null(result1.AccentBrush);

        // Result 2 belongs to the latest request and may safely drive the UI.
        Assert.True(result2.IsCurrent);
        Assert.True(result2.HasReliableAlbumArtAccent);
        Assert.NotNull(result2.AccentBrush);
    }

    [Fact]
    public async Task ProcessPlaybackAccentColorAsync_NullExternalThumbnailClearsOldAlbumArtAccent()
    {
        var service = PlaybackAccentColorService.Instance;
        var staleBrush = new SolidColorBrush(Colors.Magenta);
        staleBrush.Freeze();
        BitmapHelper.SetSavedDominantColors([staleBrush]);
        BitmapHelper.SetHasAlbumArt(true);

        var result = await service.ProcessPlaybackAccentColorAsync(
            PlaybackSourceKind.External,
            "ExternalSession",
            "Track Without Art",
            "Artist",
            null,
            null);

        Assert.True(result.IsCurrent);
        Assert.False(result.HasReliableAlbumArtAccent);
        Assert.Null(result.AccentBrush);
        Assert.False(BitmapHelper.HasAlbumArt);
        Assert.Empty(BitmapHelper.SavedDominantColors);
    }

    [Fact]
    public void BitmapHelper_IsReliableAlbumArt_IdentifiesSmallAndNullImages()
    {
        RunInStaThread(() =>
        {
            Assert.False(BitmapHelper.IsReliableAlbumArt(null));

            // Create a small 10x10 bitmap image
            var smallBmp = new WriteableBitmap(10, 10, 96, 96, PixelFormats.Bgra32, null);
            Assert.False(BitmapHelper.IsReliableAlbumArt(smallBmp));

            // Create a normal 100x100 bitmap image
            var normalBmp = new WriteableBitmap(100, 100, 96, 96, PixelFormats.Bgra32, null);
            Assert.True(BitmapHelper.IsReliableAlbumArt(normalBmp));
        });
    }

    [Fact]
    public void BitmapHelper_IsNearlyFlat_IdentifiesFlatImages()
    {
        int width = 8;
        int height = 8;
        byte[] flatPixels = new byte[width * height * 4];
        for (int i = 0; i < flatPixels.Length; i += 4)
        {
            flatPixels[i] = 100;     // B
            flatPixels[i + 1] = 100; // G
            flatPixels[i + 2] = 100; // R
            flatPixels[i + 3] = 255; // A
        }

        Assert.True(BitmapHelper.IsNearlyFlat(flatPixels, width, height));

        // Create non-flat pixels (one pixel is significantly different)
        byte[] variedPixels = new byte[width * height * 4];
        Array.Copy(flatPixels, variedPixels, flatPixels.Length);
        variedPixels[0] = 250; // blue channel varied
        variedPixels[1] = 0;   // green channel varied
        variedPixels[2] = 0;   // red channel varied

        Assert.False(BitmapHelper.IsNearlyFlat(variedPixels, width, height));
    }
}
