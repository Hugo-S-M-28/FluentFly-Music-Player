using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using FluentFlyout.Classes.Settings;
using Wpf.Ui.Appearance;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyout.Classes;

public static class WindowBlurHelper
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
    private static bool? _isAcrylicSupported;
    private static bool _acrylicFailedInSession = false;
    private static readonly Dictionary<IntPtr, Window> _activeAcrylicWindows = new();

    public static string? LastAcrylicFailureMessage { get; private set; }
    public static event EventHandler? AcrylicSupportChanged;

    public static bool IsAcrylicSupported
    {
        get
        {
            if (_acrylicFailedInSession)
            {
                return false;
            }

            if (!_isAcrylicSupported.HasValue)
            {
                try
                {
                    _isAcrylicSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299);
                }
                catch
                {
                    _isAcrylicSupported = false;
                }
            }
            return _isAcrylicSupported.Value;
        }
    }

    private static void TrackAcrylicWindow(IntPtr hwnd, Window window)
    {
        if (!_activeAcrylicWindows.ContainsKey(hwnd))
        {
            _activeAcrylicWindows[hwnd] = window;
            window.Closed -= Window_Closed;
            window.Closed += Window_Closed;
        }
    }

    private static void UntrackAcrylicWindow(IntPtr hwnd)
    {
        if (_activeAcrylicWindows.Remove(hwnd, out var window))
        {
            window.Closed -= Window_Closed;
        }
    }

    private static void Window_Closed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.Closed -= Window_Closed;
            var match = _activeAcrylicWindows.FirstOrDefault(x => x.Value == window);
            if (match.Key != IntPtr.Zero)
            {
                _activeAcrylicWindows.Remove(match.Key);
            }
        }
    }

    public static void ApplyWindowBackdrop(Window window, int? mediaBackdropPreset = null)
    {
        if (window == null)
        {
            return;
        }

        if (ShouldHaveAcrylicBlur(window))
        {
            // Resolve preset
            int preset = 0;
            if (mediaBackdropPreset.HasValue)
            {
                preset = mediaBackdropPreset.Value;
            }
            else if (window.GetType().Name == "MainWindow")
            {
                preset = SettingsManager.Current.MediaFlyoutBackgroundBlur;
            }

            // Resolve opacity
            uint opacity = 175;
            if (SettingsManager.Current.IsPremiumUnlocked)
            {
                opacity = SettingsManager.Current.AcrylicBlurOpacity;
            }
            else
            {
                if (preset == 1 || preset == 2)
                {
                    opacity = 70;
                }
                else
                {
                    opacity = 175;
                }
            }
            opacity = Math.Clamp(opacity, 0u, 255u);

            uint color = ResolveBlurBackgroundColor();
            EnableBlur(window, opacity, color);
        }
        else
        {
            DisableBlur(window);
        }
    }

    public static void RefreshAllWindowBackdrops()
    {
        if (Application.Current == null)
        {
            return;
        }

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
        if (window == null) return;
        if (!IsAcrylicSupported)
        {
            DisableBlur(window);
            return;
        }

        var windowHelper = new WindowInteropHelper(window);
        if (windowHelper.Handle == IntPtr.Zero)
        {
            return;
        }

        IntPtr accentPtr = IntPtr.Zero;
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GradientColor = (blurOpacity << 24) | (blurBackgroundColor & 0xFFFFFF)
            };

            var accentStructSize = Marshal.SizeOf(accent);
            accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            int result = SetWindowCompositionAttribute(windowHelper.Handle, ref data);
            if (result == 0)
            {
                throw new COMException("SetWindowCompositionAttribute returned 0");
            }

            TrackAcrylicWindow(windowHelper.Handle, window);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to enable acrylic blur for window {WindowName}", window.GetType().Name);
            _acrylicFailedInSession = true;
            LastAcrylicFailureMessage = ex.Message;
            AcrylicSupportChanged?.Invoke(null, EventArgs.Empty);

            DisableBlur(window);
        }
        finally
        {
            if (accentPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
    }

    /// <summary>
    /// Disables blur effect on the specified window
    /// </summary>
    /// <param name="window">The window to disable blur on</param>
    public static void DisableBlur(Window window)
    {
        if (window == null) return;
        var windowHelper = new WindowInteropHelper(window);
        if (windowHelper.Handle == IntPtr.Zero) return;

        IntPtr accentPtr = IntPtr.Zero;
        try
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_DISABLED
            };

            var accentStructSize = Marshal.SizeOf(accent);
            accentPtr = Marshal.AllocHGlobal(accentStructSize);
            Marshal.StructureToPtr(accent, accentPtr, false);

            var data = new WindowCompositionAttributeData
            {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            SetWindowCompositionAttribute(windowHelper.Handle, ref data);
            
            UntrackAcrylicWindow(windowHelper.Handle);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to disable acrylic blur for window {WindowName}", window.GetType().Name);
        }
        finally
        {
            if (accentPtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }
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
