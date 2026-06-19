using System;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentFlyout.Classes;

namespace FluentFlyoutWPF.Models;

public partial class TrackModel : ObservableObject
{
    private string _searchIndex = string.Empty;
    private string _defaultArtist;
    private string _defaultAlbum;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullArtistDisplay))]
    private string artist;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FullArtistDisplay))]
    private string collaborators = string.Empty;

    [ObservableProperty]
    private string album;

    [ObservableProperty]
    private TimeSpan duration;

    [ObservableProperty]
    private string filePath = string.Empty;

    [ObservableProperty]
    private string? albumArtPath;

    [ObservableProperty]
    private BitmapImage? albumArt;

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

    [ObservableProperty]
    private int playCount;

    [ObservableProperty]
    private long fileModifiedUtcTicks;

    /// <summary>
    /// 1-based display position in the playback queue, set by PlaylistViewModel.SyncQueue().
    /// Bound directly in PlaylistControl to avoid WPF container-level index bugs
    /// (AlternationIndex / IndexFromContainer fail under VirtualizationMode=Recycling).
    /// </summary>
    [ObservableProperty]
    private int displayIndex;


    public TrackModel()
    {
        _defaultArtist = LocalizationManager.GetString("Track_UnknownArtist");
        _defaultAlbum = LocalizationManager.GetString("Track_UnknownAlbum");
        artist = _defaultArtist;
        album = _defaultAlbum;
        LocalizationManager.LocalizationChanged += HandleLocalizationChanged;
        RefreshSearchIndex();
    }

    public string FullArtistDisplay => string.IsNullOrWhiteSpace(Collaborators) ? Artist : $"{Artist} (feat. {Collaborators})";

    public string SearchIndex => _searchIndex;

    partial void OnLyricsChanged(string value)
    {
        HasLyrics = !string.IsNullOrWhiteSpace(value);
    }

    public void RefreshSearchIndex()
    {
        _searchIndex = string.Join('\n', [Title, Artist, Collaborators, Album])
            .ToLowerInvariant();
    }

    partial void OnTitleChanged(string value) => RefreshSearchIndex();
    partial void OnArtistChanged(string value) => RefreshSearchIndex();
    partial void OnCollaboratorsChanged(string value) => RefreshSearchIndex();
    partial void OnAlbumChanged(string value) => RefreshSearchIndex();

    private void HandleLocalizationChanged(object? sender, EventArgs e)
    {
        var nextDefaultArtist = LocalizationManager.GetString("Track_UnknownArtist");
        var nextDefaultAlbum = LocalizationManager.GetString("Track_UnknownAlbum");

        if (Artist == _defaultArtist)
        {
            Artist = nextDefaultArtist;
        }

        if (Album == _defaultAlbum)
        {
            Album = nextDefaultAlbum;
        }

        _defaultArtist = nextDefaultArtist;
        _defaultAlbum = nextDefaultAlbum;
    }
}

public partial class LibraryArtist : ObservableObject
{
    private string _searchIndex = string.Empty;
    private string _defaultName;

    [ObservableProperty]
    private string name;

    public System.Collections.Generic.List<TrackModel> Songs { get; set; } = new();

    [ObservableProperty]
    private BitmapImage? image;

    [ObservableProperty]
    private string? artPath;

    [ObservableProperty]
    private bool hasLyrics;

    public LibraryArtist()
    {
        _defaultName = LocalizationManager.GetString("Track_UnknownArtist");
        name = _defaultName;
        LocalizationManager.LocalizationChanged += HandleLocalizationChanged;
        RefreshSearchIndex();
    }

    public string SearchIndex => _searchIndex;

    public void RefreshSearchIndex()
    {
        _searchIndex = Name.ToLowerInvariant();
    }

    partial void OnNameChanged(string value) => RefreshSearchIndex();

    private void HandleLocalizationChanged(object? sender, EventArgs e)
    {
        var nextDefaultName = LocalizationManager.GetString("Track_UnknownArtist");
        if (Name == _defaultName)
        {
            Name = nextDefaultName;
        }

        _defaultName = nextDefaultName;
    }
}

public partial class LibraryAlbum : ObservableObject
{
    private string _searchIndex = string.Empty;
    private string _defaultTitle;
    private string _defaultArtist;

    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string artist;

    public System.Collections.Generic.List<TrackModel> Songs { get; set; } = new();

    [ObservableProperty]
    private BitmapImage? coverArt;

    [ObservableProperty]
    private string? artPath;

    [ObservableProperty]
    private bool hasLyrics;

    public LibraryAlbum()
    {
        _defaultTitle = LocalizationManager.GetString("Track_UnknownAlbum");
        _defaultArtist = LocalizationManager.GetString("Track_UnknownArtist");
        title = _defaultTitle;
        artist = _defaultArtist;
        LocalizationManager.LocalizationChanged += HandleLocalizationChanged;
        RefreshSearchIndex();
    }

    public string SearchIndex => _searchIndex;

    public void RefreshSearchIndex()
    {
        _searchIndex = $"{Title}\n{Artist}".ToLowerInvariant();
    }

    partial void OnTitleChanged(string value) => RefreshSearchIndex();
    partial void OnArtistChanged(string value) => RefreshSearchIndex();

    private void HandleLocalizationChanged(object? sender, EventArgs e)
    {
        var nextDefaultTitle = LocalizationManager.GetString("Track_UnknownAlbum");
        var nextDefaultArtist = LocalizationManager.GetString("Track_UnknownArtist");

        if (Title == _defaultTitle)
        {
            Title = nextDefaultTitle;
        }

        if (Artist == _defaultArtist)
        {
            Artist = nextDefaultArtist;
        }

        _defaultTitle = nextDefaultTitle;
        _defaultArtist = nextDefaultArtist;
    }
}
