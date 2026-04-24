using FluentFlyout.Classes.Settings;
using System.Windows.Controls;

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
            DataContext = SettingsManager.Current;
        }
    }
}
