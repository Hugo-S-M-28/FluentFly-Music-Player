using Microsoft.Win32;
using MicaWPF.Core.Enums;

namespace FluentFlyoutWPF.Classes.Utils;

public static class WindowsThemeHelper
{
    public static WindowsTheme GetCurrentWindowsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            if (value is int i && i == 0) return WindowsTheme.Dark;
        }
        catch 
        { 
            // Fallback to Light if registry access fails
        }
        return WindowsTheme.Light;
    }
}
