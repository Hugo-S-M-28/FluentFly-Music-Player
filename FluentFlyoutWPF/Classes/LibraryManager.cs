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
    private readonly SemaphoreSlim _scanSemaphore = new(1, 1);
    private CancellationTokenSource? _debounceCts;
    private CancellationTokenSource? _derivedRefreshCts;
    private CancellationTokenSource? _saveLibraryCts;
    private readonly object _watcherLock = new();
    private readonly Dictionary<string, TrackModel> _trackIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingLibraryChange> _pendingChanges = new();
    private readonly object _pendingChangesLock = new();

    public static LibraryManager Instance => _instance ??= new LibraryManager();

    public BulkObservableCollection<TrackModel> Tracks { get; } = new();
    public BulkObservableCollection<LibraryAlbum> Albums { get; } = new();
    public BulkObservableCollection<LibraryArtist> Artists { get; } = new();

    public event EventHandler<TrackModel>? TrackMetadataUpdated;
    public event EventHandler<TrackModel>? TrackLyricsUpdated;

    private readonly Dictionary<string, List<string>> _albumArtCache = new();
    private readonly string[] _supportedExtensions = AudioReaderFactory.GetSupportedExtensions();
    private static string ArtCachePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FluentFlyout", "ArtCache");

    private static string LibraryFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentFlyout",
        "library.json"
    );

    private enum PendingLibraryChangeKind
    {
        Upsert,
        Remove,
        Rename
    }

    private sealed record PendingLibraryChange(PendingLibraryChangeKind Kind, string Path, string? OldPath = null);

    public async Task InitializeAsync()
    {
        if (!Directory.Exists(ArtCachePath)) Directory.CreateDirectory(ArtCachePath);
        
        // Load existing library from JSON first (very fast)
        await LoadLibraryAsync();
        await RefreshDerivedCollectionsAsync(); // Initial build from cache on background thread

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
                watcher.Changed += OnFileChanged;
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
        if (e is RenamedEventArgs renamedEventArgs)
        {
            QueueLibraryChange(new PendingLibraryChange(PendingLibraryChangeKind.Rename, renamedEventArgs.FullPath, renamedEventArgs.OldFullPath));
            return;
        }

        var kind = e.ChangeType == WatcherChangeTypes.Deleted
            ? PendingLibraryChangeKind.Remove
            : PendingLibraryChangeKind.Upsert;

        QueueLibraryChange(new PendingLibraryChange(kind, e.FullPath));
    }

    private void QueueLibraryChange(PendingLibraryChange change)
    {
        lock (_watcherLock)
        {
            _debounceCts?.Cancel();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;
            lock (_pendingChangesLock)
            {
                _pendingChanges.Add(change);
            }

            Task.Delay(2000, token).ContinueWith(async t =>
            {
                if (t.IsCanceled) return;
                
                try
                {
                    await ProcessPendingChangesAsync();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error processing pending library changes");
                }
            }, token);
        }
    }

    private async Task LoadLibraryAsync()
    {
        try
        {
            if (File.Exists(LibraryFilePath))
            {
                var json = await File.ReadAllTextAsync(LibraryFilePath);
                var tracks = await Task.Run(() =>
                {
                    var list = System.Text.Json.JsonSerializer.Deserialize<List<TrackModel>>(json);
                    if (list != null)
                    {
                        foreach (var track in list)
                        {
                            track.RefreshSearchIndex();
                        }
                    }
                    return list;
                });

                if (tracks != null)
                {
                    // Load tracks in a single batch to minimize UI thread overhead
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Tracks.ReplaceAll(tracks);
                    });
                    RebuildTrackIndex();
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
                WriteIndented = false,
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
        
        if (!await _scanSemaphore.WaitAsync(0))
        {
            // Already scanning, skip this request
            return;
        }

        _isScanning = true;
        try
        {
            var folders = SettingsManager.Current.MusicLibraryFolders;
            if (folders == null || folders.Count == 0) 
            {
                return;
            }

            var filesInLibrary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folderList = folders.ToList();
            var tracksToUpsert = new List<TrackModel>();

            // Enumerate files safely on a background thread first
            var filesToProcess = new List<string>();
            await Task.Run(() =>
            {
                foreach (var folder in folderList)
                {
                    if (Directory.Exists(folder))
                    {
                        filesToProcess.AddRange(SafeEnumerateFiles(folder));
                    }
                }
            });

            // Process metadata extraction in parallel
            await Task.Run(() =>
            {
                Parallel.ForEach(filesToProcess, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, filePath =>
                {
                    lock (filesInLibrary)
                    {
                        filesInLibrary.Add(filePath);
                    }

                    if (NeedsMetadataRefresh(filePath))
                    {
                        var track = ExtractMetadata(filePath);
                        if (track != null)
                        {
                            lock (tracksToUpsert)
                            {
                                tracksToUpsert.Add(track);
                            }
                        }
                    }
                });
            });

            var indexedPaths = _trackIndex.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tracksToRemove = indexedPaths.Except(filesInLibrary, StringComparer.OrdinalIgnoreCase).ToList();

            await ApplyTrackChangesAsync(tracksToUpsert, tracksToRemove);
        }
        finally
        {
            _isScanning = false;
            _scanSemaphore.Release();
        }
    }

    private IEnumerable<string> SafeEnumerateFiles(string path)
    {
        var files = new List<string>();
        SafeEnumerateFilesInternal(path, files);
        return files;
    }

    private void SafeEnumerateFilesInternal(string path, List<string> result)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path))
            {
                var ext = Path.GetExtension(file);
                if (!string.IsNullOrEmpty(ext) && _supportedExtensions.Contains(ext.ToLowerInvariant()) && !IsPathExcluded(file))
                {
                    result.Add(file);
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                SafeEnumerateFilesInternal(dir, result);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore folders without permissions
        }
        catch (DirectoryNotFoundException)
        {
            // Ignore directories deleted mid-scan
        }
        catch (PathTooLongException)
        {
            // Ignore extremely long paths
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"Error traversing directory: {path}");
        }
    }

    public TrackModel ExtractMetadata(string filePath)
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
                HasLyrics = !string.IsNullOrWhiteSpace(file.Tag?.Lyrics),
                FileModifiedUtcTicks = File.GetLastWriteTimeUtc(filePath).Ticks
            };
            track.RefreshSearchIndex();

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
                        try
                        {
                            File.WriteAllBytes(cacheFile, picture.Data.Data);
                        }
                        catch (IOException)
                        {
                            // In case another thread is writing/wrote the file
                        }
                    }
                    
                    track.AlbumArtPath = cacheFile;
                    
                    // Cache by album key for quick lookup
                    var albumKey = GetAlbumKey(artist, album);
                    lock (_albumArtCache)
                    {
                        if (!_albumArtCache.ContainsKey(albumKey))
                        {
                            _albumArtCache[albumKey] = new List<string>();
                        }
                        _albumArtCache[albumKey].Add(cacheFile);
                    }
                }
            }

            return track;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to read metadata for {filePath}");
            var fallbackTrack = new TrackModel
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath),
                Artist = "Unknown Artist",
                Album = "Unknown Album"
            };
            fallbackTrack.RefreshSearchIndex();
            return fallbackTrack;
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
                // Increased delay for .m4a and .flac files which often use MediaFoundation
                await Task.Delay(500);
            }

            await Task.Run(() =>
            {
                // Ensure file is not read-only
                var attributes = File.GetAttributes(track.FilePath);
                if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                {
                    File.SetAttributes(track.FilePath, attributes & ~FileAttributes.ReadOnly);
                }

                var file = TagLib.File.Create(track.FilePath);
                try
                {
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
                    catch (Exception ex) when (attempts < 5 && (ex is UnauthorizedAccessException || ex is IOException))
                    {
                        attempts++;
                        file.Dispose();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        System.Threading.Thread.Sleep(500 * attempts);
                        
                        // Re-open and re-apply tags for next attempt
                        file = TagLib.File.Create(track.FilePath);
                        file.Tag.Title = newTitle;
                        file.Tag.Performers = performersList.ToArray();
                        file.Tag.Album = newAlbum;
                        file.Tag.AlbumArtists = new[] { newArtist };
                        file.Tag.Genres = new[] { newGenre };
                        file.Tag.Track = (uint)Math.Max(0, newTrackNumber);
                    }
                    catch
                    {
                        throw;
                    }
                }
                }
                finally
                {
                    file?.Dispose();
                }
            });

            // Update model in memory
            track.Title = newTitle;
            track.Artist = newArtist;
            track.Collaborators = newCollaborators;
            track.Album = newAlbum;
            track.Genre = newGenre;
            track.TrackNumber = newTrackNumber;
            track.FileModifiedUtcTicks = File.GetLastWriteTimeUtc(track.FilePath).Ticks;
            track.RefreshSearchIndex();

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
            ScheduleLibrarySave();
            ScheduleDerivedCollectionsRefresh();
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
                await Task.Delay(500);
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

                var file = TagLib.File.Create(track.FilePath);
                try
                {
                    file.Tag.Lyrics = lyricsText;
                
                int attempts = 0;
                while (true)
                {
                    try
                    {
                        file.Save();
                        break;
                    }
                    catch (Exception ex) when (attempts < 5 && (ex is UnauthorizedAccessException || ex is IOException))
                    {
                        attempts++;
                        file.Dispose();
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        System.Threading.Thread.Sleep(500 * attempts);
                        file = TagLib.File.Create(track.FilePath);
                        file.Tag.Lyrics = lyricsText;
                    }
                    catch
                    {
                        throw;
                    }
                }
                }
                finally
                {
                    file?.Dispose();
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
            track.FileModifiedUtcTicks = File.GetLastWriteTimeUtc(track.FilePath).Ticks;
            
            // Re-save library and notify UI
            ScheduleLibrarySave();
            TrackLyricsUpdated?.Invoke(this, track);
            ScheduleDerivedCollectionsRefresh();

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
        if (data.Length <= 32 * 1024)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        else
        {
            // For larger data, compute MD5 of a combined sample: first 16KB + last 16KB + data length
            var sample = new byte[32 * 1024 + sizeof(int)];
            Array.Copy(data, 0, sample, 0, 16 * 1024);
            Array.Copy(data, data.Length - 16 * 1024, sample, 16 * 1024, 16 * 1024);
            byte[] lengthBytes = BitConverter.GetBytes(data.Length);
            Array.Copy(lengthBytes, 0, sample, 32 * 1024, lengthBytes.Length);
            
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(sample);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    private string GetAlbumKey(string artist, string album)
    {
        return $"{artist.ToLowerInvariant()}::{album.ToLowerInvariant()}";
    }

    public void RebuildAlbumsAndArtists()
    {
        RefreshDerivedCollectionsAsync().GetAwaiter().GetResult();
    }

    private async Task RefreshDerivedCollectionsAsync()
    {
        var trackSnapshot = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Tracks.ToList());
        var snapshot = await Task.Run(() =>
        {
            var tempAlbums = new List<LibraryAlbum>();
            var tempArtists = new List<LibraryArtist>();
            var albumArtCache = new Dictionary<string, List<string>>();

            var albumGroups = trackSnapshot.GroupBy(t => new { t.Artist, t.Album });
            foreach (var group in albumGroups)
            {
                var albumTracks = group.ToList();
                var album = new LibraryAlbum
                {
                    Title = group.Key.Album,
                    Artist = group.Key.Artist,
                    Songs = albumTracks.OrderBy(t => t.TrackNumber).ToList(),
                    HasLyrics = albumTracks.Any(t => t.HasLyrics)
                };
                album.RefreshSearchIndex();

                var trackWithArt = albumTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.AlbumArtPath));
                if (trackWithArt != null)
                {
                    album.ArtPath = trackWithArt.AlbumArtPath;
                }

                tempAlbums.Add(album);

                var albumKey = GetAlbumKey(album.Artist, album.Title);
                if (!string.IsNullOrEmpty(trackWithArt?.AlbumArtPath))
                {
                    albumArtCache[albumKey] = [trackWithArt.AlbumArtPath];
                }
            }

            var artistGroups = trackSnapshot.GroupBy(t => t.Artist);
            foreach (var group in artistGroups)
            {
                var artistTracks = group.ToList();
                var artist = new LibraryArtist
                {
                    Name = group.Key,
                    Songs = artistTracks,
                    HasLyrics = artistTracks.Any(t => t.HasLyrics)
                };
                artist.RefreshSearchIndex();

                var trackWithArt = artistTracks.FirstOrDefault(t => !string.IsNullOrEmpty(t.AlbumArtPath));
                if (trackWithArt != null)
                {
                    artist.ArtPath = trackWithArt.AlbumArtPath;
                }

                tempArtists.Add(artist);
            }

            return (Albums: tempAlbums, Artists: tempArtists, AlbumArtCache: albumArtCache);
        });

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            lock (_albumArtCache)
            {
                _albumArtCache.Clear();
                foreach (var entry in snapshot.AlbumArtCache)
                {
                    _albumArtCache[entry.Key] = entry.Value;
                }
            }

            Albums.ReplaceAll(snapshot.Albums.OrderBy(x => x.Title));
            Artists.ReplaceAll(snapshot.Artists.OrderBy(x => x.Name));
        });
    }

    public BitmapImage? GetAlbumArt(TrackModel? track, int decodeWidth = 0)
    {
        string? artPath = track?.AlbumArtPath;
        
        if (string.IsNullOrEmpty(artPath))
        {
            // Try to get from cache
            var albumKey = GetAlbumKey(track?.Artist ?? "Unknown Artist", track?.Album ?? "Unknown Album");
            lock (_albumArtCache)
            {
                if (_albumArtCache.TryGetValue(albumKey, out var artList) && artList.Count > 0)
                {
                    artPath = artList[0];
                }
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

    public bool IsPathExcluded(string path)
    {
        var exclusions = SettingsManager.Current.ExcludedLibraryPaths;
        if (exclusions == null || exclusions.Count == 0) return false;

        string normalizedPath = Path.GetFullPath(path).Replace('\\', '/');
        foreach (var exclusion in exclusions)
        {
            string normalizedExclusion = Path.GetFullPath(exclusion).Replace('\\', '/');
            if (normalizedPath.Equals(normalizedExclusion, StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.StartsWith(normalizedExclusion + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private bool IsInMonitoredFolders(string path)
    {
        var folders = SettingsManager.Current.MusicLibraryFolders;
        if (folders == null || folders.Count == 0) return false;

        string normalizedPath = Path.GetFullPath(path).Replace('\\', '/');
        foreach (var folder in folders)
        {
            string normalizedFolder = Path.GetFullPath(folder).Replace('\\', '/');
            if (normalizedPath.StartsWith(normalizedFolder + "/", StringComparison.OrdinalIgnoreCase) ||
                normalizedPath.Equals(normalizedFolder, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public void CleanAndRefreshLibrary()
    {
        var tracksToRemove = Tracks.Where(t => !File.Exists(t.FilePath) || IsPathExcluded(t.FilePath) || !IsInMonitoredFolders(t.FilePath)).ToList();
        if (tracksToRemove.Count > 0)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var track in tracksToRemove)
                {
                    Tracks.Remove(track);
                }
            });
            RebuildTrackIndex();
            ScheduleLibrarySave();
            ScheduleDerivedCollectionsRefresh();
        }
    }

    private bool NeedsMetadataRefresh(string filePath)
    {
        if (!_trackIndex.TryGetValue(filePath, out var existingTrack))
            return true;

        return existingTrack.FileModifiedUtcTicks != File.GetLastWriteTimeUtc(filePath).Ticks;
    }

    private async Task ProcessPendingChangesAsync()
    {
        List<PendingLibraryChange> pendingChanges;
        lock (_pendingChangesLock)
        {
            pendingChanges = [.. _pendingChanges];
            _pendingChanges.Clear();
        }

        if (pendingChanges.Count == 0)
            return;

        var removePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var upsertPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in pendingChanges)
        {
            switch (change.Kind)
            {
                case PendingLibraryChangeKind.Remove:
                    removePaths.Add(change.Path);
                    upsertPaths.Remove(change.Path);
                    break;
                case PendingLibraryChangeKind.Rename:
                    if (!string.IsNullOrWhiteSpace(change.OldPath))
                    {
                        removePaths.Add(change.OldPath);
                        upsertPaths.Remove(change.OldPath);
                    }
                    if (IsSupportedLibraryFile(change.Path))
                    {
                        upsertPaths.Add(change.Path);
                    }
                    break;
                default:
                    if (IsSupportedLibraryFile(change.Path))
                    {
                        upsertPaths.Add(change.Path);
                        removePaths.Remove(change.Path);
                    }
                    break;
            }
        }

        var tracksToUpsert = await Task.Run(() =>
        {
            var updatedTracks = new List<TrackModel>();
            foreach (var path in upsertPaths)
            {
                if (!File.Exists(path) || IsPathExcluded(path) || !IsInMonitoredFolders(path))
                    continue;

                if (!NeedsMetadataRefresh(path))
                    continue;

                var extracted = ExtractMetadata(path);
                if (extracted != null)
                {
                    updatedTracks.Add(extracted);
                }
            }

            return updatedTracks;
        });

        await ApplyTrackChangesAsync(tracksToUpsert, removePaths);
    }

    private async Task ApplyTrackChangesAsync(IReadOnlyList<TrackModel> tracksToUpsert, IEnumerable<string> trackPathsToRemove)
    {
        var removeSet = trackPathsToRemove.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool hasChanges = removeSet.Count > 0 || tracksToUpsert.Count > 0;
        if (!hasChanges)
            return;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (removeSet.Count > 0)
            {
                var remainingTracks = Tracks.Where(t => !removeSet.Contains(t.FilePath)).ToList();
                Tracks.ReplaceAll(remainingTracks);
            }

            var tracksToAdd = new List<TrackModel>();
            foreach (var updatedTrack in tracksToUpsert)
            {
                if (_trackIndex.TryGetValue(updatedTrack.FilePath, out var existingTrack))
                {
                    UpdateTrack(existingTrack, updatedTrack);
                }
                else
                {
                    tracksToAdd.Add(updatedTrack);
                }
            }

            if (tracksToAdd.Count > 0)
            {
                Tracks.AddRange(tracksToAdd);
            }

            RebuildTrackIndex();
        });

        ScheduleLibrarySave();
        ScheduleDerivedCollectionsRefresh();
    }

    private void UpdateTrack(TrackModel target, TrackModel source)
    {
        target.Title = source.Title;
        target.Artist = source.Artist;
        target.Collaborators = source.Collaborators;
        target.Album = source.Album;
        target.Duration = source.Duration;
        target.TrackNumber = source.TrackNumber;
        target.Genre = source.Genre;
        target.Lyrics = source.Lyrics;
        target.HasLyrics = source.HasLyrics;
        target.AlbumArtPath = source.AlbumArtPath;
        target.FileModifiedUtcTicks = source.FileModifiedUtcTicks;
        target.RefreshSearchIndex();
        TrackMetadataUpdated?.Invoke(this, target);
    }

    private void RebuildTrackIndex()
    {
        _trackIndex.Clear();
        foreach (var track in Tracks)
        {
            _trackIndex[track.FilePath] = track;
        }
    }

    private void ScheduleDerivedCollectionsRefresh(int delayMilliseconds = 500)
    {
        _derivedRefreshCts?.Cancel();
        _derivedRefreshCts = new CancellationTokenSource();
        var token = _derivedRefreshCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMilliseconds, token);
                if (!token.IsCancellationRequested)
                {
                    await RefreshDerivedCollectionsAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to refresh derived library collections");
            }
        }, token);
    }

    private void ScheduleLibrarySave(int delayMilliseconds = 1000)
    {
        _saveLibraryCts?.Cancel();
        _saveLibraryCts = new CancellationTokenSource();
        var token = _saveLibraryCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMilliseconds, token);
                if (!token.IsCancellationRequested)
                {
                    await SaveLibraryAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save music library");
            }
        }, token);
    }

    private bool IsSupportedLibraryFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return _supportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
        _watchers.Clear();
        _debounceCts?.Dispose();
        _derivedRefreshCts?.Dispose();
        _saveLibraryCts?.Dispose();
        _scanSemaphore.Dispose();
    }
}
