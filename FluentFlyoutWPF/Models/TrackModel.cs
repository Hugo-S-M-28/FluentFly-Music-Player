using System;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FluentFlyoutWPF.Models;

public partial class TrackModel : ObservableObject
{
    private string _searchIndex = string.Empty;

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

    [ObservableProperty]
    private long fileModifiedUtcTicks;

    public string FullArtistDisplay => string.IsNullOrWhiteSpace(Collaborators) ? Artist : $"{Artist} (feat. {Collaborators})";

    public string SearchIndex => _searchIndex;

    public void RefreshSearchIndex()
    {
        _searchIndex = string.Join('\n', [Title, Artist, Collaborators, Album])
            .ToLowerInvariant();
    }

    partial void OnTitleChanged(string value) => RefreshSearchIndex();
    partial void OnArtistChanged(string value) => RefreshSearchIndex();
    partial void OnCollaboratorsChanged(string value) => RefreshSearchIndex();
    partial void OnAlbumChanged(string value) => RefreshSearchIndex();
}

public partial class LibraryArtist : ObservableObject
{
    private string _searchIndex = string.Empty;

    [ObservableProperty]
    private string name = "Unknown Artist";

    public System.Collections.Generic.List<TrackModel> Songs { get; set; } = new();

    [ObservableProperty]
    private BitmapImage? image;

    [ObservableProperty]
    private string? artPath;

    [ObservableProperty]
    private bool hasLyrics;

    public string SearchIndex => _searchIndex;

    public void RefreshSearchIndex()
    {
        _searchIndex = Name.ToLowerInvariant();
    }

    partial void OnNameChanged(string value) => RefreshSearchIndex();
}

public partial class LibraryAlbum : ObservableObject
{
    private string _searchIndex = string.Empty;

    [ObservableProperty]
    private string title = "Unknown Album";

    [ObservableProperty]
    private string artist = "Unknown Artist";

    public System.Collections.Generic.List<TrackModel> Songs { get; set; } = new();

    [ObservableProperty]
    private BitmapImage? coverArt;

    [ObservableProperty]
    private string? artPath;

    [ObservableProperty]
    private bool hasLyrics;

    public string SearchIndex => _searchIndex;

    public void RefreshSearchIndex()
    {
        _searchIndex = $"{Title}\n{Artist}".ToLowerInvariant();
    }

    partial void OnTitleChanged(string value) => RefreshSearchIndex();
    partial void OnArtistChanged(string value) => RefreshSearchIndex();
}
