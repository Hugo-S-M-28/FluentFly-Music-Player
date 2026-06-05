using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Services;

namespace FluentFlyoutWPF.ViewModels;

public partial class ManageLibraryViewModel : ObservableObject
{
    private readonly IFolderPickerService _folderPickerService;
    private readonly IFileDialogService _fileDialogService;

    public ObservableCollection<string> MusicLibraryFolders => SettingsManager.Current.MusicLibraryFolders;
    public ObservableCollection<string> ExcludedLibraryPaths => SettingsManager.Current.ExcludedLibraryPaths;

    public ManageLibraryViewModel(
        IFolderPickerService folderPickerService,
        IFileDialogService fileDialogService)
    {
        _folderPickerService = folderPickerService;
        _fileDialogService = fileDialogService;
    }

    [RelayCommand]
    private void AddFolder()
    {
        var folder = _folderPickerService.PickFolder("Selecciona una carpeta para agregar a la biblioteca de música");
        if (!string.IsNullOrWhiteSpace(folder) && !MusicLibraryFolders.Contains(folder))
        {
            MusicLibraryFolders.Add(folder);
            SettingsManager.SaveSettings();
            _ = LibraryManager.Instance.ScanLibraryAsync();
        }
    }

    [RelayCommand]
    private void RemoveFolder(string folder)
    {
        if (!string.IsNullOrEmpty(folder))
        {
            MusicLibraryFolders.Remove(folder);
            SettingsManager.SaveSettings();
            LibraryManager.Instance.CleanAndRefreshLibrary();
        }
    }

    [RelayCommand]
    private void AddFolderExclusion()
    {
        var folder = _folderPickerService.PickFolder("Selecciona una carpeta para excluir de la biblioteca");
        if (!string.IsNullOrWhiteSpace(folder) && !ExcludedLibraryPaths.Contains(folder))
        {
            ExcludedLibraryPaths.Add(folder);
            SettingsManager.SaveSettings();
            LibraryManager.Instance.CleanAndRefreshLibrary();
        }
    }

    [RelayCommand]
    private void AddFileExclusion()
    {
        var file = _fileDialogService.OpenFile(
            "Selecciona un archivo para excluir de la biblioteca",
            "Archivos de Audio (*.mp3;*.m4a;*.flac;*.wav;*.wma)|*.mp3;*.m4a;*.flac;*.wav;*.wma|Todos los archivos (*.*)|*.*");

        if (!string.IsNullOrWhiteSpace(file) && !ExcludedLibraryPaths.Contains(file))
        {
            ExcludedLibraryPaths.Add(file);
            SettingsManager.SaveSettings();
            LibraryManager.Instance.CleanAndRefreshLibrary();
        }
    }

    [RelayCommand]
    private void RemoveExclusion(string exclusion)
    {
        if (!string.IsNullOrEmpty(exclusion))
        {
            ExcludedLibraryPaths.Remove(exclusion);
            SettingsManager.SaveSettings();
            _ = LibraryManager.Instance.ScanLibraryAsync();
        }
    }

    [RelayCommand]
    private void CloseWindow(object? windowObj)
    {
        if (windowObj is Window window)
        {
            window.Close();
        }
    }
}
