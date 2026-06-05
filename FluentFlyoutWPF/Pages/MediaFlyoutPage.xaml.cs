using System.Windows.Controls;
using FluentFlyoutWPF.ViewModels;
using System.ComponentModel;

namespace FluentFlyoutWPF.Pages;

public partial class MediaFlyoutPage : Page
{
    public MediaFlyoutPage()
    {
        InitializeComponent();
        DataContext = DesignerProperties.GetIsInDesignMode(this)
            ? DesignTimeViewModelFactory.CreateSettingsShellViewModel()
            : App.GetRequiredService<SettingsShellViewModel>();
    }
}
