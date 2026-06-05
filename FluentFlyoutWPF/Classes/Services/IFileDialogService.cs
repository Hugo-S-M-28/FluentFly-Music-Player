namespace FluentFlyoutWPF.Classes.Services;

public interface IFileDialogService
{
    string? OpenFile(string title, string filter);
    string? SaveFile(string title, string filter, string defaultFileName = "");
}
