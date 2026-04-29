using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace FluentFlyoutWPF.Pages;

public partial class LibraryPage : Page
{
    private readonly DispatcherTimer _searchDebounceTimer;

    public LibraryPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;

        // Debounce search: wait 300ms after last keystroke before filtering
        _searchDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            SettingsManager.Current.LibrarySearchText = SearchBox.Text;
            SettingsManager.SaveSettings();
            RefreshFilters();
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        // Deshabilitar el scroll del contenedor padre si existe, para que solo 
        // los elementos internos de la página puedan scrollear.
        DependencyObject parent = VisualTreeHelper.GetParent(this);
        while (parent != null)
        {
            if (parent is ScrollViewer sv)
            {
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                break;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }

        if (LibraryManager.Instance.Tracks.Count == 0 && !LibraryManager.Instance.Tracks.Any())
        {
            await LibraryManager.Instance.InitializeAsync();
        }
        
        AlphabetIndex.ItemsSource = new string[] { "#", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };

        _ = Dispatcher.BeginInvoke(new Action(() => {
            // Restore search text
            SearchBox.Text = SettingsManager.Current.LibrarySearchText;

            // Restore tab
            switch (SettingsManager.Current.LibrarySelectedTab)
            {
                case 1: AlbumsTab.IsChecked = true; break;
                case 2: ArtistsTab.IsChecked = true; break;
                default: SongsTab.IsChecked = true; break;
            }

            UpdateBindings();
            UpdateSortDirectionIcon();

            // Restore SortButton content
            if (SortButton.Flyout is System.Windows.Controls.ContextMenu cm)
            {
                foreach (var item in cm.Items)
                {
                    if (item is System.Windows.Controls.MenuItem mi && mi.Tag?.ToString() == SettingsManager.Current.LibrarySortProperty)
                    {
                        SortButton.Content = mi.Header;
                        break;
                    }
                }
            }

            UpdateShuffleVisual();
        }), DispatcherPriority.Background);

        MusicPlayerService.Instance.PropertyChanged += MusicPlayerService_PropertyChanged;
        LibraryManager.Instance.TrackMetadataUpdated += LibraryManager_TrackMetadataUpdated;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        MusicPlayerService.Instance.PropertyChanged -= MusicPlayerService_PropertyChanged;
        LibraryManager.Instance.TrackMetadataUpdated -= LibraryManager_TrackMetadataUpdated;
    }

    private void MusicPlayerService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MusicPlayerService.IsShuffleEnabled))
        {
            Dispatcher.Invoke(UpdateShuffleVisual);
        }
    }

    private void LibraryManager_TrackMetadataUpdated(object? sender, TrackModel e)
    {
        Dispatcher.Invoke(RefreshFilters);
    }

    private void UpdateShuffleVisual()
    {
        bool isShuffle = MusicPlayerService.Instance.IsShuffleEnabled;
        var accentBrush = (Brush)System.Windows.Application.Current.TryFindResource("MicaWPF.Brushes.AccentFillColorDefault") ?? Brushes.DeepSkyBlue;
        var defaultBrush = Brushes.White;

        ShuffleIcon.Symbol = isShuffle ? Wpf.Ui.Controls.SymbolRegular.ArrowShuffle24 : Wpf.Ui.Controls.SymbolRegular.ArrowShuffleOff24;
        ShuffleIcon.Foreground = isShuffle ? accentBrush : defaultBrush;
        ShuffleBtn.Opacity = isShuffle ? 1.0 : 0.5;
    }

    private void UpdateBindings()
    {
        var dir = SettingsManager.Current.LibrarySortAscending ? System.ComponentModel.ListSortDirection.Ascending : System.ComponentModel.ListSortDirection.Descending;
        var sortProp = SettingsManager.Current.LibrarySortProperty;

        if (SongsTab.IsChecked == true)
        {
            var tracksView = CollectionViewSource.GetDefaultView(LibraryManager.Instance.Tracks);
            tracksView.Filter = TrackFilter;
            ApplySorting(tracksView, sortProp, dir);
            TracksListView.ItemsSource = tracksView;
        }
        else if (AlbumsTab.IsChecked == true)
        {
            var albumsView = CollectionViewSource.GetDefaultView(LibraryManager.Instance.Albums);
            albumsView.Filter = AlbumFilter;
            string albumSort = (sortProp == "Duration" || sortProp == "PlayCount" || sortProp == "Album") ? "Title" : sortProp;
            ApplySorting(albumsView, albumSort, dir);
            AlbumsListView.ItemsSource = albumsView;
        }
        else if (ArtistsTab.IsChecked == true)
        {
            var artistsView = CollectionViewSource.GetDefaultView(LibraryManager.Instance.Artists);
            artistsView.Filter = ArtistFilter;
            ApplySorting(artistsView, "Name", dir);
            ArtistsListView.ItemsSource = artistsView;
        }
    }

    private void ApplySorting(System.ComponentModel.ICollectionView view, string propertyName, System.ComponentModel.ListSortDirection direction)
    {
        view.SortDescriptions.Clear();
        view.SortDescriptions.Add(new System.ComponentModel.SortDescription(propertyName, direction));
    }

    private void SortMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            SettingsManager.Current.LibrarySortProperty = menuItem.Tag.ToString() ?? "Title";
            
            // Set default direction based on property if it's the first time
            if (SettingsManager.Current.LibrarySortProperty == "PlayCount" || SettingsManager.Current.LibrarySortProperty == "Duration")
            {
                SettingsManager.Current.LibrarySortAscending = false;
            }
            else
            {
                SettingsManager.Current.LibrarySortAscending = true;
            }

            SettingsManager.SaveSettings();
            SortButton.Content = menuItem.Header;
            UpdateSortDirectionIcon();
            UpdateBindings();
        }
    }

    private void SortDirectionBtn_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.LibrarySortAscending = !SettingsManager.Current.LibrarySortAscending;
        SettingsManager.SaveSettings();
        
        UpdateSortDirectionIcon();
        UpdateBindings();
    }

    private void UpdateSortDirectionIcon()
    {
        if (SortDirectionIcon != null)
        {
            SortDirectionIcon.Symbol = SettingsManager.Current.LibrarySortAscending
                ? Wpf.Ui.Controls.SymbolRegular.ArrowSortUp24
                : Wpf.Ui.Controls.SymbolRegular.ArrowSortDown24;
        }
    }

    private bool TrackFilter(object item)
    {
        if (item is TrackModel track)
        {
            // Lyrics filter
            if (SettingsManager.Current.LibraryLyricsFilterEnabled && !track.HasLyrics)
                return false;

            if (string.IsNullOrWhiteSpace(SearchBox.Text)) return true;
            
            return (track.Title?.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) == true) ||
                   (track.Artist?.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) == true) ||
                   (track.Album?.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) == true);
        }
        return false;
    }

    private bool AlbumFilter(object item)
    {
        if (item is LibraryAlbum album)
        {
            if (SettingsManager.Current.LibraryLyricsFilterEnabled && !album.Songs.Any(s => s.HasLyrics))
                return false;

            if (string.IsNullOrWhiteSpace(SearchBox.Text)) return true;
            return (album.Title?.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) == true) ||
                   (album.Artist?.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) == true);
        }
        return false;
    }

    private bool ArtistFilter(object item)
    {
        if (item is LibraryArtist artist)
        {
            if (SettingsManager.Current.LibraryLyricsFilterEnabled && !artist.Songs.Any(s => s.HasLyrics))
                return false;

            if (string.IsNullOrWhiteSpace(SearchBox.Text)) return true;
            return (artist.Name?.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) == true);
        }
        return false;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Restart the debounce timer on each keystroke
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void RefreshFilters()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (SongsTab.IsChecked == true)
                CollectionViewSource.GetDefaultView(LibraryManager.Instance.Tracks).Refresh();
            else if (AlbumsTab.IsChecked == true)
                CollectionViewSource.GetDefaultView(LibraryManager.Instance.Albums).Refresh();
            else if (ArtistsTab.IsChecked == true)
                CollectionViewSource.GetDefaultView(LibraryManager.Instance.Artists).Refresh();
        }, DispatcherPriority.Background);
    }

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        if (SongsTab.IsChecked == true) SettingsManager.Current.LibrarySelectedTab = 0;
        else if (AlbumsTab.IsChecked == true) SettingsManager.Current.LibrarySelectedTab = 1;
        else if (ArtistsTab.IsChecked == true) SettingsManager.Current.LibrarySelectedTab = 2;

        SettingsManager.SaveSettings();

        // Switch between Songs, Albums, Artists — update bindings to show active tab data
        UpdateBindings();
    }

    private void Album_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LibraryAlbum album)
        {
            SearchBox.Text = album.Title;
            SongsTab.IsChecked = true;
        }
    }

    private void Artist_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is LibraryArtist artist)
        {
            SearchBox.Text = artist.Name;
            SongsTab.IsChecked = true;
        }
    }

    private async void ScanLibrary_Click(object sender, RoutedEventArgs e)
    {
        await LibraryManager.Instance.ScanLibraryAsync();
    }

    private void UpdateFolders_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = System.Windows.Application.Current.Resources["Lib_SelectFolderDescription"] as string ?? "Select a folder to add to your music library",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            var folder = dialog.SelectedPath;
            if (!SettingsManager.Current.MusicLibraryFolders.Contains(folder))
            {
                SettingsManager.Current.MusicLibraryFolders.Add(folder);
                SettingsManager.SaveSettings();
                _ = LibraryManager.Instance.ScanLibraryAsync();
            }
        }
    }

    private void TracksListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TracksListView.SelectedItem is TrackModel track)
        {
            // Activate internal player if a library track is played
            SettingsManager.Current.InternalPlayerEnabled = true;
            SettingsManager.SaveSettings();

            // Get the current sorted/filtered list from the view to match what the user sees
            var tracksView = CollectionViewSource.GetDefaultView(LibraryManager.Instance.Tracks);
            var currentPlaylist = tracksView.Cast<TrackModel>().ToList();
            MusicPlayerService.Instance.PlaySingle(track, currentPlaylist);
        }
    }

    private void PlayAll_Click(object sender, RoutedEventArgs e)
    {
        var tracksView = CollectionViewSource.GetDefaultView(LibraryManager.Instance.Tracks);
        var tracks = tracksView.Cast<TrackModel>().ToList();
        if (tracks.Count > 0)
        {
            SettingsManager.Current.InternalPlayerEnabled = true;
            SettingsManager.SaveSettings();
            MusicPlayerService.Instance.IsShuffleEnabled = false;
            MusicPlayerService.Instance.PlayQueue(tracks, 0);
        }
    }

    private void ShuffleBtn_Click(object sender, RoutedEventArgs e)
    {
        MusicPlayerService.Instance.IsShuffleEnabled = !MusicPlayerService.Instance.IsShuffleEnabled;
        UpdateShuffleVisual();
    }
    private void OnPlayTrackCommand(object sender, RoutedEventArgs e)
    {
        if (TracksListView.SelectedItem is TrackModel track)
        {
            SettingsManager.Current.InternalPlayerEnabled = true;
            SettingsManager.SaveSettings();
            
            var tracksView = CollectionViewSource.GetDefaultView(LibraryManager.Instance.Tracks);
            var currentPlaylist = tracksView.Cast<TrackModel>().ToList();
            MusicPlayerService.Instance.PlaySingle(track, currentPlaylist);
        }
    }

    private void OnEditTrackCommand(object sender, RoutedEventArgs e)
    {
        var track = GetTrackFromContextMenu(sender) ?? TracksListView.SelectedItem as TrackModel;
        if (track != null)
        {
            EditTrackWindow.ShowInstance(track, Window.GetWindow(this));
        }
    }

    private void OnAddToQueueCommand(object sender, RoutedEventArgs e)
    {
        var track = GetTrackFromContextMenu(sender) ?? TracksListView.SelectedItem as TrackModel;
        if (track != null)
        {
            SettingsManager.Current.InternalPlayerEnabled = true;
            SettingsManager.SaveSettings();
            MusicPlayerService.Instance.AddToQueue(track);
        }
    }

    private void OnOpenFileLocationCommand(object sender, RoutedEventArgs e)
    {
        var track = GetTrackFromContextMenu(sender) ?? TracksListView.SelectedItem as TrackModel;
        if (track != null)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(track.FilePath);
                if (directory != null && System.IO.Directory.Exists(directory))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{track.FilePath}\"");
                }
            }
            catch { /* Ignore */ }
        }
    }

    private void ResetLibraryAppearance_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.Current.LibraryGridItemSize = 160.0;
        SettingsManager.Current.LibraryTrackIconSize = 40.0;
        SettingsManager.SaveSettings();
    }

    private void LyricsFilterBtn_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.SaveSettings();
        RefreshFilters();
    }

    /// <summary>
    /// Extracts the TrackModel from a context menu item's DataContext chain.
    /// </summary>
    private TrackModel? GetTrackFromContextMenu(object sender)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem &&
            menuItem.Parent is System.Windows.Controls.ContextMenu contextMenu &&
            contextMenu.PlacementTarget is FrameworkElement fe)
        {
            return fe.DataContext as TrackModel;
        }
        return null;
    }

    private void AlphabetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is string letter)
        {
            ScrollToLetter(letter);
        }
    }

    private void ScrollToLetter(string letter)
    {
        System.Windows.Controls.ListView? activeListView = null;
        if (SongsTab.IsChecked == true) activeListView = TracksListView;
        else if (AlbumsTab.IsChecked == true) activeListView = AlbumsListView;
        else if (ArtistsTab.IsChecked == true) activeListView = ArtistsListView;

        if (activeListView == null || activeListView.Items.Count == 0) return;

        object? itemToScroll = null;
        string sortProperty = SettingsManager.Current.LibrarySortProperty;

        foreach (var item in activeListView.Items)
        {
            string title = "";
            if (item is TrackModel track)
            {
                if (sortProperty == "Artist") title = track.Artist;
                else if (sortProperty == "Album") title = track.Album;
                else title = track.Title;
            }
            else if (item is LibraryAlbum album)
            {
                if (sortProperty == "Artist") title = album.Artist;
                else title = album.Title;
            }
            else if (item is LibraryArtist artist)
            {
                title = artist.Name;
            }

            if (string.IsNullOrWhiteSpace(title)) continue;

            if (letter == "#")
            {
                if (!char.IsLetter(title[0]))
                {
                    itemToScroll = item;
                    break;
                }
            }
            else
            {
                if (title.StartsWith(letter, StringComparison.OrdinalIgnoreCase))
                {
                    itemToScroll = item;
                    break;
                }
            }
        }

        if (itemToScroll != null)
        {
            activeListView.ScrollIntoView(itemToScroll);
        }
    }
}
