using System;
using System.Threading.Tasks;
using System.Windows;

namespace FluentFlyoutWPF.Classes.Services;

public class DialogService : IDialogService
{
    public Task ShowErrorAsync(string title, string message)
    {
        return Application.Current.Dispatcher.Invoke(async () =>
        {
            Wpf.Ui.Controls.MessageBox messageBox = new()
            {
                Title = title,
                Content = message,
                CloseButtonText = Application.Current.TryFindResource("General_Ok")?.ToString() ?? "OK"
            };
            await messageBox.ShowDialogAsync();
        });
    }

    public Task ShowMessageAsync(string title, string message)
    {
        return Application.Current.Dispatcher.Invoke(async () =>
        {
            Wpf.Ui.Controls.MessageBox messageBox = new()
            {
                Title = title,
                Content = message,
                CloseButtonText = Application.Current.TryFindResource("General_Ok")?.ToString() ?? "OK"
            };
            await messageBox.ShowDialogAsync();
        });
    }

    public Task<bool> ShowConfirmAsync(string title, string message)
    {
        return Application.Current.Dispatcher.Invoke(async () =>
        {
            Wpf.Ui.Controls.MessageBox confirmBox = new()
            {
                Title = title,
                Content = message,
                PrimaryButtonText = Application.Current.TryFindResource("General_Yes")?.ToString() ?? "Yes",
                CloseButtonText = Application.Current.TryFindResource("General_No")?.ToString() ?? "No"
            };
            var result = await confirmBox.ShowDialogAsync();
            return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
        });
    }
}
