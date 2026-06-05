using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.ViewModels;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;

namespace FluentFlyoutWPF.Pages;

public partial class LibraryPage : Page
{
    private readonly LibraryViewModel _viewModel;

    public LibraryPage()
    {
        InitializeComponent();
        _viewModel = DesignerProperties.GetIsInDesignMode(this)
            ? DesignTimeViewModelFactory.CreateLibraryViewModel()
            : App.GetRequiredService<LibraryViewModel>();
        DataContext = _viewModel;
    }

    private void TrackItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListViewItem { DataContext: TrackModel track })
        {
            _viewModel.PlayTrackCommand.Execute(track);
            e.Handled = true;
        }
    }
}
