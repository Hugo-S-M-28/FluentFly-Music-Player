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
                    var readableBrush = AccentColorResolver.ResolveReadableAccentBrush(brush, isDarkTheme);

                    // Apply to Play/Pause button background
                    _mainWindow.ControlPlayPause.Background = activeBrush;
                    _mainWindow.ControlPlayPause.Foreground = ThemeResourceHelper.GetContrastBrush(activeBrush.Color);
                    _mainWindow.ControlPlayPause.BorderThickness = new Thickness(0);
                    
                    // Apply to control icons for consistent theme
                    _mainWindow.SymbolBack.Foreground = readableBrush;
                    _mainWindow.SymbolForward.Foreground = readableBrush;
                    _mainWindow.ControlBack.Foreground =
                        _mainWindow.ControlForward.Foreground =
                        _mainWindow.ControlRepeat.Foreground =
                        _mainWindow.ControlShuffle.Foreground = readableBrush;

                    // Subtle background for other controls
                    var subtleBrush = readableBrush.Clone();
                    subtleBrush.Opacity = isDarkTheme ? 0.18 : 0.28;
                    subtleBrush.Freeze();
                    _mainWindow.ControlBack.Background = 
                        _mainWindow.ControlForward.Background = 
                        _mainWindow.ControlRepeat.Background = 
                        _mainWindow.ControlShuffle.Background = subtleBrush;
                    _mainWindow.ControlBack.BorderBrush =
                        _mainWindow.ControlForward.BorderBrush =
                        _mainWindow.ControlRepeat.BorderBrush =
                        _mainWindow.ControlShuffle.BorderBrush = readableBrush;
                    
                    // Apply to Seekbar
                    _mainWindow.Seekbar.Foreground = readableBrush;

                    // Apply to Glow effect
                    if (_mainWindow.SongImageGlow != null)
                    {
                        _mainWindow.SongImageGlow.Color = activeBrush.Color;
                        _mainWindow.SongImageGlow.Opacity = 0.6;
                        _mainWindow.SongImageGlow.BlurRadius = 25;
                        _mainWindow.SongImageGlow.ShadowDepth = 0;
                    }
                    
                    // Apply to placeholder
                    _mainWindow.SongImagePlaceholder.Foreground = readableBrush;

                    _mainWindow.ViewModel.NowPlaying.UpdateForegrounds();
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
