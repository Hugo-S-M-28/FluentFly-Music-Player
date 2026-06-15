using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyoutWPF.Classes.Messages;

namespace FluentFlyoutWPF.Classes.Utils
{
    public class MainWindowAccentColorApplier
    {
        private readonly MainWindow _mainWindow;

        public MainWindowAccentColorApplier(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void ApplyAccentColor(SolidColorBrush? brush, bool notifyOthers = false)
        {
            bool useAccent = AccentColorResolver.ShouldUseAccent(brush);
            var activeBrush = AccentColorResolver.ResolveAccentBrush(brush);

            Action action = () =>
            {
                var primaryTextBrush = ThemeResourceHelper.GetPrimaryTextSolidBrush();
                var secondaryControlBrush = ThemeResourceHelper.GetControlFillSecondarySolidBrush();
                var tertiaryControlBrush = ThemeResourceHelper.GetControlFillTertiarySolidBrush();
                var strokeBrush = ThemeResourceHelper.GetControlStrokeSolidBrush();
                bool isDarkTheme = ThemeResourceHelper.IsDarkTheme();

                if (useAccent && activeBrush != null)
                {
                    // Apply to Play/Pause button background
                    _mainWindow.ControlPlayPause.Background = activeBrush;
                    _mainWindow.ControlPlayPause.Foreground = ThemeResourceHelper.GetContrastBrush(activeBrush.Color);
                    _mainWindow.ControlPlayPause.BorderThickness = new Thickness(0);
                    
                    // Apply to control icons for consistent theme
                    _mainWindow.SymbolBack.Foreground = activeBrush;
                    _mainWindow.SymbolForward.Foreground = activeBrush;
                    _mainWindow.ControlBack.Foreground =
                        _mainWindow.ControlForward.Foreground =
                        _mainWindow.ControlRepeat.Foreground =
                        _mainWindow.ControlShuffle.Foreground = activeBrush;

                    // Subtle background for other controls
                    var subtleBrush = activeBrush.Clone();
                    subtleBrush.Opacity = isDarkTheme ? 0.18 : 0.28;
                    subtleBrush.Freeze();
                    _mainWindow.ControlBack.Background = 
                        _mainWindow.ControlForward.Background = 
                        _mainWindow.ControlRepeat.Background = 
                        _mainWindow.ControlShuffle.Background = subtleBrush;
                    _mainWindow.ControlBack.BorderBrush =
                        _mainWindow.ControlForward.BorderBrush =
                        _mainWindow.ControlRepeat.BorderBrush =
                        _mainWindow.ControlShuffle.BorderBrush = activeBrush;
                    
                    // Apply to Seekbar
                    _mainWindow.Seekbar.Foreground = activeBrush;

                    // Apply to Glow effect
                    if (_mainWindow.SongImageGlow != null)
                    {
                        _mainWindow.SongImageGlow.Color = activeBrush.Color;
                        _mainWindow.SongImageGlow.Opacity = 0.6;
                        _mainWindow.SongImageGlow.BlurRadius = 25;
                        _mainWindow.SongImageGlow.ShadowDepth = 0;
                    }
                    
                    // Apply to placeholder
                    _mainWindow.SongImagePlaceholder.Foreground = activeBrush;

                    var resolved = PlaybackSourceResolver.Resolve();
                    bool isInternal = resolved.Kind == PlaybackSourceKind.Internal;
                    bool isShuffle = false;
                    bool isRepeatAll = false;
                    bool isRepeatOne = false;

                    if (isInternal)
                    {
                        isShuffle = MusicPlayerService.Instance.IsShuffleEnabled;
                        isRepeatAll = MusicPlayerService.Instance.RepeatMode == Classes.RepeatMode.All;
                        isRepeatOne = MusicPlayerService.Instance.RepeatMode == Classes.RepeatMode.One;
                    }
                    else if (resolved.Kind == PlaybackSourceKind.External && resolved.ExternalSession != null)
                    {
                        var playbackInfo = resolved.ExternalSession.ControlSession.GetPlaybackInfo();
                        isShuffle = playbackInfo.IsShuffleActive ?? false;
                        isRepeatAll = playbackInfo.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.List;
                        isRepeatOne = playbackInfo.AutoRepeatMode == global::Windows.Media.MediaPlaybackAutoRepeatMode.Track;
                    }

                    _mainWindow.UpdateShuffleRepeatVisuals(isShuffle, isRepeatAll, isRepeatOne, isInternal);
                }
                else
                {
                    // Reset to theme-aware neutral brushes with stronger contrast.
                    var neutralPrimaryButtonBrush = isDarkTheme
                        ? tertiaryControlBrush
                        : ThemeResourceHelper.GetPrimaryTextSolidBrush();
                    var neutralPrimaryButtonForeground = isDarkTheme
                        ? primaryTextBrush
                        : ThemeResourceHelper.GetContrastBrush(neutralPrimaryButtonBrush.Color);

                    _mainWindow.ControlPlayPause.Background = neutralPrimaryButtonBrush;
                    _mainWindow.ControlPlayPause.Foreground = neutralPrimaryButtonForeground;
                    _mainWindow.ControlPlayPause.BorderBrush = strokeBrush;
                    _mainWindow.ControlPlayPause.BorderThickness = isDarkTheme ? new Thickness(1) : new Thickness(0);

                    _mainWindow.ControlBack.Background =
                        _mainWindow.ControlForward.Background =
                        _mainWindow.ControlRepeat.Background =
                        _mainWindow.ControlShuffle.Background = secondaryControlBrush;
                    _mainWindow.ControlBack.BorderBrush =
                        _mainWindow.ControlForward.BorderBrush =
                        _mainWindow.ControlRepeat.BorderBrush =
                        _mainWindow.ControlShuffle.BorderBrush = strokeBrush;
                    
                    _mainWindow.SymbolBack.Foreground = primaryTextBrush;
                    _mainWindow.SymbolForward.Foreground = primaryTextBrush;
                    _mainWindow.ControlBack.Foreground =
                        _mainWindow.ControlForward.Foreground =
                        _mainWindow.ControlRepeat.Foreground =
                        _mainWindow.ControlShuffle.Foreground = primaryTextBrush;
                    
                    _mainWindow.Seekbar.Foreground = ThemeResourceHelper.GetSecondaryTextSolidBrush();
                    
                    if (_mainWindow.SongImageGlow != null)
                    {
                        _mainWindow.SongImageGlow.Opacity = 0;
                        _mainWindow.SongImageGlow.ShadowDepth = 0;
                    }

                    _mainWindow.SongImagePlaceholder.Foreground = primaryTextBrush;
                }

                if (notifyOthers)
                {
                    WeakReferenceMessenger.Default.Send(new UpdateAccentColorMessage());
                }
            };

            if (_mainWindow.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _ = _mainWindow.Dispatcher.InvokeAsync(action);
            }
        }
    }
}
