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

    public static void OpenAnimation(MicaWindow window, bool alwaysBottom = false, MonitorInfo? selectedMonitor = null)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers?.Actions[0] as BeginStoryboard;
        if (beginStoryboard == null)
        {
            Logger.Error("FlyoutAnimationService: Failed to find BeginStoryboard in window triggers. Check XAML structure.");
            return;
        }
        var storyboard = beginStoryboard.Storyboard;

        if (storyboard.Children.Count < 4)
        {
            Logger.Error("FlyoutAnimationService: Storyboard missing children for polished animation. Check XAML structure.");
            return;
        }

        var moveAnimation = (DoubleAnimation)storyboard.Children[0];
        var opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        var scaleXAnimation = (DoubleAnimation)storyboard.Children[2];
        var scaleYAnimation = (DoubleAnimation)storyboard.Children[3];

        var monitor = selectedMonitor != null ? selectedMonitor.Value : GetSelectedMonitor();
        var workArea = monitor.workArea;

        // Update the DPI by moving the window to the target workArea, ignoring WPF scaling
        WindowHelper.SetPosition(window, workArea.Left, workArea.Top);
        var windowRect = WindowHelper.GetPlacement(window);

        double window_left = 0;
        int position = SettingsManager.Current.Position;

        if (alwaysBottom == false)
        {
            if (position == 0)
            {
                window_left = workArea.Left + 16;
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16;
                moveAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.To : workArea.Top + workArea.Height - windowRect.Height + 10;
            }
            else if (position == 1)
            {
                window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 80;
                moveAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.To : workArea.Top + workArea.Height - windowRect.Height - 70;
            }
            else if (position == 2)
            {
                window_left = workArea.Left + workArea.Width - windowRect.Width - 16;
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16;
                moveAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.To : workArea.Top + workArea.Height - windowRect.Height + 10;
            }
            else if (position == 3)
            {
                window_left = workArea.Left + 16;
                moveAnimation.To = workArea.Top + 16;
                moveAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.To : workArea.Top - 10;
            }
            else if (position == 4)
            {
                window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
                moveAnimation.To = workArea.Top + 16;
                moveAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.To : workArea.Top - 10;
            }
            else if (position == 5)
            {
                window_left = workArea.Left + workArea.Width - windowRect.Width - 16;
                moveAnimation.To = workArea.Top + 16;
                moveAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.To : workArea.Top - 10;
            }
        }
        else
        {
            window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
            moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16;
            moveAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.To : workArea.Top + workArea.Height - windowRect.Height + 10;
        }

        WindowHelper.SetPosition(window, window_left, moveAnimation.From!.Value);

        double dpiScale = monitor.dpiY > 0 ? monitor.dpiY : 96.0;
        window.Left = window_left * 96.0 / dpiScale;
        moveAnimation.From *= 96.0 / dpiScale;
        moveAnimation.To *= 96.0 / dpiScale;

        int msDuration = GetDuration();
        var easing = GetEasingStyle(true);

        opacityAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0;
        opacityAnimation.To = 1;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        opacityAnimation.EasingFunction = easing;

        moveAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        moveAnimation.EasingFunction = easing;

        scaleXAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0.95;
        scaleXAnimation.To = 1.0;
        scaleXAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        scaleXAnimation.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };

        scaleYAnimation.From = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0.95;
        scaleYAnimation.To = 1.0;
        scaleYAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        scaleYAnimation.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut };

        storyboard.Begin(window);
        WindowHelper.SetTopmost(window);
    }

    public static void CloseAnimation(MicaWindow window, bool alwaysBottom = false, MonitorInfo? selectedMonitor = null)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers?.Actions[0] as BeginStoryboard;
        if (beginStoryboard == null) return;
        var storyboard = beginStoryboard.Storyboard;

        if (storyboard.Children.Count < 4) return;

        var moveAnimation = (DoubleAnimation)storyboard.Children[0];
        var opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        var scaleXAnimation = (DoubleAnimation)storyboard.Children[2];
        var scaleYAnimation = (DoubleAnimation)storyboard.Children[3];

        var monitor = selectedMonitor != null ? selectedMonitor.Value : GetSelectedMonitor();
        var workArea = monitor.workArea;
        var windowRect = WindowHelper.GetPlacement(window);

        int position = SettingsManager.Current.Position;

        if (alwaysBottom == false)
        {
            if (position == 0 || position == 2)
            {
                moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height - 16;
                moveAnimation.To = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.From : workArea.Top + workArea.Height - windowRect.Height + 10;
            }
            else if (position == 1)
            {
                moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height - 80;
                moveAnimation.To = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.From : workArea.Top + workArea.Height - windowRect.Height - 70;
            }
            else if (position == 3 || position == 4 || position == 5)
            {
                moveAnimation.From = workArea.Top + 16;
                moveAnimation.To = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.From : workArea.Top - 10;
            }
        }
        else
        {
            moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height - 16;
            moveAnimation.To = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? moveAnimation.From : workArea.Top + workArea.Height - windowRect.Height + 10;
        }

        double closeDpiScale = monitor.dpiY > 0 ? monitor.dpiY : 96.0;
        moveAnimation.From *= 96.0 / closeDpiScale;
        if (moveAnimation.To != null)
            moveAnimation.To *= 96.0 / closeDpiScale;

        int msDuration = GetDuration();
        var easing = GetEasingStyle(false);

        opacityAnimation.From = 1;
        opacityAnimation.To = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        opacityAnimation.EasingFunction = easing;

        moveAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        moveAnimation.EasingFunction = easing;

        scaleXAnimation.From = 1.0;
        scaleXAnimation.To = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0.95;
        scaleXAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        scaleXAnimation.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn };

        scaleYAnimation.From = 1.0;
        scaleYAnimation.To = SettingsManager.Current.FlyoutAnimationSpeed == 0 ? 1 : 0.95;
        scaleYAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));
        scaleYAnimation.EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn };

        storyboard.Begin(window);
    }
}
