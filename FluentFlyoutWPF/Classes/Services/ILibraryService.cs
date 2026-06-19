using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Classes.Services;

public interface ILibraryService
{
    IReadOnlyList<TrackModel> Tracks { get; }
    IReadOnlyList<LibraryAlbum> Albums { get; }
    IReadOnlyList<LibraryArtist> Artists { get; }
    event EventHandler<TrackModel>? TrackMetadataUpdated;
    Task InitializeAsync();
    Task ScanLibraryAsync();
    System.Windows.Media.Imaging.BitmapImage? GetAlbumArt(TrackModel track, int size);
}
