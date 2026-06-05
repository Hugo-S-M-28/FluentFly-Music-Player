using System.Diagnostics;
using System.IO;

namespace FluentFlyoutWPF.Classes.Services;

public class SystemShellService : ISystemShellService
{
    public void RevealInFileExplorer(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(filePath);
        if (directory == null || !Directory.Exists(directory))
        {
            return;
        }

        Process.Start("explorer.exe", $"/select,\"{filePath}\"");
    }

    public void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            NLog.LogManager.GetCurrentClassLogger().Error(ex, "Failed to open URL: {Url}", url);
        }
    }
}
