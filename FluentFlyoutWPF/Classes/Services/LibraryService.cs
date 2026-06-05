using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Classes.Services;

public sealed class LibraryService : ILibraryService
{
    private LibraryManager Manager => LibraryManager.Instance;

    public IReadOnlyList<TrackModel> Tracks => Manager.Tracks;
    public IReadOnlyList<LibraryAlbum> Albums => Manager.Albums;
    public IReadOnlyList<LibraryArtist> Artists => Manager.Artists;

    public event EventHandler<TrackModel>? TrackMetadataUpdated
    {
        add => Manager.TrackMetadataUpdated += value;
        remove => Manager.TrackMetadataUpdated -= value;
    }

    public Task InitializeAsync()
    {
        return Manager.InitializeAsync();
    }

    public Task ScanLibraryAsync()
    {
        return Manager.ScanLibraryAsync();
    }
}
