using FluentFlyout.Classes.Settings;
using System.IO;
using Windows.Storage;

namespace FluentFlyoutWPF.Classes.Utils
{
    internal class FileSystemHelper
    {
        public static string GetLogsPath()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentFlyout", "logs");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }
    }
}
