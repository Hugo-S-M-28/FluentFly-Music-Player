using static WindowsMediaController.MediaManager;

namespace FluentFlyoutWPF.Classes.Services;

public sealed class PlaybackSourceResolverService : IPlaybackSourceResolver
{
    private readonly ISettingsService _settingsService;
    private readonly IPlaybackService _playbackService;
    private readonly IExternalMediaService _externalMediaService;

    public PlaybackSourceResolverService(
        ISettingsService settingsService,
        IPlaybackService playbackService,
        IExternalMediaService externalMediaService)
    {
        _settingsService = settingsService;
        _playbackService = playbackService;
        _externalMediaService = externalMediaService;
    }

    public ResolvedPlaybackSource Resolve()
    {
        bool internalEnabled = _settingsService.Current.InternalPlayerEnabled;
        bool internalIsPlaying = internalEnabled && _playbackService.IsPlaying;
        bool internalHasTrack = internalEnabled && _playbackService.CurrentTrack != null;

        MediaSession? externalSession = _externalMediaService.GetPreferredSession();
        bool externalExists = externalSession != null;
        bool externalIsPlaying = _externalMediaService.IsSessionPlaying(externalSession);

        PlaybackSourceKind kind = PlaybackSourceResolver.ResolveKind(
            internalEnabled,
            internalIsPlaying,
            internalHasTrack,
            externalExists,
            externalIsPlaying);

        return kind == PlaybackSourceKind.External
            ? new ResolvedPlaybackSource(kind, externalSession)
            : new ResolvedPlaybackSource(kind);
    }
}
