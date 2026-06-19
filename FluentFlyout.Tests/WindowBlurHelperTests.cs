using System;
using System.Windows;
using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using Xunit;

namespace FluentFlyout.Tests;

public class WindowBlurHelperTests
{
    private class MainWindow : Window { }
    private class NextUpWindow : Window { }
    private class LockWindow : Window { }

    private void RunInSta(Action action)
    {
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                throw new Exception("STA Thread Exception", ex);
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    public void IsAcrylicSupported_ReturnsTrueOnWindows10OrLater()
    {
        bool isSupported = WindowBlurHelper.IsAcrylicSupported;
        bool expected = Environment.OSVersion.Platform == PlatformID.Win32NT &&
                        Environment.OSVersion.Version.Major >= 10;
        Assert.Equal(expected, isSupported);
    }

    [Fact]
    public void ApplyWindowBackdrop_WithNullWindow_DoesNotThrow()
    {
        WindowBlurHelper.ApplyWindowBackdrop(null!);
    }

    [Fact]
    public void ApplyWindowBackdrop_UnmanagedWindow_DisablesBlur()
    {
        RunInSta(() =>
        {
            var window = new Window();
            // Since window type is "Window", ShouldHaveAcrylicBlur is false.
            // It will call DisableBlur, which returns early because Handle is Zero.
            WindowBlurHelper.ApplyWindowBackdrop(window);
        });
    }

    [Fact]
    public void ApplyWindowBackdrop_MainWindow_EnabledWithoutCrash()
    {
        RunInSta(() =>
        {
            var window = new MainWindow();
            SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled = true;
            SettingsManager.Current.IsPremiumUnlocked = false;

            // ShouldHaveAcrylicBlur evaluates to true.
            // It resolves opacity, and calls EnableBlur.
            // Handle is Zero, so it returns safely.
            WindowBlurHelper.ApplyWindowBackdrop(window);
        });
    }

    [Fact]
    public void ApplyWindowBackdrop_NextUpWindow_EnabledWithoutCrash()
    {
        RunInSta(() =>
        {
            var window = new NextUpWindow();
            SettingsManager.Current.NextUpAcrylicWindowEnabled = true;
            SettingsManager.Current.IsPremiumUnlocked = true;
            SettingsManager.Current.AcrylicBlurOpacity = 150;

            WindowBlurHelper.ApplyWindowBackdrop(window);
        });
    }

    [Fact]
    public void ApplyWindowBackdrop_LockWindow_EnabledWithoutCrash()
    {
        RunInSta(() =>
        {
            var window = new LockWindow();
            SettingsManager.Current.LockKeysAcrylicWindowEnabled = true;

            WindowBlurHelper.ApplyWindowBackdrop(window);
        });
    }
}
