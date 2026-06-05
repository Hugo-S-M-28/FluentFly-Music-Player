namespace FluentFlyoutWPF.Classes.Services;

public interface ISystemShellService
{
    void RevealInFileExplorer(string filePath);
    void OpenUrl(string url);
}
