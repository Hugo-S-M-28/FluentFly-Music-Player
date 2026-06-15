using WindowsMediaController;
using FluentFlyout.Classes.Settings;
using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF.Classes;

public enum PlaybackSourceKind
{
    None,
    Internal,
    External
}

public sealed class ResolvedPlaybackSource
{
    public PlaybackSourceKind Kind { get; }
    public MediaSession? ExternalSession { get; }

    public ResolvedPlaybackSource(PlaybackSourceKind kind, MediaSession? externalSession = null)
    {
        Kind = kind;
        ExternalSession = externalSession;
    }
}

public static class PlaybackSourceResolver
{
    public static ResolvedPlaybackSource Resolve()
    {
        bool internalEnabled = SettingsManager.Current.InternalPlayerEnabled;
        bool internalIsPlaying = internalEnabled && MusicPlayerService.Instance.IsPlaying;
        bool internalHasTrack = internalEnabled && MusicPlayerService.Instance.CurrentTrack != null;

        MediaSession? externalSession = ExternalMediaService.Instance.GetPreferredSession();
        bool externalExists = externalSession != null;
        bool externalIsPlaying = ExternalMediaService.Instance.IsSessionPlaying(externalSession);

        PlaybackSourceKind kind = ResolveKind(
            internalEnabled,
            internalIsPlaying,
            internalHasTrack,
            externalExists,
            externalIsPlaying);

        return kind == PlaybackSourceKind.External
            ? new ResolvedPlaybackSource(kind, externalSession)
            : new ResolvedPlaybackSource(kind);
    }

    internal static PlaybackSourceKind ResolveKind(
        bool internalEnabled,
        bool internalIsPlaying,
        bool internalHasTrack,
        bool externalExists,
        bool externalIsPlaying)
    {
        if (internalEnabled && internalIsPlaying)
        {
            return PlaybackSourceKind.Internal;
        }

        if (externalExists && externalIsPlaying)
        {
            return PlaybackSourceKind.External;
        }

        if (internalEnabled && internalHasTrack)
        {
            return PlaybackSourceKind.Internal;
        }

        if (externalExists)
        {
            return PlaybackSourceKind.External;
        }

        return PlaybackSourceKind.None;
    }
}
