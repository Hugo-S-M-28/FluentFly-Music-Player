using System.Threading.Tasks;

namespace FluentFlyoutWPF.Classes.Services;

public interface IDialogService
{
    Task ShowErrorAsync(string title, string message);
    Task ShowMessageAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);
}
