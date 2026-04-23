// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using MicaWPF.Controls;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.Windows;

/// <summary>
/// Interaction logic for NextUpWindow.xaml
/// </summary>
public partial class NextUpWindow : MicaWindow
{
    public NextUpWindow(string title, string artist, BitmapImage thumbnail)
    {
        DataContext = SettingsManager.Current;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft - Width - 100; // move window safely off-screen (multi-monitor safe)
        Top = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + 100;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);
        CustomWindowChrome.CaptionHeight = 0;

        if (SettingsManager.Current.NextUpAcrylicWindowEnabled)
        {
            WindowBlurHelper.EnableBlur(this);
        }
        else
        {
            WindowBlurHelper.DisableBlur(this);
        }

        var upNextWidth = StringWidth.GetStringWidth(UpNextTextBlock.Text);
        var titleWidth = StringWidth.GetStringWidth(title);
        var artistWidth = StringWidth.GetStringWidth(artist);

        if (titleWidth > artistWidth) Width = titleWidth + 76 + upNextWidth;
        else Width = artistWidth + 76 + upNextWidth;
        if (Width > 400) Width = 400; // max width to prevent window from being too wide
        SongTitle.Text = title;
        SongArtist.Text = artist;
        UpdateThumbnail(thumbnail);
        ApplyAccentColor(BitmapHelper.SavedDominantColors.FirstOrDefault());
        Show();

        FlyoutAnimationService.OpenAnimation(this);

        async void wait()
        {
            await Task.Delay(SettingsManager.Current.NextUpDuration);
            FlyoutAnimationService.CloseAnimation(this);
            await Task.Delay(FlyoutAnimationService.GetDuration());
            Close();
        }

        wait();
    }

    public void UpdateThumbnail(BitmapImage thumbnail)
    {
        SongImage.ImageSource = thumbnail;
        if (SongImage.ImageSource == null) SongImagePlaceholder.Visibility = Visibility.Visible;
        else SongImagePlaceholder.Visibility = Visibility.Collapsed;
    }
    private void ApplyAccentColor(System.Windows.Media.SolidColorBrush? brush)
    {
        bool useAccent = SettingsManager.Current.UseAlbumArtAsAccentColor && brush != null;

        if (useAccent && brush != null)
        {
            NextUpIcon.Foreground = brush;
            UpNextTextBlock.Foreground = brush;
            SongImagePlaceholder.Foreground = brush;
        }
    }
}