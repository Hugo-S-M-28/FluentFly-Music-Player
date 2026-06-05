using System;
using System.Windows;
using Microsoft.Win32;

namespace FluentFlyoutWPF.Classes.Services;

public class FileDialogService : IFileDialogService
{
    public string? OpenFile(string title, string filter)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter
            };
            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }
            return null;
        });
    }

    public string? SaveFile(string title, string filter, string defaultFileName = "")
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = defaultFileName
            };
            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }
            return null;
        });
    }
}
