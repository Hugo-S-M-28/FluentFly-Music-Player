using FluentFlyout.Classes.Settings;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Wpf.Ui.Appearance;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyout.Classes;

public static class WindowBlurHelper
{
    public static void ApplyWindowBackdrop(Window window)
    {
        if (window == null)
        {
            return;
        }

        if (ShouldHaveAcrylicBlur(window))
        {
            EnableBlur(window, ResolveBlurOpacity(), ResolveBlurBackgroundColor());
        }
        else
        {
            DisableBlur(window);
        }
    }

    public static void RefreshAllWindowBackdrops()
    {
        foreach (Window window in Application.Current.Windows)
        {
            if (window == null || !IsManagedBackdropWindow(window))
            {
                continue;
            }

            ApplyWindowBackdrop(window);
        }
    }

    /// <summary>
    /// Enables acrylic blur effect on the specified window
    /// </summary>
    /// <param name="window">The window to apply blur to</param>
    /// <param name="blurOpacity">Opacity of the blur (0-255)</param>
    /// <param name="blurBackgroundColor">Background color in BGR format (default: 0x000000)</param>
    public static void EnableBlur(Window window, uint blurOpacity = 175, uint blurBackgroundColor = 0x000000)
    {
        var windowHelper = new WindowInteropHelper(window);

        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
            GradientColor = (blurOpacity << 24) | (blurBackgroundColor & 0xFFFFFF)
        };

        var accentStructSize = Marshal.SizeOf(accent);
        var accentPtr = Marshal.AllocHGlobal(accentStructSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };

        SetWindowCompositionAttribute(windowHelper.Handle, ref data);

        Marshal.FreeHGlobal(accentPtr);
    }

    /// <summary>
    /// Disables blur effect on the specified window
    /// </summary>
    /// <param name="window">The window to disable blur on</param>
    public static void DisableBlur(Window window)
    {
        var windowHelper = new WindowInteropHelper(window);

        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_DISABLED
        };

        var accentStructSize = Marshal.SizeOf(accent);
        var accentPtr = Marshal.AllocHGlobal(accentStructSize);
        Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };

        SetWindowCompositionAttribute(windowHelper.Handle, ref data);

        Marshal.FreeHGlobal(accentPtr);
    }

    /// <summary>
    /// Adjusts the blur opacity for all windows that have acrylic blur enabled
    /// </summary>
    /// <param name="newBlurOpacity">New opacity value (0-255)</param>
    public static void AdjustBlurOpacityForAllWindows(uint newBlurOpacity)
    {
        if (!SettingsManager.Current.IsPremiumUnlocked) return;
        RefreshAllWindowBackdrops();
    }

    /// <summary>
    /// Checks if a window should have acrylic blur enabled based on settings
    /// </summary>
    private static bool ShouldHaveAcrylicBlur(Window window)
    {
        var windowType = window.GetType().Name;

        return windowType switch
        {
            "MainWindow" => SettingsManager.Current.MediaFlyoutAcrylicWindowEnabled,
            "NextUpWindow" => SettingsManager.Current.NextUpAcrylicWindowEnabled,
            "LockWindow" => SettingsManager.Current.LockKeysAcrylicWindowEnabled,
            _ => false
        };
    }

    private static bool IsManagedBackdropWindow(Window window)
    {
        return window.GetType().Name switch
        {
            "MainWindow" or "NextUpWindow" or "LockWindow" => true,
            _ => false
        };
    }

    private static uint ResolveBlurOpacity()
    {
        uint blurOpacity = 175;

        if (SettingsManager.Current.IsPremiumUnlocked)
        {
            blurOpacity = SettingsManager.Current.AcrylicBlurOpacity;
        }

        return Math.Clamp(blurOpacity, 0, 255);
    }

    private static uint ResolveBlurBackgroundColor()
    {
        var currentTheme = ApplicationThemeManager.GetAppTheme();
        return currentTheme == ApplicationTheme.Light ? 0xFFFFFFu : 0x000000u;
    }
}
