using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using System.Reflection;
using System.Windows.Media;
using Xunit;

namespace FluentFlyout.Tests;

[CollectionDefinition("AccentColorResolver", DisableParallelization = true)]
public sealed class AccentColorResolverCollectionDefinition
{
}

[Collection("AccentColorResolver")]
public class AccentColorResolverTests
{
    [Fact]
    public void ResolveAccentBrush_AlbumArtActive_WinsOverCustom()
    {
        var previousSettings = SettingsManager.Current;
        try
        {
            SettingsManager.Current = new UserSettings
            {
                UseAlbumArtAsAccentColor = true,
                UseCustomAccentColor = true,
                CustomAccentColorHex = "#FF5733"
            };

            SetBitmapHelperHasAlbumArt(true);

            var albumBrush = CreateBrush("#123456");

            var resolved = AccentColorResolver.ResolveAccentBrush(albumBrush);

            Assert.Equal(AccentColorSource.AlbumArt, AccentColorResolver.ResolveAccentSource(albumBrush));
            Assert.Equal(albumBrush.Color, resolved.Color);
        }
        finally
        {
            SetBitmapHelperHasAlbumArt(false);
            SettingsManager.Current = previousSettings;
        }
    }

    [Fact]
    public void ResolveAccentBrush_AlbumArtUnavailable_UsesCustom()
    {
        var previousSettings = SettingsManager.Current;
        try
        {
            SettingsManager.Current = new UserSettings
            {
                UseAlbumArtAsAccentColor = true,
                UseCustomAccentColor = true,
                CustomAccentColorHex = "#FF5733"
            };

            SetBitmapHelperHasAlbumArt(false);

            var resolved = AccentColorResolver.ResolveAccentBrush();

            Assert.Equal(AccentColorSource.Custom, AccentColorResolver.ResolveAccentSource());
            Assert.Equal(CreateBrush("#FF5733").Color, resolved.Color);
        }
        finally
        {
            SettingsManager.Current = previousSettings;
        }
    }

    [Fact]
    public void ResolveAccentBrush_InvalidCustom_FallsBackToNeutral()
    {
        var previousSettings = SettingsManager.Current;
        try
        {
            SettingsManager.Current = new UserSettings
            {
                UseAlbumArtAsAccentColor = false,
                UseCustomAccentColor = true,
                CustomAccentColorHex = "not-a-color"
            };

            SetBitmapHelperHasAlbumArt(false);

            var resolved = AccentColorResolver.ResolveAccentBrush();
            var neutral = ThemeResourceHelper.GetSecondaryTextSolidBrush();

            Assert.Equal(AccentColorSource.Neutral, AccentColorResolver.ResolveAccentSource());
            Assert.Equal(neutral.Color, resolved.Color);
        }
        finally
        {
            SettingsManager.Current = previousSettings;
        }
    }

    [Fact]
    public void ResolveAccentBrush_NoAccentOptions_ReturnsNeutral()
    {
        var previousSettings = SettingsManager.Current;
        try
        {
            SettingsManager.Current = new UserSettings
            {
                UseAlbumArtAsAccentColor = false,
                UseCustomAccentColor = false,
                CustomAccentColorHex = "#00FF00"
            };

            SetBitmapHelperHasAlbumArt(false);

            var resolved = AccentColorResolver.ResolveAccentBrush();
            var neutral = ThemeResourceHelper.GetSecondaryTextSolidBrush();

            Assert.Equal(AccentColorSource.Neutral, AccentColorResolver.ResolveAccentSource());
            Assert.Equal(neutral.Color, resolved.Color);
        }
        finally
        {
            SettingsManager.Current = previousSettings;
        }
    }

    [Fact]
    public void ResolveAccentBrush_AlbumArtEnabledWithoutAlbumArt_NeverReturnsSystemBlueFallback()
    {
        var previousSettings = SettingsManager.Current;
        try
        {
            SettingsManager.Current = new UserSettings
            {
                UseAlbumArtAsAccentColor = true,
                UseCustomAccentColor = false
            };

            SetBitmapHelperHasAlbumArt(false);

            var resolved = AccentColorResolver.ResolveAccentBrush();

            Assert.Equal(AccentColorSource.Neutral, AccentColorResolver.ResolveAccentSource());
            Assert.NotEqual(Colors.RoyalBlue, resolved.Color);
            Assert.NotEqual(Colors.DeepSkyBlue, resolved.Color);
            Assert.NotEqual(Colors.DodgerBlue, resolved.Color);
        }
        finally
        {
            SettingsManager.Current = previousSettings;
        }
    }

    [Theory]
    [InlineData("#FF5733")]
    [InlineData("#CCFF5733")]
    public void TryParseCustomAccent_ValidHex_ReturnsTrue(string hex)
    {
        var parsed = AccentColorResolver.TryParseCustomAccent(hex, out var brush);

        Assert.True(parsed);
        Assert.NotNull(brush);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("#XYZ123")]
    [InlineData("#1234567")]
    public void TryParseCustomAccent_InvalidHex_ReturnsFalse(string hex)
    {
        var parsed = AccentColorResolver.TryParseCustomAccent(hex, out var brush);

        Assert.False(parsed);
        Assert.Null(brush);
    }

    private static SolidColorBrush CreateBrush(string hex)
        => new((Color)ColorConverter.ConvertFromString(hex)!);

    private static void SetBitmapHelperHasAlbumArt(bool hasAlbumArt)
    {
        var bitmapHelperType = typeof(SettingsManager).Assembly.GetType("FluentFlyout.Classes.Utils.BitmapHelper", throwOnError: true)!;
        var currentHashCodeField = bitmapHelperType.GetField("_currentHashCode", BindingFlags.NonPublic | BindingFlags.Static)!;
        var currentHashCodeContextField = bitmapHelperType.GetField("_currentHashCodeContext", BindingFlags.NonPublic | BindingFlags.Static)!;

        currentHashCodeField.SetValue(null, hasAlbumArt ? 1 : 0);

        var asyncLocal = currentHashCodeContextField.GetValue(null)!;
        var valueProperty = asyncLocal.GetType().GetProperty("Value")!;
        valueProperty.SetValue(asyncLocal, hasAlbumArt ? 1 : 0);
    }
}
