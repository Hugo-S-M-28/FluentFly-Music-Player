using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Windows.Media.Control;
using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using MicaWPF.Core.Extensions;

namespace FluentFlyoutWPF.Classes.Coordinators;

public class FlyoutCoordinator : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    public void CancelLoop(MainWindow window)
    {
        window.cts.Cancel();
        window.cts = new CancellationTokenSource();
    }

    public bool TryShowMediaFlyoutDebounced(MainWindow window)
    {
        long currentTime = Environment.TickCount64;
        if ((currentTime - window._lastFlyoutTime) < 200)
        {
            return false;
        }
        window._lastFlyoutTime = currentTime;
        _ = ShowMediaFlyoutAsync(window);
        return true;
    }

    public async Task ShowMediaFlyoutAsync(MainWindow window, bool toggleMode = false, bool forceShow = false)
    {
        // If in toggle mode and flyout is visible, close it
        if (toggleMode && window.Visibility == Visibility.Visible && !window._isHiding)
        {
            window._isHiding = true;
            FlyoutAnimationService.CloseAnimation(window);
            window.cts.Cancel();
            await Task.Delay(FlyoutAnimationService.GetDuration());
            if (window._isHiding)
            {
                window.Hide();
                if (SettingsManager.Current.SeekbarEnabled)
                    window.UpdatePlaybackStateTimer(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
                window._isHiding = false;
            }
            return;
        }

        var focusedSession = ExternalMediaService.Instance.GetPreferredSession();
        bool hasInternalTrack = SettingsManager.Current.InternalPlayerEnabled && MusicPlayerService.Instance.CurrentTrack != null;

        if (focusedSession == null && !hasInternalTrack)
            return;

        if (!forceShow && !SettingsManager.Current.MediaFlyoutEnabled)
        {
            Logger.Debug("ShowMediaFlyout: Suppressed because MediaFlyoutEnabled is false.");
            return;
        }

        if (FullscreenDetector.IsFullscreenApplicationRunning())
        {
            Logger.Debug("ShowMediaFlyout: Suppressed because a fullscreen application is running.");
            return;
        }

        window.UpdateUI();

        if (SettingsManager.Current.SeekbarEnabled)
        {
            if (focusedSession != null)
                window.UpdatePlaybackStateTimer(focusedSession.ControlSession.GetPlaybackInfo().PlaybackStatus);
            else if (hasInternalTrack)
                window.UpdatePlaybackStateTimer(MusicPlayerService.Instance.IsPlaying ? 
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing : 
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
        }

        window.CloseNextUpWindowIfOpen();

        bool needsAnimation = window.Visibility != Visibility.Visible || window._isHiding;
        window.Visibility = Visibility.Visible;
        if (needsAnimation)
        {
            window._isHiding = false;
            FlyoutAnimationService.OpenAnimation(window);
        }
        window.cts.Cancel();
        window.cts = new CancellationTokenSource();
        var token = window.cts.Token;
        WindowHelper.SetTopmost(window);
        window.EnableBackdrop();

        _ = RunFlyoutLoop(window, token);
    }

    private async Task RunFlyoutLoop(MainWindow window, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(100, token);
                if (!window.IsMouseOver && !SettingsManager.Current.MediaFlyoutAlwaysDisplay)
                {
                    await Task.Delay(SettingsManager.Current.Duration, token);
                    if (!window.IsMouseOver)
                    {
                        window._isHiding = true;
                        FlyoutAnimationService.CloseAnimation(window);
                        await Task.Delay(FlyoutAnimationService.GetDuration());
                        if (window._isHiding == false) return;
                        window.Hide();
                        if (SettingsManager.Current.SeekbarEnabled)
                            window.UpdatePlaybackStateTimer(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused);
                        break;
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
            // Task canceled, ignore
        }
    }

    public void Dispose()
    {
    }
}
