using System;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentFlyoutWPF.Models;

public partial class TrackModel : ObservableObject
{
    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullArtistDisplay))]
    private string artist = "Unknown Artist";
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullArtistDisplay))]
    private string collaborators = string.Empty;

    [ObservableProperty]
    private string album = "Unknown Album";

    [ObservableProperty]
    private TimeSpan duration;

    [ObservableProperty]
    private string filePath = string.Empty;

    [ObservableProperty]
    private string? albumArtPath;

    [ObservableProperty]
    private BitmapImage? albumArt;
    
    // For storing the raw cover data or generic representation if needed
    public byte[]? AlbumArtData { get; set; }

    [ObservableProperty]
    private int trackNumber;

    [ObservableProperty]
    private string genre = string.Empty;

    [ObservableProperty]
    private string coverUrl = string.Empty;

    [ObservableProperty]
    private string lyrics = string.Empty;

    [ObservableProperty]
    private bool hasLyrics;

    partial void OnLyricsChanged(string value)
    {
        HasLyrics = !string.IsNullOrWhiteSpace(value);
    }

    [ObservableProperty]
    private int playCount;

    public string FullArtistDisplay => string.IsNullOrWhiteSpace(Collaborators) ? Artist : $"{Artist} (feat. {Collaborators})";
}

public partial class LibraryArtist : ObservableObject
{
    [ObservableProperty]
    private string name = "Unknown Artist";

    public System.Collections.Generic.List<TrackModel> Songs { get; set; } = new();

    [ObservableProperty]
    private BitmapImage? image;

    [ObservableProperty]
    private string? artPath;
}

public partial class LibraryAlbum : ObservableObject
{
    [ObservableProperty]
    private string title = "Unknown Album";

    [ObservableProperty]
    private string artist = "Unknown Artist";

    public System.Collections.Generic.List<TrackModel> Songs { get; set; } = new();

    [ObservableProperty]
    private BitmapImage? coverArt;

    [ObservableProperty]
    private string? artPath;
}
