using System;
using System.Windows;
using FluentFlyoutWPF.ViewModels;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.Windows;

public partial class EditTrackWindow : FluentWindow
{
    public EditTrackViewModel ViewModel { get; }

    public EditTrackWindow(EditTrackViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        // Hover effect for album art overlay (purely visual state management)
        AlbumArtBorder.MouseEnter += (_, _) => ArtOverlay.Opacity = 1;
        AlbumArtBorder.MouseLeave += (_, _) => ArtOverlay.Opacity = 0;
    }

    public void SelectLyricsTab()
    {
        if (FindName("LyricsTab") is System.Windows.Controls.TabItem tab)
        {
            tab.IsSelected = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.Cleanup();
        base.OnClosed(e);
    }
}
