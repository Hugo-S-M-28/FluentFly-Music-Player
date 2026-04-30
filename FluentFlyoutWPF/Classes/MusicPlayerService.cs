using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using Windows.Media;
using Windows.Storage.Streams;
using System.Threading.Tasks;
using NAudio.Wave;
using FluentFlyoutWPF.Models;
using FluentFlyout.Classes.Settings;

namespace FluentFlyoutWPF.Classes;

public enum RepeatMode
{
    None,
    One,
    All
}

public class MusicPlayerService : INotifyPropertyChanged, IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static MusicPlayerService? _instance;
    private static readonly object _instanceLock = new();

    public static MusicPlayerService Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_instanceLock)
                {
                    _instance ??= new MusicPlayerService();
                }
            }
            return _instance;
        }
    }

    private IWavePlayer? _waveOut;
    private WaveStream? _audioStream;
    private SampleAggregator? _sampleAggregator;
    private DispatcherTimer? _positionTimer;

    private readonly Random _random = new();
    private volatile bool _manualStop = false;

    // Lyrics
    private readonly LyricsService _lyricsService = new();
    private List<LyricLine> _currentLyrics = new();
    private LyricLine? _currentLyricLine;

    // Queue management
    private List<TrackModel> _originalQueue = new();
    private List<TrackModel> _shuffledQueue = new();
    private int _currentQueueIndex = -1;
    private bool _isShuffleEnabled;
    private RepeatMode _repeatMode = RepeatMode.None;
    private List<TrackModel> _history = new();
    private const int MAX_HISTORY = 50;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? TrackEnded;
    public event EventHandler? TrackChanged;
    public event EventHandler? QueueChanged;
    public event EventHandler<string>? PlaybackError;
    public event EventHandler<float[]>? FftDataAvailable;


    public LyricLine? CurrentLyricLine
    {
        get => _currentLyricLine;
        private set
        {
            if (_currentLyricLine?.Text != value?.Text)
            {
                _currentLyricLine = value;
                OnPropertyChanged(nameof(CurrentLyricLine));
            }
        }
    }

    public List<LyricLine> CurrentLyrics => _currentLyrics;

    private TrackModel? _currentTrack;
    public TrackModel? CurrentTrack
    {
        get => _currentTrack;
        set
        {
            if (_currentTrack != value)
            {
                _currentTrack = value;
                if (value != null && value.AlbumArt == null)
                {
                    value.AlbumArt = LibraryManager.Instance.GetAlbumArt(value);
                }
                OnPropertyChanged(nameof(CurrentTrack));
                SmtcService.Instance.UpdateMetadata(value, CurrentPosition, TotalDuration);
                LoadLyrics(value);
                OnPropertyChanged(nameof(CurrentLyricLine)); // Notify lyrics cleared/changed
                TrackChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying != value)
            {
                _isPlaying = value;
                
                if (_isPlaying)
                {
                    Volume = SettingsManager.Current.Volume;
                }

                OnPropertyChanged(nameof(IsPlaying));
                SmtcService.Instance.UpdatePlaybackStatus(value);
            }
        }
    }

    public TimeSpan CurrentPosition
    {
        get => _audioStream?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_audioStream != null && value >= TimeSpan.Zero && value <= TotalDuration)
            {
                _audioStream.CurrentTime = value;
                OnPropertyChanged(nameof(CurrentPosition));
            }
        }
    }

    public TimeSpan TotalDuration => _audioStream?.TotalTime ?? TimeSpan.Zero;

    private float _volume = 1.0f;
    public float Volume
    {
        get => _volume;
        set
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            if (Math.Abs(_volume - clamped) < 0.001f) return;

            _volume = clamped;
            OnPropertyChanged(nameof(Volume));

            AppVolumeService.Instance.SetVolume(_volume);

            SettingsManager.Current.Volume = _volume;
            SettingsManager.SaveSettings();
        }
    }

    public bool CanGoNext => _currentQueueIndex < (_isShuffleEnabled ? _shuffledQueue.Count : _originalQueue.Count) - 1 || _repeatMode != RepeatMode.None;
    public bool CanGoPrevious => _history.Count > 0 || _currentQueueIndex > 0;

    public bool IsShuffleEnabled
    {
        get => _isShuffleEnabled;
        set
        {
            if (_isShuffleEnabled != value)
            {
                _isShuffleEnabled = value;
                if (value)
                {
                    CreateShuffledQueue();
                }
                else
                {
                    if (CurrentTrack != null)
                    {
                        var idx = _originalQueue.FindIndex(t => t.FilePath == CurrentTrack.FilePath);
                        if (idx >= 0) _currentQueueIndex = idx;
                    }
                }
                OnPropertyChanged(nameof(IsShuffleEnabled));
                UpdateSmtcShuffleRepeat();
                QueueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (_repeatMode != value)
            {
                _repeatMode = value;
                OnPropertyChanged(nameof(RepeatMode));
                UpdateSmtcShuffleRepeat();
            }
        }
    }

    public IReadOnlyList<TrackModel> CurrentQueue => _isShuffleEnabled ? _shuffledQueue : _originalQueue;
    public int CurrentQueueIndex => _currentQueueIndex;

    /// <summary>
    /// Move a track from one position to another in the active queue.
    /// Adjusts the current playback index so the playing track is not disrupted.
    /// </summary>
    public void MoveTrack(int oldIndex, int newIndex)
    {
        var queue = _isShuffleEnabled ? _shuffledQueue : _originalQueue;

        if (oldIndex < 0 || oldIndex >= queue.Count || newIndex < 0 || newIndex >= queue.Count || oldIndex == newIndex)
            return;

        var track = queue[oldIndex];
        queue.RemoveAt(oldIndex);
        queue.Insert(newIndex, track);

        // Adjust _currentQueueIndex to follow the currently playing track
        if (_currentQueueIndex == oldIndex)
        {
            // The playing track was the one moved
            _currentQueueIndex = newIndex;
        }
        else if (oldIndex < _currentQueueIndex && newIndex >= _currentQueueIndex)
        {
            // Moved a track from before the current to after — current shifts down
            _currentQueueIndex--;
        }
        else if (oldIndex > _currentQueueIndex && newIndex <= _currentQueueIndex)
        {
            // Moved a track from after the current to before — current shifts up
            _currentQueueIndex++;
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    private MusicPlayerService()
    {
        _positionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _positionTimer.Tick += PositionTimer_Tick;

        // Restore volume from settings
        _volume = SettingsManager.Current.Volume;

        // Setup SMTC events
        SmtcService.Instance.PlayRequested += (s, e) => System.Windows.Application.Current.Dispatcher.Invoke(Play);
        SmtcService.Instance.PauseRequested += (s, e) => System.Windows.Application.Current.Dispatcher.Invoke(Pause);
        SmtcService.Instance.NextRequested += (s, e) => System.Windows.Application.Current.Dispatcher.Invoke(() => PlayNext());
        SmtcService.Instance.PreviousRequested += (s, e) => System.Windows.Application.Current.Dispatcher.Invoke(() => PlayPrevious());
        SmtcService.Instance.PositionChangeRequested += (s, pos) => System.Windows.Application.Current.Dispatcher.Invoke(() => Seek(pos));
        SmtcService.Instance.ShuffleChangeRequested += (s, shuffle) => System.Windows.Application.Current.Dispatcher.Invoke(() => IsShuffleEnabled = shuffle);
        SmtcService.Instance.RepeatModeChangeRequested += (s, mode) => System.Windows.Application.Current.Dispatcher.Invoke(() => 
        {
            RepeatMode = mode switch
            {
                MediaPlaybackAutoRepeatMode.Track => RepeatMode.One,
                MediaPlaybackAutoRepeatMode.List => RepeatMode.All,
                _ => RepeatMode.None
            };
        });

        // Listen for metadata updates from external editing windows
        LibraryManager.Instance.TrackMetadataUpdated += (s, track) => 
        {
            if (CurrentTrack?.FilePath == track.FilePath)
            {
                SmtcService.Instance.UpdateMetadata(track, CurrentPosition, TotalDuration);
                OnPropertyChanged(nameof(CurrentTrack));
                TrackChanged?.Invoke(this, EventArgs.Empty);
            }
        };

        LibraryManager.Instance.TrackLyricsUpdated += (s, track) => 
        {
            if (CurrentTrack?.FilePath == track.FilePath)
            {
                LoadLyrics(track);
                OnPropertyChanged(nameof(CurrentLyricLine));
            }
        };

        // Two-way sync with Windows Volume Mixer
        AppVolumeService.Instance.VolumeChanged += (s, vol) => 
        {
            if (Math.Abs(_volume - vol) > 0.01f) // Avoid feedback loop
            {
                _volume = vol;
                OnPropertyChanged(nameof(Volume));
                SettingsManager.Current.Volume = _volume;
                SettingsManager.SaveSettings();
            }
        };
    }

    private void UpdateSmtcShuffleRepeat()
    {
        if (!SettingsManager.Current.SystemMediaControlEnabled) return;
        
        var smtcRepeat = _repeatMode switch
        {
            RepeatMode.One => MediaPlaybackAutoRepeatMode.Track,
            RepeatMode.All => MediaPlaybackAutoRepeatMode.List,
            _ => MediaPlaybackAutoRepeatMode.None
        };
        SmtcService.Instance.UpdateShuffleRepeat(_isShuffleEnabled, smtcRepeat);
    }



    private void LoadLyrics(TrackModel? track)
    {
        _currentLyricLine = null;
        _currentLyrics.Clear();
        if (track == null) return;

        // Try to find external .lrc file first
        var lrcPath = _lyricsService.FindLrcFile(track.FilePath);
        if (lrcPath != null)
        {
            _currentLyrics = _lyricsService.ParseLrc(lrcPath);
        }
        
        // If no external lyrics, try to parse internal lyrics from metadata
        if (_currentLyrics.Count == 0 && !string.IsNullOrWhiteSpace(track.Lyrics))
        {
            _currentLyrics = _lyricsService.ParseLrcText(track.Lyrics);
            
            // If still no lines (maybe it was plain text), create a single line at 0:00
            if (_currentLyrics.Count == 0 && !string.IsNullOrWhiteSpace(track.Lyrics))
            {
                _currentLyrics.Add(new LyricLine { Time = TimeSpan.Zero, Text = track.Lyrics });
            }
        }
    }





    private long _lastPosition;
    private int _stallCount;
    private void PositionTimer_Tick(object? sender, EventArgs e)
    {
        if (IsPlaying && _audioStream != null)
        {
            OnPropertyChanged(nameof(CurrentPosition));

            // Update Lyrics
            UpdateCurrentLyricLine();

            // Update SMTC Timeline occasionally
            if (SettingsManager.Current.SystemMediaControlEnabled)
            {
                SmtcService.Instance.UpdateTimeline(CurrentPosition, TotalDuration);
            }

            // Stall detection (Silence/Format error fallback)
            if (_audioStream.Position == _lastPosition && IsPlaying)
            {
                _stallCount++;
                if (_stallCount > 10) // ~5 seconds of stall
                {
                    Logger.Warn("Playback stall detected");
                    PlaybackError?.Invoke(this, "Playback stall detected. The audio file might be corrupted or format is unsupported.");
                    Stop();
                    _stallCount = 0;
                }
            }
            else
            {
                _lastPosition = _audioStream.Position;
                _stallCount = 0;
            }
        }
    }

    private void UpdateCurrentLyricLine()
    {
        if (_currentLyrics.Count == 0) return;

        var currentTime = CurrentPosition;
        var line = _currentLyrics.LastOrDefault(l => l.Time <= currentTime);
        CurrentLyricLine = line.Text != null ? line : null;
    }

    /// <summary>
    /// Play a single track (resolves queue index by FilePath for external callers)
    /// </summary>
    public void Play(TrackModel track)
    {
        if (track == null || !File.Exists(track.FilePath))
        {
            Logger.Warn("Cannot play track: file not found or track is null");
            return;
        }

        if (!SettingsManager.Current.InternalPlayerEnabled)
        {
            Logger.Warn("Internal player is disabled in settings. Ignoring play request.");
            return;
        }

        // Update queue index to match the track being played
        var activeQueue = _isShuffleEnabled ? _shuffledQueue : _originalQueue;
        bool indexAlreadyCorrect = _currentQueueIndex >= 0 && 
                                 _currentQueueIndex < activeQueue.Count && 
                                 ReferenceEquals(activeQueue[_currentQueueIndex], track);

        if (!indexAlreadyCorrect)
        {
            // Use reference equality first, then fall back to FilePath
            _currentQueueIndex = activeQueue.FindIndex(t => ReferenceEquals(t, track));
            if (_currentQueueIndex < 0)
            {
                _currentQueueIndex = activeQueue.FindIndex(t => t.FilePath == track.FilePath);
            }
        }

        PlayTrackDirectly(track);
    }

    /// <summary>
    /// Play a track at a specific index in the current active queue.
    /// Use this when the caller already knows the exact position.
    /// </summary>
    public void PlayAtIndex(int index)
    {
        var queue = _isShuffleEnabled ? _shuffledQueue : _originalQueue;
        if (index < 0 || index >= queue.Count)
        {
            Logger.Warn($"PlayAtIndex: index {index} out of range (queue size: {queue.Count})");
            return;
        }

        _currentQueueIndex = index;
        PlayTrackDirectly(queue[index]);
    }

    /// <summary>
    /// Core playback method. Plays the given track WITHOUT modifying _currentQueueIndex.
    /// The caller is responsible for setting the index before calling this.
    /// </summary>
    private void PlayTrackDirectly(TrackModel track)
    {
        if (track == null || !File.Exists(track.FilePath))
        {
            Logger.Warn("Cannot play track: file not found or track is null");
            return;
        }

        if (!SettingsManager.Current.InternalPlayerEnabled)
        {
            Logger.Warn("Internal player is disabled in settings. Ignoring play request.");
            return;
        }

        // Stop current playback
        StopInternal();

        try
        {
            CurrentTrack = track;
            InitializePlayback(track.FilePath);

            _manualStop = false;
            _waveOut?.Play();
            IsPlaying = true;
            _positionTimer?.Start();

            // Add to history and increment play count
            track.PlayCount++;
            AddToHistory(track);

            Logger.Info($"Playing [{_currentQueueIndex}]: {track.Title} - {track.Artist}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to play track: {track.FilePath}");
            _currentQueueIndex = -1; // Reset on failure
            CleanupPlayback();
        }
    }

    /// <summary>
    /// Play a queue of tracks starting from a specific index
    /// </summary>
    public void PlayQueue(List<TrackModel> tracks, int startIndex = 0)
    {
        if (tracks == null || tracks.Count == 0 || startIndex < 0 || startIndex >= tracks.Count)
        {
            Logger.Warn("Invalid queue or start index");
            return;
        }

        _originalQueue = new List<TrackModel>(tracks);
        _currentQueueIndex = startIndex;

        if (_isShuffleEnabled)
        {
            CreateShuffledQueue();
            // Find the start track in shuffled queue using reference equality
            var startTrack = _originalQueue[startIndex];
            _currentQueueIndex = _shuffledQueue.FindIndex(t => ReferenceEquals(t, startTrack));
            if (_currentQueueIndex < 0) _currentQueueIndex = 0;
        }

        var trackToPlay = GetCurrentTrackFromQueue();
        QueueChanged?.Invoke(this, EventArgs.Empty);

        if (trackToPlay != null)
        {
            PlayTrackDirectly(trackToPlay);
        }
    }

    /// <summary>
    /// Play a single track and set the queue context.
    /// The allTracks list is always stored as the original (unshuffled) queue.
    /// </summary>
    public void PlaySingle(TrackModel track, List<TrackModel>? allTracks = null)
    {
        if (allTracks != null)
        {
            _originalQueue = new List<TrackModel>(allTracks);
            // Use reference equality first, then fall back to FilePath
            _currentQueueIndex = _originalQueue.FindIndex(t => ReferenceEquals(t, track));
            if (_currentQueueIndex < 0)
            {
                _currentQueueIndex = _originalQueue.FindIndex(t => t.FilePath == track.FilePath);
            }
            if (_currentQueueIndex < 0)
            {
                _originalQueue.Clear();
                _originalQueue.Add(track);
                _currentQueueIndex = 0;
            }

            if (_isShuffleEnabled)
            {
                CreateShuffledQueue();
            }
            else
            {
                _shuffledQueue.Clear();
            }
        }
        else
        {
            _originalQueue = new List<TrackModel> { track };
            _shuffledQueue = new List<TrackModel> { track };
            _currentQueueIndex = 0;
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
        PlayTrackDirectly(track);
    }

    /// <summary>
    /// Adds a track to the end of the current queue without interrupting playback.
    /// If nothing is playing, starts playback of this track.
    /// </summary>
    public void AddToQueue(TrackModel track)
    {
        if (track == null) return;

        if (_originalQueue.Count == 0)
        {
            // Nothing in queue, just play it
            PlaySingle(track);
            return;
        }

        _originalQueue.Add(track);

        if (_isShuffleEnabled)
        {
            _shuffledQueue.Add(track);
        }

        QueueChanged?.Invoke(this, EventArgs.Empty);
        Logger.Info($"Added to queue: {track.Title} - {track.Artist}");
    }

    /// <summary>
    /// Removes a track from the queue without interrupting playback.
    /// If the removed track is the currently playing one, advances to the next track.
    /// Adjusts _currentQueueIndex so the playing track is not disrupted.
    /// </summary>
    public void RemoveFromQueue(TrackModel track)
    {
        if (track == null) return;

        var queue = _isShuffleEnabled ? _shuffledQueue : _originalQueue;
        int removeIndex = queue.IndexOf(track);
        if (removeIndex < 0) return;

        bool isCurrentTrack = removeIndex == _currentQueueIndex;

        // Remove from both queues
        _originalQueue.Remove(track);
        if (_isShuffleEnabled)
            _shuffledQueue.Remove(track);

        // Refresh removeIndex after removal from active queue
        // Adjust _currentQueueIndex
        if (isCurrentTrack)
        {
            // The playing track was removed — play the next one
            // After removal, the track at _currentQueueIndex is already the "next" one
            // unless we were at the end
            var activeQueue = _isShuffleEnabled ? _shuffledQueue : _originalQueue;
            if (_currentQueueIndex >= activeQueue.Count)
                _currentQueueIndex = activeQueue.Count - 1;

            if (_currentQueueIndex >= 0)
            {
                QueueChanged?.Invoke(this, EventArgs.Empty);
                PlayTrackDirectly(activeQueue[_currentQueueIndex]);
            }
            else
            {
                // Queue is now empty
                Stop();
                CurrentTrack = null;
                QueueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            // Recalculate index: if we removed something before the current, shift down
            if (removeIndex < _currentQueueIndex)
                _currentQueueIndex--;

            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        Logger.Info($"Removed from queue: {track.Title} - {track.Artist}");
    }

    private void InitializePlayback(string filePath)
    {
        try
        {
            var extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            ISampleProvider sampleProvider;

            // AudioFileReader assumes 16-bit PCM output from MediaFoundationReader.
            // If the FLAC/M4A is 24-bit, it causes heavy interference. 
            // So we explicitly use MediaFoundationReader and .ToSampleProvider() which handles 24-bit/32-bit correctly.
            if (extension == ".flac" || extension == ".m4a")
            {
                var mfReader = new MediaFoundationReader(filePath);
                _audioStream = mfReader;
                sampleProvider = mfReader.ToSampleProvider();
            }
            else
            {
                var afr = new AudioFileReader(filePath);
                _audioStream = afr;
                sampleProvider = afr;
            }

            // Apply equalizer processing
            var equalized = new EqualizerSampleProvider(sampleProvider);

            // Wrap with aggregator for FFT (after EQ so visualizer shows equalized output)
            _sampleAggregator = new SampleAggregator(equalized);
            _sampleAggregator.FftCalculated += (s, data) => FftDataAvailable?.Invoke(this, data);

            _waveOut = new WaveOutEvent();
            _waveOut.Init(_sampleAggregator);
            _waveOut.PlaybackStopped += WaveOut_PlaybackStopped;

            SmtcService.Instance.UpdatePlaybackStatus(true);
            
            // Refresh session to ensure AppVolumeService binds to the new audio stream
            AppVolumeService.Instance.RefreshSession();
            AppVolumeService.Instance.SetVolume(Volume);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to initialize playback for {filePath}");
            CleanupPlayback();
            throw; // Re-throw to be caught by the caller (like UpdateTrackMetadataAsync)
        }
    }

    private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Logger.Error(e.Exception, "Playback stopped due to error");
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                IsPlaying = false;
                _positionTimer?.Stop();
                CleanupPlayback();
            });
            return;
        }

        // Check if track ended naturally — marshal to the UI thread so that
        // HandleTrackEnded (which modifies _currentQueueIndex and calls PlayNext)
        // is serialized with UI-thread button clicks (Next / Previous).
        // This eliminates the race condition that caused random songs to play.
        if (!_manualStop)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                // Re-check _manualStop on the UI thread: a button click may have
                // set it between the time the callback was queued and now.
                if (_manualStop) return;

                IsPlaying = false;
                _positionTimer?.Stop();
                TrackEnded?.Invoke(this, EventArgs.Empty);
                HandleTrackEnded();
            });
        }
        else
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                IsPlaying = false;
                _positionTimer?.Stop();
            });
        }
    }

    private void HandleTrackEnded()
    {
        switch (_repeatMode)
        {
            case RepeatMode.One:
                // Repeat current track
                if (CurrentTrack != null)
                {
                    Seek(TimeSpan.Zero);
                    Play();
                }
                break;

            case RepeatMode.All:
                // Play next or restart queue
                if (!PlayNext())
                {
                    // End of queue, restart
                    _currentQueueIndex = -1;
                    PlayNext();
                }
                break;

            case RepeatMode.None:
            default:
                // Play next if available
                if (!PlayNext())
                {
                    // End of playback
                    CleanupPlayback();
                    CurrentTrack = null;
                }
                break;
        }
    }

    private TrackModel? GetCurrentTrackFromQueue()
    {
        var queue = _isShuffleEnabled ? _shuffledQueue : _originalQueue;
        if (_currentQueueIndex >= 0 && _currentQueueIndex < queue.Count)
        {
            return queue[_currentQueueIndex];
        }
        return null;
    }

    private void CreateShuffledQueue()
    {
        _shuffledQueue = new List<TrackModel>(_originalQueue);
        // Fisher-Yates shuffle
        int n = _shuffledQueue.Count;
        while (n > 1)
        {
            n--;
            int k = _random.Next(n + 1);
            (_shuffledQueue[k], _shuffledQueue[n]) = (_shuffledQueue[n], _shuffledQueue[k]);
        }

        // If we have a current track, move it to the very first position (index 0)
        // This ensures the current song stays playing but is now the start of the shuffled queue
        if (CurrentTrack != null)
        {
            int currentInShuffled = _shuffledQueue.FindIndex(t => t.FilePath == CurrentTrack.FilePath);
            if (currentInShuffled >= 0)
            {
                var track = _shuffledQueue[currentInShuffled];
                _shuffledQueue.RemoveAt(currentInShuffled);
                _shuffledQueue.Insert(0, track);
                _currentQueueIndex = 0;
            }
        }
    }

    private void AddToHistory(TrackModel track)
    {
        _history.Remove(track); // Remove if exists to avoid duplicates
        _history.Insert(0, track);
        if (_history.Count > MAX_HISTORY)
        {
            _history.RemoveAt(_history.Count - 1);
        }
    }

    /// <summary>
    /// Pause playback
    /// </summary>
    public void Pause()
    {
        if (_waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing)
        {
            _waveOut.Pause();
            IsPlaying = false;
            _positionTimer?.Stop();
            SmtcService.Instance.UpdateTimeline(CurrentPosition, TotalDuration);
            Logger.Debug("Playback paused");
        }
    }

    /// <summary>
    /// Resume playback
    /// </summary>
    public void Play()
    {
        if (_waveOut != null && _waveOut.PlaybackState == PlaybackState.Paused)
        {
            _manualStop = false;
            _waveOut.Play();
            IsPlaying = true;
            _positionTimer?.Start();
            Logger.Debug("Playback resumed");
        }
        else if (CurrentTrack != null && _waveOut == null)
        {
            // Re-initialize if needed
            Play(CurrentTrack);
        }
    }

    /// <summary>
    /// Toggle play/pause
    /// </summary>
    public void TogglePlayPause()
    {
        if (IsPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>
    /// Stop playback completely
    /// </summary>
    public void Stop()
    {
        StopInternal();
        CurrentTrack = null;
        _currentQueueIndex = -1;
        SmtcService.Instance.UpdatePlaybackStatus(false);
        Logger.Info("Playback stopped");
    }

    private void StopInternal()
    {
        IsPlaying = false;
        _positionTimer?.Stop();

        if (_waveOut != null)
        {
            try
            {
                _manualStop = true;
                // Unsubscribe BEFORE Stop() so the natural-end callback
                // cannot fire and race with the new track setup.
                _waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                _waveOut.Stop();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error stopping waveOut");
            }
            finally
            {
                _waveOut?.Dispose();
                _waveOut = null;
            }
        }

        CleanupPlayback();
    }

    private void CleanupPlayback()
    {
        if (_audioStream != null)
        {
            try
            {
                _audioStream.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Error disposing audioStream");
            }
            finally
            {
                _audioStream = null;
                // Force a small collect to ensure COM objects from MediaFoundation are released
                // This is specifically helpful for .m4a and .flac files on Windows
                GC.Collect(1); 
            }
        }
    }

    /// <summary>
    /// Play next track in queue.
    /// Sets the index directly and uses PlayTrackDirectly to avoid index re-search.
    /// </summary>
    /// <returns>True if a next track was found and played</returns>
    public bool PlayNext()
    {
        var queue = _isShuffleEnabled ? _shuffledQueue : _originalQueue;

        if (queue.Count == 0)
        {
            return false;
        }

        int nextIndex = _currentQueueIndex + 1;

        if (nextIndex >= queue.Count)
        {
            if (_repeatMode == RepeatMode.All)
            {
                nextIndex = 0;
            }
            else
            {
                return false;
            }
        }

        _currentQueueIndex = nextIndex;
        var nextTrack = queue[_currentQueueIndex];

        if (nextTrack != null && File.Exists(nextTrack.FilePath))
        {
            Logger.Debug($"PlayNext: index {_currentQueueIndex} -> {nextTrack.Title}");
            PlayTrackDirectly(nextTrack);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Play previous track in queue.
    /// Sets the index directly and uses PlayTrackDirectly to avoid index re-search.
    /// </summary>
    /// <returns>True if a previous track was found and played</returns>
    public bool PlayPrevious()
    {
        // 1. If we are playing and more than 3 seconds have passed, just restart the song
        // This is a standard behavior in most music players.
        if (CurrentPosition > TimeSpan.FromSeconds(3))
        {
            Seek(TimeSpan.Zero);
            return true;
        }

        // 2. Follow the natural order of the queue
        var queue = _isShuffleEnabled ? _shuffledQueue : _originalQueue;
        if (queue.Count == 0) return false;

        int prevIndex = _currentQueueIndex - 1;

        if (prevIndex < 0)
        {
            if (_repeatMode == RepeatMode.All)
            {
                // Wrap around to the last track
                prevIndex = queue.Count - 1;
            }
            else
            {
                // Already at the beginning, just restart the first song
                Seek(TimeSpan.Zero);
                return true;
            }
        }

        _currentQueueIndex = prevIndex;
        var previousTrack = queue[_currentQueueIndex];

        if (previousTrack != null && File.Exists(previousTrack.FilePath))
        {
            Logger.Debug($"PlayPrevious: index {_currentQueueIndex} -> {previousTrack.Title}");
            PlayTrackDirectly(previousTrack);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Seek to a specific position in the current track
    /// </summary>
    public void Seek(TimeSpan position)
    {
        if (_audioStream != null)
        {
            CurrentPosition = position;
        }
    }

    /// <summary>
    /// Get tracks from current queue after current position
    /// </summary>
    public List<TrackModel> GetUpcomingTracks(int count = 10)
    {
        var queue = _isShuffleEnabled ? _shuffledQueue : _originalQueue;
        var result = new List<TrackModel>();

        for (int i = _currentQueueIndex + 1; i < queue.Count && result.Count < count; i++)
        {
            result.Add(queue[i]);
        }

        return result;
    }

    protected void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _positionTimer?.Stop();
        StopInternal();
        _positionTimer = null;
        GC.SuppressFinalize(this);
    }
}
