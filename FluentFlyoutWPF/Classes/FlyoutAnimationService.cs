using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Utils;
using MicaWPF.Controls;
using NLog;
using System;
using System.Windows;
using System.Windows.Media.Animation;
using static FluentFlyoutWPF.Classes.Utils.MonitorUtil;

namespace FluentFlyoutWPF.Classes;

public static class FlyoutAnimationService
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    public static int GetDuration()
    {
        int msDuration = SettingsManager.Current.FlyoutAnimationSpeed switch
        {
            0 => 0, // off
            1 => 150, // 0.5x
            2 => 300, // 1x
            3 => 450, // 1.5x
            4 => 600, // 2x
            _ => 900 // 3x
        };
        return msDuration;
    }

    public static EasingFunctionBase? GetEasingStyle(bool easeOut)
    {
        EasingMode easingMode = easeOut ? EasingMode.EaseOut : EasingMode.EaseIn;
        EasingFunctionBase? easingStyle = SettingsManager.Current.FlyoutAnimationEasingStyle switch
        {
            1 => new SineEase { EasingMode = easingMode }, // sine
            2 => new QuadraticEase { EasingMode = easingMode }, // quadratic
            3 => new CubicEase { EasingMode = easingMode }, // cubic
            _ => null
        };
        return easingStyle;
    }

    private static MonitorInfo GetSelectedMonitor()
    {
        return MonitorUtil.GetSelectedMonitor(SettingsManager.Current.FlyoutSelectedMonitor);
    }

    private static bool TryGetStoryboardAnimations(
        Storyboard storyboard,
        out DoubleAnimation? moveAnimation,
        out DoubleAnimation? opacityAnimation,
        out DoubleAnimation? scaleXAnimation,
        out DoubleAnimation? scaleYAnimation)
    {
        moveAnimation = null;
        opacityAnimation = null;
        scaleXAnimation = null;
        scaleYAnimation = null;

        if (storyboard == null || storyboard.Children.Count < 2)
        {
            return false;
        }

        moveAnimation = storyboard.Children[0] as DoubleAnimation;
        opacityAnimation = storyboard.Children[1] as DoubleAnimation;

        if (moveAnimation == null || opacityAnimation == null)
        {
            return false;
        }

        if (storyboard.Children.Count >= 4)
        {
            scaleXAnimation = storyboard.Children[2] as DoubleAnimation;
            scaleYAnimation = storyboard.Children[3] as DoubleAnimation;
        }

        return true;
    }

    private static void ComputeFlyoutPlacement(
        MicaWindow window,
        bool alwaysBottom,
        MonitorInfo monitor,
        out double windowLeft,
        out double fromTop,
        out double toTop)
    {
        var workArea = monitor.workArea;

        // Update the DPI by moving the window to the target workArea, ignoring WPF scaling
        WindowHelper.SetPosition(window, workArea.Left, workArea.Top);
        var windowRect = WindowHelper.GetPlacement(window);

        windowLeft = 0;
        toTop = 0;
        int position = SettingsManager.Current.Position;

        if (alwaysBottom == false)
        {
            if (position == 0)
            {
                windowLeft = workArea.Left + 16;
                toTop = workArea.Top + workArea.Height - windowRect.Height - 16;
            }
            else if (position == 1)
            {
                windowLeft = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
                toTop = workArea.Top + workArea.Height - windowRect.Height - 80;
            }
            else if (position == 2)
            {
                windowLeft = workArea.Left + workArea.Width - windowRect.Width - 16;
                toTop = workArea.Top + workArea.Height - windowRect.Height - 16;
            }
            else if (position == 3)
            {
                windowLeft = workArea.Left + 16;
                toTop = workArea.Top + 16;
            }
            else if (position == 4)
            {
                windowLeft = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
                toTop = workArea.Top + 16;
            }
            else if (position == 5)
            {
                windowLeft = workArea.Left + workArea.Width - windowRect.Width - 16;
                toTop = workArea.Top + 16;
            }
        }
        else
        {
            windowLeft = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
            toTop = workArea.Top + workArea.Height - windowRect.Height - 16;
        }

        if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
        {
            fromTop = toTop;
        }
        else
        {
            if (alwaysBottom == false && (position == 3 || position == 4 || position == 5))
            {
                fromTop = toTop - 26;
            }
            else if (alwaysBottom == false && position == 1)
            {
                fromTop = toTop + 10;
            }
            else
            {
                fromTop = toTop + 26;
            }
        }
    }

    private static void ApplyImmediatePlacement(MicaWindow window, bool alwaysBottom = false, MonitorInfo? selectedMonitor = null)
    {
        try
        {
            var monitor = selectedMonitor != null ? selectedMonitor.Value : GetSelectedMonitor();
            ComputeFlyoutPlacement(window, alwaysBottom, monitor, out double windowLeft, out double _, out double toTop);

            double dpiScale = monitor.dpiY > 0 ? monitor.dpiY : 96.0;
            double targetLeft = windowLeft * 96.0 / dpiScale;
            double targetTop = toTop * 96.0 / dpiScale;

            WindowHelper.SetPosition(window, windowLeft, toTop);
            window.Left = targetLeft;
            window.Top = targetTop;
            window.Opacity = 1.0;

            WindowHelper.SetVisibility(window, true);
            WindowHelper.SetTopmost(window);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "FlyoutAnimationService: Failed to apply immediate placement fallback.");
        }
    }

    public static void OpenAnimation(MicaWindow window, bool alwaysBottom = false, MonitorInfo? selectedMonitor = null)
    {
        var eventTriggers = window.Triggers.Count > 0 ? window.Triggers[0] as EventTrigger : null;
        var beginStoryboard = eventTriggers?.Actions.Count > 0 ? eventTriggers.Actions[0] as BeginStoryboard : null;
        if (beginStoryboard == null)
        {
            Logger.Error("FlyoutAnimationService: Failed to find BeginStoryboard in window triggers. Check XAML structure.");
            ApplyImmediatePlacement(window, alwaysBottom, selectedMonitor);
            return;
        }
        var storyboard = beginStoryboard.Storyboard;

        if (!TryGetStoryboardAnimations(storyboard, out var moveAnimation, out var opacityAnimation, out var scaleXAnimation, out var scaleYAnimation))
        {
            Logger.Error("FlyoutAnimationService: Storyboard missing children (need at least 2: Top, Opacity).");
            ApplyImmediatePlacement(window, alwaysBottom, selectedMonitor);
            return;
        }

        var monitor = selectedMonitor != null ? selectedMonitor.Value : GetSelectedMonitor();
        ComputeFlyoutPlacement(window, alwaysBottom, monitor, out double windowLeft, out double fromTop, out double toTop);

        // Update the DPI by moving the window to the target workArea, ignoring WPF scaling
        WindowHelper.SetPosition(window, windowLeft, fromTop);

        double dpiScale = monitor.dpiY > 0 ? monitor.dpiY : 96.0;
        window.Left = windowLeft * 96.0 / dpiScale;
        moveAnimation!.From = fromTop * 96.0 / dpiScale;
        moveAnimation!.To = toTop * 96.0 / dpiScale;

        int msDuration = GetDuration();
        var easing = GetEasingStyle(true);

        opacityAnimation!.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0;
        opacityAnimation!.To = 1;
        opacityAnimation!.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        opacityAnimation!.EasingFunction = easing;

        moveAnimation!.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        moveAnimation!.EasingFunction = easing;

        if (scaleXAnimation != null && scaleYAnimation != null)
        {
            scaleXAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0.95;
            scaleXAnimation.To = 1.0;
            scaleXAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
            scaleXAnimation.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };

            scaleYAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0.95;
            scaleYAnimation.To = 1.0;
            scaleYAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
            scaleYAnimation.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };
        }

        storyboard.Begin(window);
        WindowHelper.SetTopmost(window);
    }

    public static void CloseAnimation(MicaWindow window, bool alwaysBottom = false, MonitorInfo? selectedMonitor = null)
    {
        var eventTriggers = window.Triggers.Count > 0 ? window.Triggers[0] as EventTrigger : null;
        var beginStoryboard = eventTriggers?.Actions.Count > 0 ? eventTriggers.Actions[0] as BeginStoryboard : null;
        if (beginStoryboard == null) return;
        var storyboard = beginStoryboard.Storyboard;

        if (!TryGetStoryboardAnimations(storyboard, out var moveAnimation, out var opacityAnimation, out var scaleXAnimation, out var scaleYAnimation))
        {
            return;
        }

        var monitor = selectedMonitor != null ? selectedMonitor.Value : GetSelectedMonitor();
        ComputeFlyoutPlacement(window, alwaysBottom, monitor, out double _, out double toTop, out double fromTop);

        double closeDpiScale = monitor.dpiY > 0 ? monitor.dpiY : 96.0;
        moveAnimation!.From = fromTop * 96.0 / closeDpiScale;
        moveAnimation!.To = toTop * 96.0 / closeDpiScale;

        int msDuration = GetDuration();
        var easing = GetEasingStyle(false);

        opacityAnimation!.From = 1;
        opacityAnimation!.To = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0;
        opacityAnimation!.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        opacityAnimation!.EasingFunction = easing;

        moveAnimation!.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        moveAnimation!.EasingFunction = easing;

        if (scaleXAnimation != null && scaleYAnimation != null)
        {
            scaleXAnimation.From = 1.0;
            scaleXAnimation.To = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0.95;
            scaleXAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
            scaleXAnimation.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn };

            scaleYAnimation.From = 1.0;
            scaleYAnimation.To = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0.95;
            scaleYAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
            scaleYAnimation.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn };
        }

        storyboard.Begin(window);
    }
}
