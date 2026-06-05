using System.Windows.Controls;
using FluentFlyoutWPF.ViewModels;
using System.ComponentModel;

namespace FluentFlyoutWPF.Pages;

public partial class TaskbarVisualizerPage : Page
{
    public TaskbarVisualizerPage()
    {
        InitializeComponent();
        DataContext = DesignerProperties.GetIsInDesignMode(this)
            ? DesignTimeViewModelFactory.CreateSettingsShellViewModel()
            : App.GetRequiredService<SettingsShellViewModel>();
    }
}
