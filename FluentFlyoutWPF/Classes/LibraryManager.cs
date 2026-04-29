using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Classes;

public class LibraryManager : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static LibraryManager? _instance;
    private readonly List<FileSystemWatcher> _watchers = new();
    private bool _isScanning;

    public static LibraryManager Instance => _instance ??= new LibraryManager();

    public BulkObservableCollection<TrackModel> Tracks { get; } = new();
    public BulkObservableCollection<LibraryAlbum> Albums { get; } = new();
    public BulkObservableCollection<LibraryArtist> Artists { get; } = new();

    public event EventHandler<TrackModel>? TrackMetadataUpdated;
    public event EventHandler<TrackModel>? TrackLyricsUpdated;

    private readonly Dictionary<string, List<string>> _albumArtCache = new();
    private readonly string[] _supportedExtensions = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".wma", ".aac", ".opus" };
    private static string ArtCachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluentFlyout", "ArtCache");

    private static string LibraryFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentFlyout",
        "library.json"
    );

    public async Task InitializeAsync()
    {
        if (!Directory.Exists(ArtCachePath)) Directory.CreateDirectory(ArtCachePath);
        
        // Load existing library from JSON first (very fast)
        await LoadLibraryAsync();
        RebuildAlbumsAndArtists(); // Initial build from cache

        SetupWatchers();

        // Start disk scan in background without awaiting it to unblock UI
        _ = Task.Run(async () => 
        {
            await ScanLibraryAsync();
        });
    }

    private void SetupWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        _watchers.Clear();

        var folders = SettingsManager.Current.MusicLibraryFolders;
        if (folders == null) return;

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;

            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                watcher.Created += OnFileChanged;
                watcher.Deleted += OnFileChanged;
                watcher.Renamed += OnFileChanged;

                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed to setup watcher for {folder}");
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Simple debounce: trigger scan after 2 seconds of no activity
        _ = Task.Delay(2000).ContinueWith(_ => 
        {
            if (!_isScanning)
            {
                _ = ScanLibraryAsync();
            }
        });
    }

    private async Task LoadLibraryAsync()
    {
        try
        {
            if (File.Exists(LibraryFilePath))
            {
                var json = await File.ReadAllTextAsync(LibraryFilePath);
                var tracks = System.Text.Json.JsonSerializer.Deserialize<List<TrackModel>>(json);
                if (tracks != null)
                {
                    // Load tracks in a single batch to minimize UI thread overhead
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Tracks.ReplaceAll(tracks);
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load music library.");
        }
    }

    public async Task SaveLibraryAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(LibraryFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };
            var json = System.Text.Json.JsonSerializer.Serialize(Tracks.ToList(), options);
            await File.WriteAllTextAsync(LibraryFilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to save music library.");
        }
    }

    public async Task ScanLibraryAsync()
    {
        if (_isScanning) return;
        _isScanning = true;

        var folders = SettingsManager.Current.MusicLibraryFolders;
        if (folders == null || folders.Count == 0) 
        {
            _isScanning = false;
            return;
        }

        int updatedCount = 0;
        var existingPaths = new HashSet<string>(Tracks.Select(t => t.FilePath));
        var folderList = folders.ToList();

        // Parallelize scanning across multiple folders if there are several
        var scanTasks = folderList.Select(async folder =>
        {
            if (!Directory.Exists(folder)) return;

            try
            {
                var files = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                                     .Where(f => _supportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

                var newTracks = new List<TrackModel>();
                foreach (var file in files)
                {
                    if (existingPaths.Contains(file)) continue;

                    var track = await Task.Run(() => ExtractMetadata(file));
                    if (track != null)
                    {
                        newTracks.Add(track);
                        System.Threading.Interlocked.Increment(ref updatedCount);
                    }
                    
                    // Add in batches of 50 to keep UI responsive but avoid too many notifications
                    if (newTracks.Count >= 50)
                    {
                        var batch = newTracks.ToList();
                        newTracks.Clear();
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            Tracks.AddRange(batch);
                        });
                    }
                }

                if (newTracks.Count > 0)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Tracks.AddRange(newTracks);
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error scanning folder {folder}");
            }
        });

        await Task.WhenAll(scanTasks);

        // Remove tracks that no longer exist
        var tracksToRemove = Tracks.Where(t => !File.Exists(t.FilePath)).ToList();
        if (tracksToRemove.Count > 0)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                foreach (var track in tracksToRemove)
                {
                    Tracks.Remove(track);
                }
            });
            System.Threading.Interlocked.Increment(ref updatedCount);
        }
        
        if (updatedCount > 0)
        {
            await SaveLibraryAsync();
            RebuildAlbumsAndArtists();
        }
        _isScanning = false;
    }

    private TrackModel ExtractMetadata(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var duration = file.Properties?.Duration ?? TimeSpan.Zero;
            var title = file.Tag?.Title ?? Path.GetFileNameWithoutExtension(filePath);
            var performers = file.Tag?.Performers ?? Array.Empty<string>();
            var artist = performers.Length > 0 ? performers[0] : (file.Tag?.FirstAlbumArtist ?? "Unknown Artist");
            var collaborators = performers.Length > 1 ? string.Join("; ", performers.Skip(1)) : string.Empty;
            
            var album = file.Tag?.Album ?? "Unknown Album";
            var trackNumber = (int)(file.Tag?.Track ?? 0);
            var year = (int)(file.Tag?.Year ?? 0);
            var genre = file.Tag?.FirstGenre ?? string.Empty;

            var track = new TrackModel
            {
                FilePath = filePath,
                Title = title,
                Artist = artist,
                Collaborators = collaborators,
                Album = album,
                Duration = duration,
                TrackNumber = trackNumber,
                Genre = genre,
                Lyrics = file.Tag?.Lyrics ?? string.Empty,
                HasLyrics = !string.IsNullOrWhiteSpace(file.Tag?.Lyrics)
            };

            // Check for lyrics file (.lrc)
            var lrcPath = Path.ChangeExtension(filePath, ".lrc");
            if (File.Exists(lrcPath))
            {
                track.HasLyrics = true;
                // Optionally load first line or just mark as true
                // track.Lyrics = File.ReadAllText(lrcPath); // Loading all lyrics during scan might be slow
            }

            // Store album art to disk cache
            if (file.Tag?.Pictures != null && file.Tag.Pictures.Length > 0)
            {
                var picture = file.Tag.Pictures[0];
                if (picture?.Data?.Data != null)
                {
                    string hash = GetHash(picture.Data.Data);
                    string cacheFile = Path.Combine(ArtCachePath, hash + ".jpg");
                    
                    if (!File.Exists(cacheFile))
                    {
                        File.WriteAllBytes(cacheFile, picture.Data.Data);
                    }
                    
                    track.AlbumArtPath = cacheFile;
                    
                    // Cache by album key for quick lookup
                    var albumKey = GetAlbumKey(artist, album);
                    if (!_albumArtCache.ContainsKey(albumKey))
                    {
                        _albumArtCache[albumKey] = new List<string>();
                    }
                    _albumArtCache[albumKey].Add(cacheFile);
                }
            }

            return track;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to read metadata for {filePath}");
            return new TrackModel
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                Artist = "Unknown Artist",
                Album = "Unknown Album"
            };
        }
    }

    public async Task<bool> UpdateTrackMetadataAsync(TrackModel track, string newTitle, string newArtist, string newCollaborators, string newAlbum, string newGenre, int newTrackNumber = 0, string? newAlbumArtPath = null)
    {
        bool wasPlaying = false;
        TimeSpan lastPosition = TimeSpan.Zero;

        try
        {
            // Release file lock if it's being played
            if (MusicPlayerService.Instance.CurrentTrack?.FilePath == track.FilePath)
            {
                wasPlaying = MusicPlayerService.Instance.IsPlaying;
                lastPosition = MusicPlayerService.Instance.CurrentPosition;
                MusicPlayerService.Instance.Stop();
                // Give the system a moment to release the file handle
                await Task.Delay(200);
            }

            await Task.Run(() =>
            {
                // Ensure file is not read-only
                var attributes = File.GetAttributes(track.FilePath);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(track.FilePath, attributes & ~FileAttributes.ReadOnly);
                }

                using var file = TagLib.File.Create(track.FilePath);
                file.Tag.Title = newTitle;
                
                // Merge main artist and collaborators for the Performers tag
                var performersList = new List<string> { newArtist };
                if (!string.IsNullOrWhiteSpace(newCollaborators))
                {
                    var extraArtists = newCollaborators.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                       .Select(a => a.Trim())
                                                       .Where(a => !string.IsNullOrEmpty(a));
                    performersList.AddRange(extraArtists);
                }
                file.Tag.Performers = performersList.ToArray();
                
                file.Tag.Album = newAlbum;
                file.Tag.AlbumArtists = new[] { newArtist };
                file.Tag.Genres = new[] { newGenre };
                file.Tag.Track = (uint)Math.Max(0, newTrackNumber);

                // Update album art if a new image was selected
                if (!string.IsNullOrEmpty(newAlbumArtPath) && File.Exists(newAlbumArtPath))
                {
                    var artData = File.ReadAllBytes(newAlbumArtPath);
                    var mimeType = Path.GetExtension(newAlbumArtPath).ToLowerInvariant() switch
                    {
                        ".png" => "image/png",
                        ".bmp" => "image/bmp",
                        ".webp" => "image/webp",
                        _ => "image/jpeg"
                    };

                    file.Tag.Pictures = new TagLib.IPicture[]
                    {
                        new TagLib.Picture(new TagLib.ByteVector(artData))
                        {
                            Type = TagLib.PictureType.FrontCover,
                            MimeType = mimeType,
                            Description = "Cover"
                        }
                    };
                }

                // Retry loop for saving tags in case of transient locks
                int attempts = 0;
                while (true)
                {
                    try
                    {
                        file.Save();
                        break;
                    }
                    catch (Exception ex) when (attempts < 3 && (ex is UnauthorizedAccessException || ex is IOException))
                    {
                        attempts++;
                        System.Threading.Thread.Sleep(500);
                    }
                    catch
                    {
                        throw;
                    }
                }
            });

            // Update model in memory
            track.Title = newTitle;
            track.Artist = newArtist;
            track.Collaborators = newCollaborators;
            track.Album = newAlbum;
            track.Genre = newGenre;
            track.TrackNumber = newTrackNumber;

            // Update album art cache if changed
            if (!string.IsNullOrEmpty(newAlbumArtPath) && File.Exists(newAlbumArtPath))
            {
                var artData = File.ReadAllBytes(newAlbumArtPath);
                string hash = GetHash(artData);
                string cacheFile = Path.Combine(ArtCachePath, hash + ".jpg");

                if (!File.Exists(cacheFile))
                {
                    File.WriteAllBytes(cacheFile, artData);
                }

                track.AlbumArtPath = cacheFile;

                // Invalidate the image cache for this path so the UI picks up the new art
                PathToImageConverter.ClearCache();
            }

            // Re-save library and notify UI
            await SaveLibraryAsync();
            RebuildAlbumsAndArtists();
            TrackMetadataUpdated?.Invoke(this, track);

            // Resume playback if it was playing (optional, but nice)
            if (wasPlaying)
            {
                MusicPlayerService.Instance.Play(track);
                MusicPlayerService.Instance.Seek(lastPosition);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to update metadata for {track.FilePath}");
            return false;
        }
    }

    public async Task<bool> UpdateTrackLyricsAsync(TrackModel track, string lyricsText)
    {
        bool wasPlaying = false;
        TimeSpan lastPosition = TimeSpan.Zero;

        try
        {
            // Release file lock if it's being played
            if (MusicPlayerService.Instance.CurrentTrack?.FilePath == track.FilePath)
            {
                wasPlaying = MusicPlayerService.Instance.IsPlaying;
                lastPosition = MusicPlayerService.Instance.CurrentPosition;
                MusicPlayerService.Instance.Stop();
                await Task.Delay(200);
            }

            // 1. Save to internal tags
            await Task.Run(() =>
            {
                // Ensure file is not read-only
                var attributes = File.GetAttributes(track.FilePath);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(track.FilePath, attributes & ~FileAttributes.ReadOnly);
                }

                using var file = TagLib.File.Create(track.FilePath);
                file.Tag.Lyrics = lyricsText;
                
                int attempts = 0;
                while (true)
                {
                    try
                    {
                        file.Save();
                        break;
                    }
                    catch (Exception ex) when (attempts < 3 && (ex is UnauthorizedAccessException || ex is IOException))
                    {
                        attempts++;
                        System.Threading.Thread.Sleep(500);
                    }
                    catch
                    {
                        throw;
                    }
                }
            });

            // 2. Save to external .lrc if it contains timestamps
            if (lyricsText.Contains("[") && lyricsText.Contains("]"))
            {
                var lrcPath = Path.ChangeExtension(track.FilePath, ".lrc");
                await File.WriteAllTextAsync(lrcPath, lyricsText, System.Text.Encoding.UTF8);
            }

            track.Lyrics = lyricsText;
            track.HasLyrics = !string.IsNullOrWhiteSpace(lyricsText);
            
            // Re-save library and notify UI
            await SaveLibraryAsync();
            TrackLyricsUpdated?.Invoke(this, track);

            // Resume playback if it was playing
            if (wasPlaying)
            {
                MusicPlayerService.Instance.Play(track);
                MusicPlayerService.Instance.Seek(lastPosition);
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to update lyrics for {track.FilePath}");
            return false;
        }
    }

    private string GetHash(byte[] data)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(data);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private string GetAlbumKey(string artist, string album)
    {
        return $"{artist.ToLowerInvariant()}::{album.ToLowerInvariant()}";
    }

    public void RebuildAlbumsAndArtists()
    {
        var tempAlbums = new List<LibraryAlbum>();
        var tempArtists = new List<LibraryArtist>();
        _albumArtCache.Clear();

        // Group tracks by album
        var albumGroups = Tracks.GroupBy(t => new { t.Artist, t.Album });
        foreach (var group in albumGroups)
        {
            var albumTracks = group.ToList();
            var album = new LibraryAlbum
            {
                Title = group.Key.Album,
                Artist = group.Key.Artist,
                Songs = albumTracks.OrderBy(t => t.TrackNumber).ToList()
            };

            // Try to get album art
            var trackWithArt = albumTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.AlbumArtPath));
            if (trackWithArt != null)
            {
                album.ArtPath = trackWithArt.AlbumArtPath;
            }

            tempAlbums.Add(album);

            // Cache album art
            var albumKey = GetAlbumKey(album.Artist, album.Title);
            if (!string.IsNullOrEmpty(trackWithArt?.AlbumArtPath))
            {
                _albumArtCache[albumKey] = new List<string> { trackWithArt.AlbumArtPath };
            }
        }

        // Group tracks by artist
        var artistGroups = Tracks.GroupBy(t => t.Artist);
        foreach (var group in artistGroups)
        {
            var artistTracks = group.ToList();
            var artist = new LibraryArtist
            {
                Name = group.Key,
                Songs = artistTracks
            };
            
            // Try to get artist image from an album art
            var trackWithArt = artistTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.AlbumArtPath));
            if (trackWithArt != null)
            {
                artist.ArtPath = trackWithArt.AlbumArtPath;
            }
            
            tempArtists.Add(artist);
        }

        // Update collections on UI thread once
        // Update collections on UI thread in bulk
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Albums.ReplaceAll(tempAlbums.OrderBy(x => x.Title));
            Artists.ReplaceAll(tempArtists.OrderBy(x => x.Name));
        });
    }

    public BitmapImage? GetAlbumArt(TrackModel? track, int decodeWidth = 0)
    {
        string? artPath = track?.AlbumArtPath;
        
        if (string.IsNullOrEmpty(artPath))
        {
            // Try to get from cache
            var albumKey = GetAlbumKey(track?.Artist ?? "Unknown Artist", track?.Album ?? "Unknown Album");
            if (_albumArtCache.TryGetValue(albumKey, out var artList) && artList.Count > 0)
            {
                artPath = artList[0];
            }
        }

        if (string.IsNullOrEmpty(artPath) || !File.Exists(artPath))
            return null;

        return GetBitmapImageFromFile(artPath, decodeWidth);
    }

    private BitmapImage? GetBitmapImageFromFile(string path, int decodeWidth = 0)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.CacheOption = BitmapCacheOption.OnLoad;
            
            if (decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }
            
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to load album art image");
            return null;
        }
    }

    private BitmapImage? GetBitmapImage(byte[]? imageData, int decodeWidth = 0)
    {
        if (imageData == null || imageData.Length == 0) return null;

        try
        {
            using var mem = new MemoryStream(imageData);
            mem.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            image.CacheOption = BitmapCacheOption.OnLoad;
            
            if (decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }
            
            image.StreamSource = mem;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to load album art image");
            return null;
        }
    }

    /// <summary>
    /// Search tracks by title, artist, or album
    /// </summary>
    public IEnumerable<TrackModel> SearchTracks(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Tracks;

        query = query.ToLowerInvariant();
        return Tracks.Where(t =>
            t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            t.Album.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get tracks by artist
    /// </summary>
    public IEnumerable<TrackModel> GetTracksByArtist(string artist)
    {
        return Tracks.Where(t =>
            t.Artist.Equals(artist, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get tracks by album
    /// </summary>
    public IEnumerable<TrackModel> GetTracksByAlbum(string artist, string album)
    {
        return Tracks.Where(t =>
            t.Artist.Equals(artist, StringComparison.OrdinalIgnoreCase) &&
            t.Album.Equals(album, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Get sorted tracks
    /// </summary>
    public IEnumerable<TrackModel> GetSortedTracks(string sortBy, bool ascending = true)
    {
        var sorted = sortBy.ToLowerInvariant() switch
        {
            "title" => Tracks.OrderBy(t => t.Title),
            "artist" => Tracks.OrderBy(t => t.Artist).ThenBy(t => t.Album).ThenBy(t => t.TrackNumber),
            "album" => Tracks.OrderBy(t => t.Album).ThenBy(t => t.TrackNumber),
            "duration" => Tracks.OrderBy(t => t.Duration),
            "dateadded" => Tracks.OrderBy(t => t.FilePath), // Approximation
            _ => Tracks.OrderBy(t => t.Title)
        };

        return ascending ? sorted : sorted.Reverse();
    }

    /// <summary>
    /// Get all tracks as a list (for queue creation)
    /// </summary>
    public List<TrackModel> GetAllTracks()
    {
        return Tracks.ToList();
    }

    /// <summary>
    /// Get track by index
    /// </summary>
    public TrackModel? GetTrack(int index)
    {
        if (index >= 0 && index < Tracks.Count)
        {
            return Tracks[index];
        }
        return null;
    }

    /// <summary>
    /// Get track index
    /// </summary>
    public int GetTrackIndex(TrackModel track)
    {
        if (track == null) return -1;
        for (int i = 0; i < Tracks.Count; i++)
        {
            if (Tracks[i].FilePath == track.FilePath)
            {
                return i;
            }
        }
        return -1;
    }
    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        _watchers.Clear();
    }
}
