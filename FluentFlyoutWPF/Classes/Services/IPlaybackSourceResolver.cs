using FluentFlyoutWPF.Classes;

namespace FluentFlyoutWPF.Classes.Services;

public interface IPlaybackSourceResolver
{
    ResolvedPlaybackSource Resolve();
}
