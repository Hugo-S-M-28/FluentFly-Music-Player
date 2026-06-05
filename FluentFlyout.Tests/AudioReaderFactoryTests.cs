using FluentFlyoutWPF.Classes;
using Xunit;

namespace FluentFlyout.Tests;

public class AudioReaderFactoryTests
{
    [Theory]
    [InlineData("track.mp3")]
    [InlineData("track.mp2")]
    [InlineData("track.mp1")]
    [InlineData("track.wav")]
    [InlineData("track.wave")]
    [InlineData("track.aif")]
    [InlineData("track.aiff")]
    [InlineData("track.flac")]
    [InlineData("track.m4a")]
    [InlineData("track.m4b")]
    [InlineData("track.mp4")]
    [InlineData("track.aac")]
    [InlineData("track.wma")]
    [InlineData("track.asf")]
    [InlineData("track.ogg")]
    [InlineData("track.oga")]
    [InlineData("track.opus")]
    public void IsSupportedExtension_KnownStandardFormats_ReturnsTrue(string filePath)
    {
        Assert.True(AudioReaderFactory.IsSupportedExtension(filePath));
    }

    [Theory]
    [InlineData("track.txt")]
    [InlineData("track.jpg")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSupportedExtension_UnsupportedFormats_ReturnsFalse(string? filePath)
    {
        Assert.False(AudioReaderFactory.IsSupportedExtension(filePath));
    }
}
