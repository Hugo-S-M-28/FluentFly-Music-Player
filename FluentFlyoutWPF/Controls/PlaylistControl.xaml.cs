using FluentFlyoutWPF.ViewModels;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Classes;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace FluentFlyoutWPF.Controls;

public partial class PlaylistControl : UserControl
{
    public static readonly DependencyProperty CloseCommandProperty =
        DependencyProperty.Register(
            nameof(CloseCommand),
            typeof(ICommand),
            typeof(PlaylistControl),
            new PropertyMetadata(null));

    private readonly PlaylistViewModel _viewModel;

    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public PlaylistControl()
    {
        InitializeComponent();
        _viewModel = DesignerProperties.GetIsInDesignMode(this)
            ? DesignTimeViewModelFactory.CreatePlaylistViewModel()
            : App.GetRequiredService<PlaylistViewModel>();
        RootCard.DataContext = _viewModel;
    }
}


public class IsCurrentTrackConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is TrackModel item && values[1] is TrackModel current)
        {
            return item.FilePath == current.FilePath ? Visibility.Visible : Visibility.Collapsed;
        }

        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
