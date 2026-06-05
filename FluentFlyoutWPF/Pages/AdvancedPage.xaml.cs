using System.Windows.Controls;
using FluentFlyoutWPF.ViewModels;
using System.ComponentModel;

namespace FluentFlyoutWPF.Pages
{
    /// <summary>
    /// Interaction logic for AdvancedPage.xaml
    /// </summary>
    public partial class AdvancedPage : Page
    {
        public AdvancedPage()
        {
            InitializeComponent();
            DataContext = DesignerProperties.GetIsInDesignMode(this)
                ? DesignTimeViewModelFactory.CreateSettingsShellViewModel()
                : App.GetRequiredService<SettingsShellViewModel>();
        }
    }
}
