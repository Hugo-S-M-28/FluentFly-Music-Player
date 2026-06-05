using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Models;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;

namespace FluentFlyoutWPF.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly DispatcherTimer _searchDebounceTimer;
    private readonly ILibraryService _libraryService;
    private readonly IPlaybackService _playbackService;
    private readonly IWindowService _windowService;
    private readonly ISystemShellService _systemShellService;
    private readonly IFileDialogService _fileDialogService;
    private readonly IDialogService _dialogService;
    private bool _isInitialized;
    private bool _tracksViewNeedsRefresh = true;
    private bool _albumsViewNeedsRefresh = true;
    private bool _artistsViewNeedsRefresh = true;
    private string _normalizedSearchText = string.Empty;

    public UserSettings Settings => SettingsManager.Current;
    public ICollectionView TracksView { get; }
    public ICollectionView AlbumsView { get; }
    public ICollectionView ArtistsView { get; }
    public IReadOnlyList<string> AlphabetIndex { get; } =
    [
        "#", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L",
        "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"
    ];

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string currentSortLabel = string.Empty;

    [ObservableProperty]
    private Wpf.Ui.Controls.SymbolRegular sortDirectionSymbol = Wpf.Ui.Controls.SymbolRegular.ArrowSortUp24;

    [ObservableProperty]
    private bool isShuffleEnabled;

    [ObservableProperty]
    private Wpf.Ui.Controls.SymbolRegular shuffleSymbol = Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24;

    [ObservableProperty]
    private string? requestedScrollLetter;

    [ObservableProperty]
    private object? requestedScrollItem;

    [ObservableProperty]
    private bool isLibraryLoading;

    public LibraryViewModel(
        ILibraryService libraryService,
        IPlaybackService playbackService,
        IWindowService windowService,
        ISystemShellService systemShellService,
        IFileDialogService fileDialogService,
        IDialogService dialogService)
    {
        _libraryService = libraryService;
        _playbackService = playbackService;
        _windowService = windowService;
        _systemShellService = systemShellService;
        _fileDialogService = fileDialogService;
        _dialogService = dialogService;

        TracksView = CollectionViewSource.GetDefaultView(_libraryService.Tracks);
        AlbumsView = CollectionViewSource.GetDefaultView(_libraryService.Albums);
        ArtistsView = CollectionViewSource.GetDefaultView(_libraryService.Artists);

        TracksView.Filter = TrackFilter;
        AlbumsView.Filter = AlbumFilter;
        ArtistsView.Filter = ArtistFilter;

        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            Settings.LibrarySearchText = SearchText;
            SettingsManager.SaveSettings();
            _normalizedSearchText = NormalizeSearchText(SearchText);
            MarkViewsDirty();
            RefreshActiveView();
        };

        SearchText = Settings.LibrarySearchText;
        _normalizedSearchText = NormalizeSearchText(SearchText);

        Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UserSettings.LibrarySelectedTab))
            {
                UpdateBindings();
            }
        };

        _playbackService.PropertyChanged += PlaybackServiceOnPropertyChanged;
        _libraryService.TrackMetadataUpdated += LibraryServiceOnTrackMetadataUpdated;

        UpdateBindings();
        SyncVisualState();
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        if (_libraryService.Tracks.Count == 0 && !_libraryService.Tracks.Any())
        {
            IsLibraryLoading = true;
            await _libraryService.InitializeAsync();
            IsLibraryLoading = false;
        }

        _isInitialized = true;
        MarkViewsDirty();
        EnsureActiveViewUpToDate();
    }

    [RelayCommand]
    private Task InitializeViewAsync()
    {
        return InitializeAsync();
    }

    partial void OnSearchTextChanged(string value)
    {
        _normalizedSearchText = NormalizeSearchText(value);
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    public void UpdateBindings()
    {
        MarkViewsDirty();
        EnsureActiveViewUpToDate();
        SyncVisualState();
    }

    public void RefreshFilters()
    {
        MarkViewsDirty();
        RefreshActiveView();
    }

    public IReadOnlyList<TrackModel> GetVisibleTracks()
    {
        EnsureViewUpToDate(0);
        return TracksView.Cast<TrackModel>().ToList();
    }

    public void ApplySortProperty(string propertyName, string? label)
    {
        Settings.LibrarySortProperty = propertyName;
        Settings.LibrarySortAscending = propertyName is "PlayCount" or "Duration" ? false : true;
        SettingsManager.SaveSettings();
        CurrentSortLabel = label ?? propertyName;
        UpdateBindings();
    }

    [RelayCommand]
    public void ApplySortPropertyFromMenu(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return;
        }

        ApplySortProperty(propertyName, GetSortLabel(propertyName));
    }

    [RelayCommand]
    public void ToggleSortDirection()
    {
        Settings.LibrarySortAscending = !Settings.LibrarySortAscending;
        SettingsManager.SaveSettings();
        UpdateBindings();
    }

    [RelayCommand]
    public void ToggleLyricsFilter()
    {
        SettingsManager.SaveSettings();
        RefreshFilters();
    }

    [RelayCommand]
    public void ResetAppearance()
    {
        Settings.LibraryGridItemSize = 160.0;
        Settings.LibraryTrackIconSize = 40.0;
        SettingsManager.SaveSettings();
    }

    [RelayCommand]
    public void PlayVisibleTracks()
    {
        var tracks = GetVisibleTracks().ToList();
        if (tracks.Count == 0)
        {
            return;
        }

        Settings.InternalPlayerEnabled = true;
        SettingsManager.SaveSettings();
        _playbackService.IsShuffleEnabled = false;
        _playbackService.PlayQueue(tracks, 0);
        SyncVisualState();
    }

    [RelayCommand]
    public void PlayTrack(TrackModel? track)
    {
        if (track == null)
        {
            return;
        }

        Settings.InternalPlayerEnabled = true;
        SettingsManager.SaveSettings();
        _playbackService.PlaySingle(track, GetVisibleTracks().ToList());
    }

    [RelayCommand]
    public void AddToQueue(TrackModel? track)
    {
        if (track == null)
        {
            return;
        }

        Settings.InternalPlayerEnabled = true;
        SettingsManager.SaveSettings();
        _playbackService.AddToQueue(track);
    }

    [RelayCommand]
    public void ToggleShuffle()
    {
        _playbackService.IsShuffleEnabled = !_playbackService.IsShuffleEnabled;
        SyncVisualState();
    }

    public void SetSelectedTab(int index)
    {
        Settings.LibrarySelectedTab = index;
        SettingsManager.SaveSettings();
    }

    [RelayCommand]
    public void SelectAlbum(LibraryAlbum? album)
    {
        if (album == null)
        {
            return;
        }

        SearchText = album.Title;
        SetSelectedTab(0);
    }

    [RelayCommand]
    public void SelectArtist(LibraryArtist? artist)
    {
        if (artist == null)
        {
            return;
        }

        SearchText = artist.Name;
        SetSelectedTab(0);
    }

    [RelayCommand]
    public void HidePlaylist()
    {
        Settings.LibraryPlaylistVisible = false;
        SettingsManager.SaveSettings();
    }

    [RelayCommand]
    public void SelectTab(object? parameter)
    {
        int index = parameter switch
        {
            int value => value,
            string text when int.TryParse(text, out int parsed) => parsed,
            _ => Settings.LibrarySelectedTab
        };

        SetSelectedTab(index);
        EnsureActiveViewUpToDate();
    }

    [RelayCommand]
    public void ScrollToLetter(string? letter)
    {
        if (string.IsNullOrWhiteSpace(letter))
        {
            return;
        }

        RequestedScrollLetter = letter;
        EnsureViewUpToDate(Settings.LibrarySelectedTab);
        RequestedScrollItem = FindFirstItemForLetter(letter, Settings.LibrarySelectedTab);
    }

    public object? FindFirstItemForLetter(string letter, int tabIndex)
    {
        var items = tabIndex switch
        {
            1 => AlbumsView.Cast<object>(),
            2 => ArtistsView.Cast<object>(),
            _ => TracksView.Cast<object>()
        };

        string sortProperty = Settings.LibrarySortProperty;

        foreach (var item in items)
        {
            string title = item switch
            {
                TrackModel track when sortProperty == "Artist" => track.Artist,
                TrackModel track when sortProperty == "Album" => track.Album,
                TrackModel track => track.Title,
                LibraryAlbum album when sortProperty == "Artist" => album.Artist,
                LibraryAlbum album => album.Title,
                LibraryArtist artist => artist.Name,
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (letter == "#" ? !char.IsLetter(title[0]) : title.StartsWith(letter, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    public void SyncVisualState()
    {
        IsShuffleEnabled = _playbackService.IsShuffleEnabled;
        ShuffleSymbol = IsShuffleEnabled
            ? Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24
            : Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24;
        SortDirectionSymbol = Settings.LibrarySortAscending
            ? Wpf.Ui.Controls.SymbolRegular.ArrowSortUp24
            : Wpf.Ui.Controls.SymbolRegular.ArrowSortDown24;
        CurrentSortLabel = GetSortLabel(Settings.LibrarySortProperty);
    }

    [RelayCommand]
    public async Task ScanLibraryAsync()
    {
        await _libraryService.ScanLibraryAsync();
    }

    [RelayCommand]
    public void OpenLibraryFolders()
    {
        _windowService.ShowManageLibrary();
    }

    [RelayCommand]
    public void EditTrack(TrackModel? track)
    {
        if (track != null)
        {
            _windowService.ShowEditTrack(track);
        }
    }

    [RelayCommand]
    public void OpenTrackLocation(TrackModel? track)
    {
        if (track != null)
        {
            _systemShellService.RevealInFileExplorer(track.FilePath);
        }
    }

    private static void ApplySorting(ICollectionView view, string propertyName, ListSortDirection direction)
    {
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new SortDescription(propertyName, direction));
    }

    private bool TrackFilter(object item)
    {
        if (item is not TrackModel track)
        {
            return false;
        }

        if (Settings.LibraryLyricsFilterEnabled && !track.HasLyrics)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_normalizedSearchText))
        {
            return true;
        }

        return track.SearchIndex.Contains(_normalizedSearchText, StringComparison.Ordinal);
    }

    private bool AlbumFilter(object item)
    {
        if (item is not LibraryAlbum album)
        {
            return false;
        }

        if (Settings.LibraryLyricsFilterEnabled && !album.HasLyrics)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_normalizedSearchText))
        {
            return true;
        }

        return album.SearchIndex.Contains(_normalizedSearchText, StringComparison.Ordinal);
    }

    private bool ArtistFilter(object item)
    {
        if (item is not LibraryArtist artist)
        {
            return false;
        }

        if (Settings.LibraryLyricsFilterEnabled && !artist.HasLyrics)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_normalizedSearchText))
        {
            return true;
        }

        return artist.SearchIndex.Contains(_normalizedSearchText, StringComparison.Ordinal);
    }

    private static string GetSortLabel(string sortProperty)
    {
        return sortProperty switch
        {
            "Artist" => "Artista",
            "Album" => "Ãlbum",
            "Duration" => "DuraciÃ³n",
            "PlayCount" => "Reproducciones",
            _ => "TÃ­tulo"
        };
    }

    [RelayCommand]
    public async Task LoadPlaylistAsync()
    {
        var filePath = _fileDialogService.OpenFile(
            "Cargar Lista de ReproducciÃ³n",
            "Archivos de Lista de ReproducciÃ³n (*.m3u;*.m3u8;*.json)|*.m3u;*.m3u8;*.json|Todos los archivos (*.*)|*.*");

        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                await _playbackService.ImportQueueAsync(filePath);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Error", $"Error al cargar la lista: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public async Task SavePlaylistAsync()
    {
        if (_playbackService.CurrentQueue.Count == 0)
        {
            await _dialogService.ShowMessageAsync("Guardar Lista", "La lista de reproducciÃ³n estÃ¡ vacÃ­a y no se puede guardar.");
            return;
        }

        var filePath = _fileDialogService.SaveFile(
            "Guardar Lista de ReproducciÃ³n",
            "Archivos de Lista de ReproducciÃ³n (*.m3u;*.json)|*.m3u;*.json|Lista M3U (*.m3u)|*.m3u|Lista JSON (*.json)|*.json",
            "lista");

        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                _playbackService.ExportQueue(filePath);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync("Error", $"Error al guardar la lista: {ex.Message}");
            }
        }
    }

    private void PlaybackServiceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPlaybackService.IsShuffleEnabled))
        {
            SyncVisualState();
        }
    }

    private void LibraryServiceOnTrackMetadataUpdated(object? sender, TrackModel e)
    {
        e.RefreshSearchIndex();
        MarkViewsDirty();
        RefreshActiveView();
    }

    private void MarkViewsDirty()
    {
        _tracksViewNeedsRefresh = true;
        _albumsViewNeedsRefresh = true;
        _artistsViewNeedsRefresh = true;
    }

    private void EnsureActiveViewUpToDate()
    {
        EnsureViewUpToDate(Settings.LibrarySelectedTab);
    }

    private void RefreshActiveView()
    {
        EnsureViewUpToDate(Settings.LibrarySelectedTab);
    }

    private void EnsureViewUpToDate(int tabIndex)
    {
        switch (tabIndex)
        {
            case 1:
                if (_albumsViewNeedsRefresh)
                {
                    ApplyViewState(AlbumsView, GetSortPropertyForAlbums(), GetSortDirection());
                    _albumsViewNeedsRefresh = false;
                }
                break;
            case 2:
                if (_artistsViewNeedsRefresh)
                {
                    ApplyViewState(ArtistsView, "Name", GetSortDirection());
                    _artistsViewNeedsRefresh = false;
                }
                break;
            default:
                if (_tracksViewNeedsRefresh)
                {
                    ApplyViewState(TracksView, Settings.LibrarySortProperty, GetSortDirection());
                    _tracksViewNeedsRefresh = false;
                }
                break;
        }
    }

    private ListSortDirection GetSortDirection()
        => Settings.LibrarySortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;

    private string GetSortPropertyForAlbums()
    {
        return Settings.LibrarySortProperty is "Duration" or "PlayCount" or "Album"
            ? "Title"
            : Settings.LibrarySortProperty;
    }

    private static void ApplyViewState(ICollectionView view, string propertyName, ListSortDirection direction)
    {
        bool needsSort = view.SortDescriptions.Count != 1 ||
                         view.SortDescriptions[0].PropertyName != propertyName ||
                         view.SortDescriptions[0].Direction != direction;

        if (needsSort)
        {
            using (view.DeferRefresh())
            {
                ApplySorting(view, propertyName, direction);
            }
        }
        else
        {
            view.Refresh();
        }
    }

    private static string NormalizeSearchText(string? value)
        => value?.Trim().ToLowerInvariant() ?? string.Empty;
}
