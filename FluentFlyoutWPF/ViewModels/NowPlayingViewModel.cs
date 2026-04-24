// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using Windows.Media.Control;

namespace FluentFlyoutWPF.ViewModels;

public partial class NowPlayingViewModel : ObservableObject
{
    [ObservableProperty]
    private string title;

    [ObservableProperty]
    private string artist;

    public NowPlayingViewModel()
    {
        title = System.Windows.Application.Current.Resources["Player_NoTrack"] as string ?? "No track playing";
        artist = System.Windows.Application.Current.Resources["Player_SelectTrackMsg"] as string ?? "Select a track from the library";

        // Auto-sync with MusicPlayerService
        MusicPlayerService.Instance.TrackChanged += (s, e) => SyncWithPlayer();
        MusicPlayerService.Instance.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(MusicPlayerService.IsPlaying) || 
                e.PropertyName == nameof(MusicPlayerService.CurrentTrack))
            {
                SyncWithPlayer();
            }
            else if (e.PropertyName == nameof(MusicPlayerService.CurrentPosition))
            {
                UpdatePosition();
            }
        };

        // Auto-sync with ExternalMediaService
        ExternalMediaService.Instance.MediaPropertyChanged += (session, props) => SyncWithPlayer();
        ExternalMediaService.Instance.PlaybackStateChanged += (session, info) => SyncWithPlayer();
        ExternalMediaService.Instance.TimelinePropertyChanged += (session, props) => UpdatePosition();

        SyncWithPlayer();
    }

    private void SyncWithPlayer()
    {
        // 1. Check Internal Player
        if (MusicPlayerService.Instance.IsPlaying || (MusicPlayerService.Instance.CurrentTrack != null && SettingsManager.Current.InternalPlayerEnabled))
        {
            var track = MusicPlayerService.Instance.CurrentTrack;
            if (track != null)
            {
                Title = track.Title;
                Artist = track.FullArtistDisplay;
                HasTrack = true;
                CurrentTrack = track;
                CoverImage = LibraryManager.Instance.GetAlbumArt(track);
                IsPlaying = MusicPlayerService.Instance.IsPlaying;
                TotalTime = track.Duration.ToString(@"mm\:ss");
                SeekMaximum = track.Duration.TotalSeconds;
                UpdatePosition();
                return;
            }
        }

        // 2. Fallback to External Media
        var session = ExternalMediaService.Instance.GetPreferredSession();
        if (session != null)
        {
            var props = session.ControlSession.GetPlaybackInfo();
            var mediaProps = session.ControlSession.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
            
            if (mediaProps != null)
            {
                Title = mediaProps.Title;
                Artist = mediaProps.Artist;
                HasTrack = true;
                CoverImage = BitmapHelper.GetThumbnail(mediaProps.Thumbnail);
                IsPlaying = props.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                
                var timeline = session.ControlSession.GetTimelineProperties();
                TotalTime = timeline.EndTime.ToString(@"mm\:ss");
                SeekMaximum = timeline.EndTime.TotalSeconds;
                UpdatePosition();
                return;
            }
        }

        // 3. Default state
        Title = System.Windows.Application.Current.Resources["Player_NoTrack"] as string ?? "No track playing";
        Artist = System.Windows.Application.Current.Resources["Player_SelectTrackMsg"] as string ?? "Select a track from the library";
        HasTrack = false;
        CurrentTrack = null;
        CoverImage = null;
        IsPlaying = false;
        CurrentTime = "0:00";
        TotalTime = "0:00";
        SeekValue = 0;
    }

    private void UpdatePosition()
    {
        if (MusicPlayerService.Instance.IsPlaying || (MusicPlayerService.Instance.CurrentTrack != null && SettingsManager.Current.InternalPlayerEnabled))
        {
            var pos = MusicPlayerService.Instance.CurrentPosition;
            SeekValue = pos.TotalSeconds;
            CurrentTime = pos.ToString(@"mm\:ss");
        }
        else
        {
            var session = ExternalMediaService.Instance.GetPreferredSession();
            if (session != null)
            {
                var timeline = session.ControlSession.GetTimelineProperties();
                SeekValue = timeline.Position.TotalSeconds;
                CurrentTime = timeline.Position.ToString(@"mm\:ss");
            }
        }
    }

    [ObservableProperty]
    private BitmapImage? coverImage;

    [ObservableProperty]
    private string lyrics = string.Empty;

    [ObservableProperty]
    private bool hasTrack;

    [ObservableProperty]
    private bool isPlaying;

    [ObservableProperty]
    private string currentTime = "0:00";

    [ObservableProperty]
    private string totalTime = "0:00";

    [ObservableProperty]
    private double seekValue;

    [ObservableProperty]
    private double seekMaximum = 100;

    [ObservableProperty]
    private double volume = 0.8;

    [ObservableProperty]
    private bool isShuffleEnabled;

    [ObservableProperty]
    private bool isRepeatEnabled;

    [ObservableProperty]
    private RepeatMode repeatMode = RepeatMode.None;

    [ObservableProperty]
    private TrackModel? currentTrack;

    [ObservableProperty]
    private string coverUrl = string.Empty;

    [ObservableProperty]
    private ObservableCollection<LyricLine> lyricLines = new();

    [ObservableProperty]
    private LyricLine? activeLyricLine;

    [ObservableProperty]
    private bool hasLyrics;
}