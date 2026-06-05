using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Models;
using NAudio.Wave;
using NAudio.Dsp;

namespace FluentFlyoutWPF.ViewModels;

public partial class EditTrackViewModel : ObservableObject
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly TrackModel _track;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string artist = string.Empty;

    [ObservableProperty]
    private string collaborators = string.Empty;

    [ObservableProperty]
    private string album = string.Empty;

    [ObservableProperty]
    private string genre = string.Empty;

    [ObservableProperty]
    private string trackNumber = string.Empty;

    [ObservableProperty]
    private string lyrics = string.Empty;

    [ObservableProperty]
    private string headerTitle = string.Empty;

    [ObservableProperty]
    private string headerArtist = string.Empty;

    [ObservableProperty]
    private string headerFileInfo = string.Empty;

    [ObservableProperty]
    private string headerDuration = string.Empty;

    [ObservableProperty]
    private ImageSource? albumArtImage;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private bool isAutoSyncEnabled = true;

    private string? _newAlbumArtPath;

    public EditTrackViewModel(TrackModel track)
    {
        _track = track;

        Title = track.Title ?? string.Empty;
        Artist = track.Artist ?? string.Empty;
        Collaborators = track.Collaborators ?? string.Empty;
        Album = track.Album ?? string.Empty;
        Genre = track.Genre ?? string.Empty;
        TrackNumber = track.TrackNumber > 0 ? track.TrackNumber.ToString() : string.Empty;

        HeaderTitle = string.IsNullOrWhiteSpace(Title) ? "Untitled" : Title;
        HeaderArtist = string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;
        HeaderFileInfo = Path.GetFileName(track.FilePath) ?? string.Empty;
        HeaderDuration = track.Duration.ToString(@"m\:ss");

        LoadLyrics();
        LoadAlbumArt(track.AlbumArtPath);

        MusicPlayerService.Instance.PropertyChanged += PlayerService_PropertyChanged;
        UpdatePlayPauseState();
    }

    private void PlayerService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MusicPlayerService.IsPlaying) || e.PropertyName == nameof(MusicPlayerService.CurrentTrack))
        {
            UpdatePlayPauseState();
        }
    }

    private void UpdatePlayPauseState()
    {
        var player = MusicPlayerService.Instance;
        IsPlaying = player.CurrentTrack?.FilePath == _track.FilePath && player.IsPlaying;
    }

    private void LoadLyrics()
    {
        try
        {
            var lrcPath = Path.ChangeExtension(_track.FilePath, ".lrc");
            if (File.Exists(lrcPath))
            {
                Lyrics = File.ReadAllText(lrcPath, System.Text.Encoding.UTF8);
            }
            else
            {
                Lyrics = _track.Lyrics ?? string.Empty;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Failed to load lyrics");
        }
    }

    private void LoadAlbumArt(string? artPath)
    {
        if (!string.IsNullOrEmpty(artPath) && File.Exists(artPath))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(artPath);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 240;
                image.EndInit();
                image.Freeze();
                AlbumArtImage = image;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to load album art image");
                AlbumArtImage = null;
            }
        }
        else
        {
            AlbumArtImage = null;
        }
    }

    partial void OnTitleChanged(string value)
    {
        HeaderTitle = string.IsNullOrWhiteSpace(value) ? "Untitled" : value;
    }

    partial void OnArtistChanged(string value)
    {
        HeaderArtist = string.IsNullOrWhiteSpace(value) ? "Unknown Artist" : value;
    }

    [RelayCommand]
    public void SelectAlbumArt()
    {
        var filePath = ServiceLocator.FileDialog.OpenFile(
            "Select cover image",
            "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*"
        );

        if (!string.IsNullOrEmpty(filePath))
        {
            _newAlbumArtPath = filePath;
            LoadAlbumArt(filePath);
        }
    }

    [RelayCommand]
    public async Task SaveAsync(Window? window)
    {
        try
        {
            int num = 0;
            if (!string.IsNullOrWhiteSpace(TrackNumber))
            {
                int.TryParse(TrackNumber, out num);
            }

            var success = await LibraryManager.Instance.UpdateTrackMetadataAsync(
                _track,
                Title,
                Artist,
                Collaborators,
                Album,
                Genre,
                num,
                _newAlbumArtPath
            );

            if (success)
            {
                await LibraryManager.Instance.UpdateTrackLyricsAsync(_track, Lyrics);
                await Task.Delay(100);
                window?.Close();
            }
            else
            {
                await ServiceLocator.Dialog.ShowErrorAsync(
                    "Error",
                    "Could not save changes. Make sure the file is not being used by another process."
                );
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Crash during track save");
            await ServiceLocator.Dialog.ShowErrorAsync("Error", $"An unexpected error occurred: {ex.Message}");
        }
    }

    [RelayCommand]
    public void Cancel(Window? window)
    {
        window?.Close();
    }

    [RelayCommand]
    public void PlayPause()
    {
        var player = MusicPlayerService.Instance;
        if (player.CurrentTrack?.FilePath != _track.FilePath)
        {
            player.PlaySingle(_track);
        }
        else
        {
            player.TogglePlayPause();
        }
    }

    [RelayCommand]
    public void InsertTimestamp(System.Windows.Controls.TextBox? lyricsBox)
    {
        if (lyricsBox == null) return;

        try
        {
            var player = MusicPlayerService.Instance;
            if (player.CurrentTrack?.FilePath != _track.FilePath)
            {
                player.PlaySingle(_track);
            }

            var currentTime = player.CurrentPosition;
            var timestamp = $"[{(int)currentTime.TotalMinutes:D2}:{currentTime.Seconds:D2}.{currentTime.Milliseconds / 10:D2}] ";

            var caretIndex = lyricsBox.CaretIndex;
            lyricsBox.SelectedText = timestamp;
            lyricsBox.CaretIndex = caretIndex + timestamp.Length;
            lyricsBox.Focus();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to sync lyrics with current time");
        }
    }

    [RelayCommand]
    public void LoadLrc()
    {
        var filePath = ServiceLocator.FileDialog.OpenFile(
            "Select lyrics file",
            "LRC Files (*.lrc)|*.lrc|Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        );

        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                Lyrics = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load LRC file");
                _ = ServiceLocator.Dialog.ShowErrorAsync("Error", "Could not load the selected file.");
            }
        }
    }

    [RelayCommand]
    public async Task AutoSyncAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Lyrics)) return;

            var regex = new Regex(@"\[(\d+):(\d+)([\.:])(\d+)\]");
            var firstMatch = regex.Match(Lyrics);
            if (!firstMatch.Success)
            {
                await ServiceLocator.Dialog.ShowMessageAsync(
                    "No Timestamps",
                    "The lyrics don't have timestamps to synchronize."
                );
                return;
            }

            IsAutoSyncEnabled = false;

            double audioPeakMs = await Task.Run(() => DetectFirstVocalOnsetSpectral(_track.FilePath));

            if (audioPeakMs < 0)
            {
                await ServiceLocator.Dialog.ShowErrorAsync("Error", "Could not analyze the audio file.");
                IsAutoSyncEnabled = true;
                return;
            }

            int lMin = int.Parse(firstMatch.Groups[1].Value);
            int lSec = int.Parse(firstMatch.Groups[2].Value);
            int lFrac = int.Parse(firstMatch.Groups[4].Value);
            double lMillis = lFrac;
            if (firstMatch.Groups[4].Value.Length == 2) lMillis *= 10;
            else if (firstMatch.Groups[4].Value.Length == 1) lMillis *= 100;

            double lyricFirstMs = (lMin * 60000) + (lSec * 1000) + lMillis;
            double offsetMs = audioPeakMs - lyricFirstMs;

            ShiftTimestamps(offsetMs);

            await ServiceLocator.Dialog.ShowMessageAsync(
                "Synchronized",
                $"Lyrics shifted by {offsetMs / 1000.0:F1}s to match audio peaks."
            );
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Auto-sync failed");
        }
        finally
        {
            IsAutoSyncEnabled = true;
        }
    }

    [RelayCommand]
    public void ShiftLyrics(string amountStr)
    {
        if (double.TryParse(amountStr, out double amount))
        {
            ShiftTimestamps(amount);
        }
    }

    [RelayCommand]
    public void OpenLocation()
    {
        ServiceLocator.Shell.RevealInFileExplorer(_track.FilePath);
    }

    public void Cleanup()
    {
        MusicPlayerService.Instance.PropertyChanged -= PlayerService_PropertyChanged;
    }

    private double DetectFirstVocalOnsetSpectral(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return -1;

            using var reader = new AudioFileReader(filePath);

            int fftSize = 1024;
            int hopSize = fftSize / 2;
            float[] buffer = new float[fftSize * reader.WaveFormat.Channels];
            Complex[] fftBuffer = new Complex[fftSize];
            float[] prevMagnitudes = new float[fftSize / 2];

            double currentTimeMs = 0;
            double msPerHop = (hopSize / (double)reader.WaveFormat.SampleRate) * 1000.0;

            var fluxPoints = new System.Collections.Generic.List<(double time, double flux)>();
            double maxFlux = 0;

            while (reader.Read(buffer, 0, buffer.Length) > 0)
            {
                for (int i = 0; i < fftSize; i++)
                {
                    float sample = 0;
                    for (int c = 0; c < reader.WaveFormat.Channels; c++)
                    {
                        if (i * reader.WaveFormat.Channels + c < buffer.Length)
                            sample += buffer[i * reader.WaveFormat.Channels + c];
                    }
                    sample /= reader.WaveFormat.Channels;

                    float window = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1))));
                    fftBuffer[i].X = sample * window;
                    fftBuffer[i].Y = 0;
                }

                FastFourierTransform.FFT(true, (int)Math.Log(fftSize, 2), fftBuffer);

                double currentFlux = 0;
                for (int i = 0; i < fftSize / 2; i++)
                {
                    float magnitude = (float)Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X + fftBuffer[i].Y * fftBuffer[i].Y);
                    double freq = i * (double)reader.WaveFormat.SampleRate / fftSize;

                    if (freq >= 300 && freq <= 3500)
                    {
                        float diff = magnitude - prevMagnitudes[i];
                        if (diff > 0) currentFlux += diff;
                    }
                    prevMagnitudes[i] = magnitude;
                }

                fluxPoints.Add((currentTimeMs, currentFlux));
                if (currentFlux > maxFlux) maxFlux = currentFlux;

                currentTimeMs += msPerHop;
                if (currentTimeMs > 30000) break;

                reader.Position -= (fftSize - hopSize) * reader.WaveFormat.BlockAlign;
            }

            if (fluxPoints.Count == 0) return -1;

            double threshold = maxFlux * 0.25;
            int consecutive = 0;
            foreach (var point in fluxPoints)
            {
                if (point.flux > threshold)
                {
                    consecutive++;
                    if (consecutive >= 3)
                    {
                        return Math.Max(0, point.time - (consecutive * msPerHop));
                    }
                }
                else
                {
                    consecutive = 0;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Spectral analysis failed");
        }
        return -1;
    }

    private void ShiftTimestamps(double offsetMs)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Lyrics)) return;

            var regex = new Regex(@"\[(\d+):(\d+)([\.:])(\d+)\]");

            var newText = regex.Replace(Lyrics, match => {
                try
                {
                    int minutes = int.Parse(match.Groups[1].Value);
                    int seconds = int.Parse(match.Groups[2].Value);
                    string separator = match.Groups[3].Value;
                    int fractions = int.Parse(match.Groups[4].Value);

                    double millis = fractions;
                    if (match.Groups[4].Value.Length == 2) millis *= 10;
                    else if (match.Groups[4].Value.Length == 1) millis *= 100;

                    var time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(millis);
                    var shiftedTime = time.Add(TimeSpan.FromMilliseconds(offsetMs));

                    if (shiftedTime < TimeSpan.Zero) shiftedTime = TimeSpan.Zero;

                    return $"[{(int)shiftedTime.TotalMinutes:D2}:{shiftedTime.Seconds:D2}{separator}{shiftedTime.Milliseconds / 10:D2}]";
                }
                catch
                {
                    return match.Value;
                }
            });

            Lyrics = newText;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to shift timestamps");
        }
    }
}
