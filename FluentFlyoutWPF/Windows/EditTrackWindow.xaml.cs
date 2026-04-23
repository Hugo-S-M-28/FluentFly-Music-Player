// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Classes;
using Wpf.Ui.Controls;
using NAudio.Wave;
using NAudio.Dsp;

namespace FluentFlyoutWPF.Windows;

public partial class EditTrackWindow : FluentWindow
{
    private static readonly System.Collections.Generic.Dictionary<string, EditTrackWindow> _instances = new();

    public static void ShowInstance(TrackModel track, Window owner, bool selectLyricsTab = false)
    {
        if (_instances.TryGetValue(track.FilePath, out var existingWindow))
        {
            if (existingWindow.WindowState == WindowState.Minimized)
            {
                existingWindow.WindowState = WindowState.Normal;
            }
            if (selectLyricsTab && existingWindow.FindName("LyricsTab") is System.Windows.Controls.TabItem tab)
            {
                tab.IsSelected = true;
            }
            existingWindow.Activate();
            existingWindow.Focus();
        }
        else
        {
            var window = new EditTrackWindow(track)
            {
                Owner = owner
            };
            if (selectLyricsTab)
            {
                window.Loaded += (s, e) =>
                {
                    if (window.FindName("LyricsTab") is System.Windows.Controls.TabItem newTab)
                    {
                        newTab.IsSelected = true;
                    }
                };
            }
            _instances[track.FilePath] = window;
            window.Closed += (s, e) => _instances.Remove(track.FilePath);
            window.Show();
        }
    }

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private readonly TrackModel _track;
    private string? _newAlbumArtPath;

    public EditTrackWindow(TrackModel track)
    {
        InitializeComponent();
        _track = track;

        // Populate fields
        TitleBox.Text = track.Title;
        ArtistBox.Text = track.Artist;
        CollaboratorsBox.Text = track.Collaborators;
        AlbumBox.Text = track.Album;
        GenreBox.Text = track.Genre;
        TrackNumberBox.Text = track.TrackNumber > 0 ? track.TrackNumber.ToString() : "";

        // Header info
        HeaderTitle.Text = track.Title;
        HeaderArtist.Text = track.Artist;
        HeaderFileInfo.Text = Path.GetFileName(track.FilePath);
        HeaderDuration.Text = track.Duration.ToString(@"m\:ss");

        // Lyrics
        LoadLyrics();

        // Load album art
        LoadAlbumArt(track.AlbumArtPath);

        // Hover effect for album art overlay
        AlbumArtBorder.MouseEnter += (_, _) => ArtOverlay.Opacity = 1;
        AlbumArtBorder.MouseLeave += (_, _) => ArtOverlay.Opacity = 0;

        // Update header live as user types
        TitleBox.TextChanged += (_, _) => HeaderTitle.Text = string.IsNullOrWhiteSpace(TitleBox.Text) ? (System.Windows.Application.Current.FindResource("Edit_Untitled") as string ?? "Untitled") : TitleBox.Text;
        ArtistBox.TextChanged += (_, _) => HeaderArtist.Text = string.IsNullOrWhiteSpace(ArtistBox.Text) ? (System.Windows.Application.Current.FindResource("Edit_UnknownArtist") as string ?? "Unknown Artist") : ArtistBox.Text;

        // Player service events for Play/Pause button
        MusicPlayerService.Instance.PropertyChanged += PlayerService_PropertyChanged;
        UpdatePlayPauseIcon();
    }

    private void PlayerService_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MusicPlayerService.IsPlaying) || e.PropertyName == nameof(MusicPlayerService.CurrentTrack))
        {
            Dispatcher.Invoke(UpdatePlayPauseIcon);
        }
    }

    private void UpdatePlayPauseIcon()
    {
        var player = MusicPlayerService.Instance;
        bool isThisTrackPlaying = player.CurrentTrack?.FilePath == _track.FilePath && player.IsPlaying;
        
        if (PlayPauseButton.Icon is SymbolIcon icon)
        {
            icon.Symbol = isThisTrackPlaying ? SymbolRegular.Pause24 : SymbolRegular.Play24;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        MusicPlayerService.Instance.PropertyChanged -= PlayerService_PropertyChanged;
        base.OnClosed(e);
    }

    private void LoadLyrics()
    {
        try
        {
            var lrcPath = Path.ChangeExtension(_track.FilePath, ".lrc");
            if (File.Exists(lrcPath))
            {
                LyricsBox.Text = File.ReadAllText(lrcPath, System.Text.Encoding.UTF8);
            }
            else
            {
                LyricsBox.Text = _track.Lyrics;
            }
        }
        catch { }
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
                AlbumArtImage.Source = image;
            }
            catch
            {
                AlbumArtImage.Source = null;
            }
        }
    }

    private void AlbumArt_Click(object sender, MouseButtonEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = System.Windows.Application.Current.FindResource("Edit_SelectArtTitle") as string ?? "Select cover image",
            Filter = System.Windows.Application.Current.FindResource("Edit_ArtFilter") as string ?? "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            _newAlbumArtPath = dialog.FileName;
            LoadAlbumArt(_newAlbumArtPath);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            int trackNumber = 0;
            if (!string.IsNullOrWhiteSpace(TrackNumberBox.Text))
            {
                int.TryParse(TrackNumberBox.Text, out trackNumber);
            }

            var success = await LibraryManager.Instance.UpdateTrackMetadataAsync(
                _track,
                TitleBox.Text,
                ArtistBox.Text,
                CollaboratorsBox.Text,
                AlbumBox.Text,
                GenreBox.Text,
                trackNumber,
                _newAlbumArtPath
            );

            if (success)
            {
                // Update lyrics too
                await LibraryManager.Instance.UpdateTrackLyricsAsync(_track, LyricsBox.Text);
                
                // Close the window after a tiny delay to allow events to process
                await Task.Delay(100);
                Close();
            }
            else
            {
                Wpf.Ui.Controls.MessageBox messageBox = new()
                {
                    Title = System.Windows.Application.Current.FindResource("Edit_ErrorTitle") as string ?? "Error",
                    Content = System.Windows.Application.Current.FindResource("Edit_SaveErrorMsg") as string ?? "Could not save changes. Make sure the file is not being used by another process.",
                    CloseButtonText = System.Windows.Application.Current.FindResource("General_Ok") as string ?? "OK"
                };
                await messageBox.ShowDialogAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Crash during track save");
            Wpf.Ui.Controls.MessageBox messageBox = new()
            {
                Title = "Error",
                Content = $"An unexpected error occurred: {ex.Message}",
                CloseButtonText = "OK"
            };
            await messageBox.ShowDialogAsync();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
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

    private void SyncLyrics_Click(object sender, RoutedEventArgs e)
    {
        InsertCurrentTimestamp();
    }

    private void LyricsBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 || (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control))
        {
            InsertCurrentTimestamp();
            e.Handled = true;
        }
    }

    private void InsertCurrentTimestamp()
    {
        try
        {
            var player = MusicPlayerService.Instance;
            if (player.CurrentTrack?.FilePath != _track.FilePath)
            {
                player.PlaySingle(_track);
            }

            var currentTime = player.CurrentPosition;
            var timestamp = $"[{(int)currentTime.TotalMinutes:D2}:{currentTime.Seconds:D2}.{currentTime.Milliseconds / 10:D2}] ";
            
            var caretIndex = LyricsBox.CaretIndex;
            
            // If the cursor is at the start of a line that already has a timestamp, 
            // maybe we want to replace it? For now, just insert.
            
            LyricsBox.SelectedText = timestamp;
            LyricsBox.CaretIndex = caretIndex + timestamp.Length;
            LyricsBox.Focus();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to sync lyrics with current time");
        }
    }

    private void LoadLrc_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = System.Windows.Application.Current.FindResource("Edit_SelectLrcTitle") as string ?? "Select lyrics file",
            Filter = System.Windows.Application.Current.FindResource("Edit_LrcFilter") as string ?? "LRC Files (*.lrc)|*.lrc|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                LyricsBox.Text = File.ReadAllText(dialog.FileName, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load LRC file");
                Wpf.Ui.Controls.MessageBox messageBox = new()
                {
                    Title = System.Windows.Application.Current.FindResource("Edit_ErrorTitle") as string ?? "Error",
                    Content = System.Windows.Application.Current.FindResource("Edit_LrcLoadError") as string ?? "Could not load the selected file.",
                    CloseButtonText = System.Windows.Application.Current.FindResource("General_Ok") as string ?? "OK"
                };
                _ = messageBox.ShowDialogAsync();
            }
        }
    }

    private async void AutoSync_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = LyricsBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            // Find the first timestamp in the lyrics
            var regex = new System.Text.RegularExpressions.Regex(@"\[(\d+):(\d+)([\.:])(\d+)\]");
            var firstMatch = regex.Match(text);
            if (!firstMatch.Success)
            {
                ShowMessage(
                    Application.Current.FindResource("Edit_AutoSyncNoTimestampsTitle") as string ?? "No Timestamps",
                    Application.Current.FindResource("Edit_AutoSyncNoTimestampsMsg") as string ?? "The lyrics don't have timestamps to synchronize."
                );
                return;
            }

            AutoSyncButton.IsEnabled = false;
            
            // Detect the first significant vocal onset using Spectral Flux
            double audioPeakMs = await Task.Run(() => DetectFirstVocalOnsetSpectral(_track.FilePath));
            
            if (audioPeakMs < 0)
            {
                ShowMessage(
                    Application.Current.FindResource("Edit_ErrorTitle") as string ?? "Error",
                    Application.Current.FindResource("Edit_AutoSyncAnalysisError") as string ?? "Could not analyze the audio file."
                );
                AutoSyncButton.IsEnabled = true;
                return;
            }

            // Parse the first lyric timestamp
            int lMin = int.Parse(firstMatch.Groups[1].Value);
            int lSec = int.Parse(firstMatch.Groups[2].Value);
            int lFrac = int.Parse(firstMatch.Groups[4].Value);
            double lMillis = lFrac;
            if (firstMatch.Groups[4].Value.Length == 2) lMillis *= 10;
            else if (firstMatch.Groups[4].Value.Length == 1) lMillis *= 100;
            
            double lyricFirstMs = (lMin * 60000) + (lSec * 1000) + lMillis;

            // Calculate offset
            double offsetMs = audioPeakMs - lyricFirstMs;

            // Apply shift
            ShiftTimestamps(offsetMs);

            ShowMessage(
                Application.Current.FindResource("Edit_AutoSyncSuccessTitle") as string ?? "Synchronized",
                string.Format(Application.Current.FindResource("Edit_AutoSyncSuccessMsg") as string ?? "Lyrics shifted by {0:F1}s to match audio peaks.", offsetMs / 1000.0)
            );
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Auto-sync failed");
        }
        finally
        {
            AutoSyncButton.IsEnabled = true;
        }
    }

    private double DetectFirstVocalOnsetSpectral(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return -1;

            using var reader = new AudioFileReader(filePath);
            
            // FFT Parameters
            int fftSize = 1024;
            int hopSize = fftSize / 2; // 50% overlap
            float[] buffer = new float[fftSize * reader.WaveFormat.Channels];
            Complex[] fftBuffer = new Complex[fftSize];
            float[] prevMagnitudes = new float[fftSize / 2];
            
            double currentTimeMs = 0;
            double msPerHop = (hopSize / (double)reader.WaveFormat.SampleRate) * 1000.0;
            
            List<(double time, double flux)> fluxPoints = new();
            double maxFlux = 0;

            // Analyze the first 30 seconds
            while (reader.Read(buffer, 0, buffer.Length) > 0)
            {
                // Mix to mono and apply Hann window
                for (int i = 0; i < fftSize; i++)
                {
                    float sample = 0;
                    for (int c = 0; c < reader.WaveFormat.Channels; c++)
                    {
                        if (i * reader.WaveFormat.Channels + c < buffer.Length)
                            sample += buffer[i * reader.WaveFormat.Channels + c];
                    }
                    sample /= reader.WaveFormat.Channels;

                    // Hann window to reduce spectral leakage
                    float window = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (fftSize - 1))));
                    fftBuffer[i].X = sample * window;
                    fftBuffer[i].Y = 0;
                }

                // Perform FFT
                FastFourierTransform.FFT(true, (int)Math.Log(fftSize, 2), fftBuffer);

                double currentFlux = 0;
                for (int i = 0; i < fftSize / 2; i++)
                {
                    float magnitude = (float)Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X + fftBuffer[i].Y * fftBuffer[i].Y);
                    
                    // Frequency bin center
                    double freq = i * (double)reader.WaveFormat.SampleRate / fftSize;
                    
                    // Human vocal range focus (300Hz - 3.5kHz)
                    if (freq >= 300 && freq <= 3500)
                    {
                        // Spectral Flux: sum of positive differences in magnitude
                        float diff = magnitude - prevMagnitudes[i];
                        if (diff > 0) currentFlux += diff;
                    }
                    prevMagnitudes[i] = magnitude;
                }

                fluxPoints.Add((currentTimeMs, currentFlux));
                if (currentFlux > maxFlux) maxFlux = currentFlux;

                currentTimeMs += msPerHop;
                if (currentTimeMs > 30000) break;
                
                // Advance the reader by hopSize (simple seek for this implementation)
                reader.Position -= (fftSize - hopSize) * reader.WaveFormat.BlockAlign;
            }

            if (fluxPoints.Count == 0) return -1;

            // Find the first "peak" that is significantly higher than silence
            // We use a moving average to smooth the flux and then find the first threshold cross
            double threshold = maxFlux * 0.25; // 25% of absolute peak
            
            // Look for the first point that stays above threshold for a bit
            int consecutive = 0;
            foreach (var point in fluxPoints)
            {
                if (point.flux > threshold)
                {
                    consecutive++;
                    if (consecutive >= 3) // ~30-40ms of high flux
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

    private async void ShowMessage(string title, string content)
    {
        Wpf.Ui.Controls.MessageBox messageBox = new()
        {
            Title = title,
            Content = content,
            CloseButtonText = Application.Current.FindResource("General_Ok") as string ?? "OK"
        };
        await messageBox.ShowDialogAsync();
    }

    private void ShiftLyricsBackward_Click(object sender, RoutedEventArgs e)
    {
        ShiftTimestamps(-500);
    }

    private void ShiftLyricsForward_Click(object sender, RoutedEventArgs e)
    {
        ShiftTimestamps(500);
    }

    private void ShiftTimestamps(double offsetMs)
    {
        try
        {
            var text = LyricsBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            // Regex to find timestamps like [mm:ss.xx] or [mm:ss:xx]
            var regex = new System.Text.RegularExpressions.Regex(@"\[(\d+):(\d+)([\.:])(\d+)\]");
            
            var newText = regex.Replace(text, match => {
                try
                {
                    int minutes = int.Parse(match.Groups[1].Value);
                    int seconds = int.Parse(match.Groups[2].Value);
                    string separator = match.Groups[3].Value;
                    int fractions = int.Parse(match.Groups[4].Value);
                    
                    // LRC fractions are usually hundredths of a second (2 digits)
                    // If it's 3 digits, it's milliseconds
                    double millis = fractions;
                    if (match.Groups[4].Value.Length == 2) millis *= 10;
                    else if (match.Groups[4].Value.Length == 1) millis *= 100;

                    var time = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(millis);
                    var shiftedTime = time.Add(TimeSpan.FromMilliseconds(offsetMs));
                    
                    if (shiftedTime < TimeSpan.Zero) shiftedTime = TimeSpan.Zero;
                    
                    // Format back to [mm:ss.xx]
                    return $"[{(int)shiftedTime.TotalMinutes:D2}:{shiftedTime.Seconds:D2}{separator}{shiftedTime.Milliseconds / 10:D2}]";
                }
                catch
                {
                    return match.Value; // Fallback to original if parsing fails for one match
                }
            });
            
            LyricsBox.Text = newText;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to shift timestamps");
        }
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = Path.GetDirectoryName(_track.FilePath);
            if (directory != null && Directory.Exists(directory))
            {
                Process.Start("explorer.exe", $"/select,\"{_track.FilePath}\"");
            }
        }
        catch (Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Warn(ex, "Failed to open file location");
        }
    }
}
