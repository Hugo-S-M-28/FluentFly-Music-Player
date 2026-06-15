using FluentFlyoutWPF.Classes;
using Windows.Media.Control;
using Xunit;

namespace FluentFlyout.Tests;

public class ExternalPlaybackSelectionTests
{
    [Fact]
    public void OwnSessionId_DetectsStandaloneProcessName()
    {
        Assert.True(ExternalMediaService.IsOwnSessionId("FluentFlyout.exe"));
    }

    [Fact]
    public void OwnSessionId_DetectsMsixAumid()
    {
        Assert.True(ExternalMediaService.IsOwnSessionId("FluentFlyoutAuthors.FluentFlyout!App"));
    }

    [Fact]
    public void SessionScore_PrefersPlayingSessionOverPausedFocusedSession()
    {
        int playingScore = ExternalMediaService.CalculateSessionScore(
            "Spotify",
            isFocused: false,
            playbackStatus: GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            canPause: true,
            canPlay: false,
            canNext: true,
            canPrevious: true);

        int pausedFocusedScore = ExternalMediaService.CalculateSessionScore(
            "Chrome",
            isFocused: true,
            playbackStatus: GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,
            canPause: false,
            canPlay: true,
            canNext: true,
            canPrevious: true);

        Assert.True(playingScore > pausedFocusedScore);
    }

    [Fact]
    public void SessionScore_UsesFocusAsTieBreaker()
    {
        int focusedScore = ExternalMediaService.CalculateSessionScore(
            "Focused",
            isFocused: true,
            playbackStatus: GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,
            canPause: false,
            canPlay: true,
            canNext: true,
            canPrevious: true);

        int unfocusedScore = ExternalMediaService.CalculateSessionScore(
            "Unfocused",
            isFocused: false,
            playbackStatus: GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused,
            canPause: false,
            canPlay: true,
            canNext: true,
            canPrevious: true);

        Assert.True(focusedScore > unfocusedScore);
    }

    [Fact]
    public void ResolveKind_PrefersExternalWhenInternalIsPausedButExternalIsPlaying()
    {
        PlaybackSourceKind kind = PlaybackSourceResolver.ResolveKind(
            internalEnabled: true,
            internalIsPlaying: false,
            internalHasTrack: true,
            externalExists: true,
            externalIsPlaying: true);

        Assert.Equal(PlaybackSourceKind.External, kind);
    }

    [Fact]
    public void ResolveKind_PrefersInternalWhenItIsActivelyPlaying()
    {
        PlaybackSourceKind kind = PlaybackSourceResolver.ResolveKind(
            internalEnabled: true,
            internalIsPlaying: true,
            internalHasTrack: true,
            externalExists: true,
            externalIsPlaying: true);

        Assert.Equal(PlaybackSourceKind.Internal, kind);
    }
}
