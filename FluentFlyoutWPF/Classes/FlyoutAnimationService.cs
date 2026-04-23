using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Utils;
using MicaWPF.Controls;
using System;
using System.Windows;
using System.Windows.Media.Animation;
using static FluentFlyoutWPF.Classes.Utils.MonitorUtil;

namespace FluentFlyoutWPF.Classes;

public static class FlyoutAnimationService
{
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
        if (beginStoryboard == null) return;
        var storyboard = beginStoryboard.Storyboard;

        DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];
        var monitor = selectedMonitor != null ? selectedMonitor.Value : GetSelectedMonitor();
        var workArea = monitor.workArea;

        // prevent flickering
        WindowHelper.SetVisibility(window, false);

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
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height + 4;
            }
            else if (position == 1)
            {
                window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 80;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height - 60;
            }
            else if (position == 2)
            {
                window_left = workArea.Left + workArea.Width - windowRect.Width - 16;
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height + 4;
            }
            else if (position == 3)
            {
                window_left = workArea.Left + 16;
                moveAnimation.To = workArea.Top + 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + -4;
            }
            else if (position == 4)
            {
                window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
                moveAnimation.To = workArea.Top + 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + -4;
            }
            else if (position == 5)
            {
                window_left = workArea.Left + workArea.Width - windowRect.Width - 16;
                moveAnimation.To = workArea.Top + 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                    moveAnimation.From = moveAnimation.To;
                else
                    moveAnimation.From = workArea.Top + -4;
            }
        }
        else
        {
            window_left = workArea.Left + workArea.Width / 2 - windowRect.Width / 2;
            moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 16;
            if (SettingsManager.Current.FlyoutAnimationSpeed == 0)
                moveAnimation.From = moveAnimation.To;
            else
                moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height + 4;
        }

        WindowHelper.SetPosition(window, window_left, moveAnimation.From!.Value);

        double dpiScale = monitor.dpiY > 0 ? monitor.dpiY : 96.0;
        moveAnimation.From *= 96.0 / dpiScale;
        moveAnimation.To *= 96.0 / dpiScale;

        int msDuration = GetDuration();

        DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        if (SettingsManager.Current.FlyoutAnimationSpeed != 0) opacityAnimation.From = 0;
        opacityAnimation.To = 1;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        if (SettingsManager.Current.FlyoutAnimationEasingStyle == 0) moveAnimation.EasingFunction = opacityAnimation.EasingFunction = null;
        else moveAnimation.EasingFunction = opacityAnimation.EasingFunction = GetEasingStyle(true);
        moveAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        storyboard.Begin(window);
        WindowHelper.SetVisibility(window, true);
        WindowHelper.SetTopmost(window);
    }

    public static void CloseAnimation(MicaWindow window, bool alwaysBottom = false, MonitorInfo? selectedMonitor = null)
    {
        var eventTriggers = window.Triggers[0] as EventTrigger;
        var beginStoryboard = eventTriggers?.Actions[0] as BeginStoryboard;
        if (beginStoryboard == null) return;
        var storyboard = beginStoryboard.Storyboard;

        DoubleAnimation moveAnimation = (DoubleAnimation)storyboard.Children[0];
        var monitor = selectedMonitor != null ? selectedMonitor.Value : GetSelectedMonitor();
        var workArea = monitor.workArea;
        var windowRect = WindowHelper.GetPlacement(window);

        int position = SettingsManager.Current.Position;

        if (alwaysBottom == false)
        {
            if (position == 0 || position == 2)
            {
                moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height - 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed != 0)
                    moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height + 4;
            }
            else if (position == 1)
            {
                moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height - 80;
                if (SettingsManager.Current.FlyoutAnimationSpeed != 0)
                    moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height - 60;
            }
            else if (position == 3 || position == 4 || position == 5)
            {
                moveAnimation.From = workArea.Top + 16;
                if (SettingsManager.Current.FlyoutAnimationSpeed != 0)
                    moveAnimation.To = workArea.Top + -4;
            }
        }
        else
        {
            moveAnimation.From = workArea.Top + workArea.Height - windowRect.Height - 16;
            if (SettingsManager.Current.FlyoutAnimationSpeed != 0)
                moveAnimation.To = workArea.Top + workArea.Height - windowRect.Height + 4;
        }

        double closeDpiScale = monitor.dpiY > 0 ? monitor.dpiY : 96.0;
        moveAnimation.From *= 96.0 / closeDpiScale;
        if (moveAnimation.To != null)
            moveAnimation.To *= 96.0 / closeDpiScale;

        int msDuration = GetDuration();

        DoubleAnimation opacityAnimation = (DoubleAnimation)storyboard.Children[1];
        opacityAnimation.From = 1;
        if (SettingsManager.Current.FlyoutAnimationSpeed != 0) opacityAnimation.To = 0;
        opacityAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        if (SettingsManager.Current.FlyoutAnimationEasingStyle == 0) moveAnimation.EasingFunction = opacityAnimation.EasingFunction = null;
        else moveAnimation.EasingFunction = opacityAnimation.EasingFunction = GetEasingStyle(false);
        moveAnimation.Duration = new Duration(TimeSpan.FromMilliseconds(msDuration));

        storyboard.Begin(window);
    }
}
