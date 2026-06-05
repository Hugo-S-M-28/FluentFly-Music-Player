using System.Windows;
using System.Windows.Forms;

namespace FluentFlyoutWPF.Classes.Services;

public class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string description)
    {
        return System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = description,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };

            return dialog.ShowDialog() == DialogResult.OK
                ? dialog.SelectedPath
                : null;
        });
    }
}
