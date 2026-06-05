// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using MicaWPF.Controls;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FluentFlyoutWPF.Windows;

/// <summary>
/// Interaction logic for NextUpWindow.xaml
/// </summary>
public partial class NextUpWindow : MicaWindow
{
    public NextUpWindow(SettingsShellViewModel settingsViewModel, string title, string artist, BitmapImage thumbnail)
    {
        DataContext = settingsViewModel;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft - Width - 100; // move window safely off-screen (multi-monitor safe)
        Top = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + 100;
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);
        CustomWindowChrome.CaptionHeight = 0;

        WindowBlurHelper.ApplyWindowBackdrop(this);

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

        async Task WaitAsync()
        {
            try
            {
                await Task.Delay(SettingsManager.Current.NextUpDuration);
                FlyoutAnimationService.CloseAnimation(this);
                await Task.Delay(FlyoutAnimationService.GetDuration());
                Close();
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Error(ex, "Error in NextUpWindow close delay");
            }
        }

        _ = WaitAsync();
    }

    public void UpdateThumbnail(BitmapImage thumbnail)
    {
        SongImage.ImageSource = thumbnail;
        if (SongImage.ImageSource == null) SongImagePlaceholder.Visibility = Visibility.Visible;
        else SongImagePlaceholder.Visibility = Visibility.Collapsed;
    }
    private void ApplyAccentColor(System.Windows.Media.SolidColorBrush? brush)
    {
        bool useAccent = AccentColorResolver.ShouldUseAccent(brush);
        var activeBrush = AccentColorResolver.ResolveAccentBrush(brush);

        if (useAccent && activeBrush != null)
        {
            NextUpIcon.Foreground = activeBrush;
            UpNextTextBlock.Foreground = activeBrush;
            SongImagePlaceholder.Foreground = activeBrush;
        }
        else
        {
            var secondaryBrush = ThemeResourceHelper.GetSecondaryTextSolidBrush();
            NextUpIcon.Foreground = secondaryBrush;
            UpNextTextBlock.Foreground = secondaryBrush;
            SongImagePlaceholder.Foreground = secondaryBrush;
        }
    }
}
