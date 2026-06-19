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

public enum NextUpDisplayMode
{
    UpNext,
    NowPlaying
}

/// <summary>
/// Interaction logic for NextUpWindow.xaml
/// </summary>
public partial class NextUpWindow : MicaWindow
{
    private readonly long _openedTick = System.Environment.TickCount64;
    private bool _isClosing = false;
    private readonly object _closeLock = new();

    public NextUpWindow(SettingsShellViewModel settingsViewModel, string title, string artist, BitmapImage? thumbnail, NextUpDisplayMode displayMode = NextUpDisplayMode.UpNext, bool autoClose = true)
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

        if (displayMode == NextUpDisplayMode.NowPlaying)
        {
            UpNextTextBlock.Text = FindResource("NextUpWindow_NowPlayingText") as string ?? "Now playing:";
        }
        else
        {
            UpNextTextBlock.Text = FindResource("NextUpWindow_UpNextText") as string ?? "Up next:";
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

        if (autoClose)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(SettingsManager.Current.NextUpDuration);
                await CloseWithAnimationAsync(ignoreSafeguard: true);
            });
        }
    }

    public async Task CloseWithAnimationAsync(bool ignoreSafeguard = false)
    {
        lock (_closeLock)
        {
            if (_isClosing) return;
            _isClosing = true;
        }

        if (!ignoreSafeguard)
        {
            long elapsed = System.Environment.TickCount64 - _openedTick;
            long minDuration = SettingsManager.Current.NextUpDuration;
            if (elapsed < minDuration)
            {
                int delay = (int)(minDuration - elapsed);
                if (delay > 0)
                {
                    await Task.Delay(delay);
                }
            }
        }

        if (!Dispatcher.CheckAccess())
        {
            await Dispatcher.InvokeAsync(async () => await CloseWithAnimationInternalAsync());
        }
        else
        {
            await CloseWithAnimationInternalAsync();
        }
    }

    private async Task CloseWithAnimationInternalAsync()
    {
        try
        {
            FlyoutAnimationService.CloseAnimation(this);
            await Task.Delay(FlyoutAnimationService.GetDuration());
            Close();
        }
        catch (System.Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(ex, "Error in NextUpWindow close animation");
            try
            {
                Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    public void UpdateThumbnail(BitmapImage? thumbnail)
    {
        SongImage.ImageSource = thumbnail;
        if (SongImage.ImageSource == null) SongImagePlaceholder.Visibility = Visibility.Visible;
        else SongImagePlaceholder.Visibility = Visibility.Collapsed;
    }
    private void ApplyAccentColor(System.Windows.Media.SolidColorBrush? brush)
    {
        bool isDark = ThemeResourceHelper.IsDarkTheme();
        bool useAccent = AccentColorResolver.ShouldUseAccent(brush);
        var activeBrush = useAccent ? AccentColorResolver.ResolveReadableAccentBrush(brush, isDark) : null;

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
