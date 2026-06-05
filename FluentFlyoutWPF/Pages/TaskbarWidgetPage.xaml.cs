using FluentFlyoutWPF.ViewModels;
using System.ComponentModel;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

public partial class TaskbarWidgetPage : Page
{
    public TaskbarWidgetPage()
    {
        InitializeComponent();
        DataContext = DesignerProperties.GetIsInDesignMode(this)
            ? DesignTimeViewModelFactory.CreateSettingsShellViewModel()
            : App.GetRequiredService<SettingsShellViewModel>();
    }
}
