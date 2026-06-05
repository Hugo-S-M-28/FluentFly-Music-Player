using System.Windows;
using FluentFlyoutWPF.ViewModels;

namespace FluentFlyoutWPF.Windows;

public partial class ManageLibraryWindow : Wpf.Ui.Controls.FluentWindow
{
    public ManageLibraryWindow(ManageLibraryViewModel viewModel)
    {
        InitializeComponent();
        Owner = System.Windows.Application.Current.MainWindow;
        DataContext = viewModel;
    }

}
