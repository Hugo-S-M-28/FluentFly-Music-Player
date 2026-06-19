using System.Reflection;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Models;
using NAudio.Wave;
using Xunit;

namespace FluentFlyout.Tests;

[CollectionDefinition("MusicPlayerServiceNextUp", DisableParallelization = true)]
public sealed class MusicPlayerServiceNextUpCollectionDefinition
{
}

[Collection("MusicPlayerServiceNextUp")]
public class MusicPlayerServiceNextUpTests
{
    private sealed class FakeWaveStream : WaveStream
    {
        private readonly WaveFormat _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        private long _position;

        public FakeWaveStream(TimeSpan duration, TimeSpan position)
        {
            Length = (long)(duration.TotalSeconds * _waveFormat.AverageBytesPerSecond);
            Position = (long)(position.TotalSeconds * _waveFormat.AverageBytesPerSecond);
        }

        public override WaveFormat WaveFormat => _waveFormat;
        public override long Length { get; }

        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, Length);
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;
    }

    [Fact]
    public void GetNextTrackPreview_ReturnsNextTrackInQueue()
    {
        var service = MusicPlayerService.Instance;
        var first = Track("First", "A", "first.mp3");
        var second = Track("Second", "A", "second.mp3");
        ConfigureQueue(service, [first, second], [], currentIndex: 0, shuffle: false, repeatMode: RepeatMode.None);

        Assert.Same(second, service.GetNextTrackPreview());
    }

    [Fact]
    public void GetNextTrackPreview_UsesShuffledQueueWhenShuffleIsEnabled()
    {
        var service = MusicPlayerService.Instance;
        var first = Track("First", "A", "first.mp3");
        var second = Track("Second", "A", "second.mp3");
        var third = Track("Third", "A", "third.mp3");
        ConfigureQueue(service, [first, second, third], [third, first, second], currentIndex: 1, shuffle: true, repeatMode: RepeatMode.None);

        Assert.Same(second, service.GetNextTrackPreview());
    }

    [Fact]
    public void GetNextTrackPreview_RepeatAllWrapsToFirstTrack()
    {
        var service = MusicPlayerService.Instance;
        var first = Track("First", "A", "first.mp3");
        var second = Track("Second", "A", "second.mp3");
        ConfigureQueue(service, [first, second], [], currentIndex: 1, shuffle: false, repeatMode: RepeatMode.All);

        Assert.Same(first, service.GetNextTrackPreview());
    }

    [Fact]
    public void GetNextTrackPreview_RepeatNoneAtQueueEndReturnsNull()
    {
        var service = MusicPlayerService.Instance;
        var first = Track("First", "A", "first.mp3");
        ConfigureQueue(service, [first], [], currentIndex: 0, shuffle: false, repeatMode: RepeatMode.None);

        Assert.Null(service.GetNextTrackPreview());
    }

    [Fact]
    public void GetNextTrackPreview_RepeatOneReturnsNull()
    {
        var service = MusicPlayerService.Instance;
        var first = Track("First", "A", "first.mp3");
        var second = Track("Second", "A", "second.mp3");
        ConfigureQueue(service, [first, second], [], currentIndex: 0, shuffle: false, repeatMode: RepeatMode.One);

        Assert.Null(service.GetNextTrackPreview());
    }

    [Fact]
    public void UpcomingTrackDue_FiresOnceInsideThreeSecondWindow()
    {
        var service = MusicPlayerService.Instance;
        var first = Track("First", "A", "first.mp3");
        var second = Track("Second", "A", "second.mp3");
        ConfigureQueue(service, [first, second], [], currentIndex: 0, shuffle: false, repeatMode: RepeatMode.None);
        SetPrivateField(service, "_currentTrack", first);
        SetPrivateField(service, "_isPlaying", true);
        SetPrivateField(service, "_audioStream", new FakeWaveStream(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(58)));
        SetPrivateField(service, "_lastFiredUpcomingTrackKey", string.Empty);

        int fireCount = 0;
        UpcomingTrackEventArgs? lastArgs = null;
        void Handler(object? sender, UpcomingTrackEventArgs args)
        {
            fireCount++;
            lastArgs = args;
        }

        service.UpcomingTrackDue += Handler;
        try
        {
            InvokePrivateMethod(service, "CheckUpcomingTrackDue");
            InvokePrivateMethod(service, "CheckUpcomingTrackDue");
        }
        finally
        {
            service.UpcomingTrackDue -= Handler;
            SetPrivateField<WaveStream?>(service, "_audioStream", null);
            SetPrivateField(service, "_isPlaying", false);
        }

        Assert.Equal(1, fireCount);
        Assert.Same(first, lastArgs?.CurrentTrack);
        Assert.Same(second, lastArgs?.UpcomingTrack);
    }

    private static TrackModel Track(string title, string artist, string fileName)
        => new()
        {
            Title = title,
            Artist = artist,
            FilePath = Path.Combine(Path.GetTempPath(), fileName)
        };

    private static void ConfigureQueue(
        MusicPlayerService service,
        List<TrackModel> originalQueue,
        List<TrackModel> shuffledQueue,
        int currentIndex,
        bool shuffle,
        RepeatMode repeatMode)
    {
        SetPrivateField(service, "_originalQueue", originalQueue);
        SetPrivateField(service, "_shuffledQueue", shuffledQueue);
        SetPrivateField(service, "_currentQueueIndex", currentIndex);
        SetPrivateField(service, "_isShuffleEnabled", shuffle);
        SetPrivateField(service, "_repeatMode", repeatMode);
        SetPrivateField(service, "_lastFiredUpcomingTrackKey", string.Empty);
    }

    private static void SetPrivateField<T>(MusicPlayerService service, string fieldName, T value)
    {
        var field = typeof(MusicPlayerService).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field '{fieldName}' was not found.");
        field.SetValue(service, value);
    }

    private static void InvokePrivateMethod(MusicPlayerService service, string methodName)
    {
        var method = typeof(MusicPlayerService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method '{methodName}' was not found.");
        method.Invoke(service, null);
    }
}
