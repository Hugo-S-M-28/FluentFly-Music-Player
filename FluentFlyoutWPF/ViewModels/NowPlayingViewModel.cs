// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Classes;

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

public enum RepeatMode
{
    None,
    One,
    All
}