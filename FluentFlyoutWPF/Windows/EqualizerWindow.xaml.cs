using CommunityToolkit.Mvvm.Messaging;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.ViewModels;
using System;
using System.Windows;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF.Windows;

public partial class EqualizerWindow : FluentWindow
{
    private readonly EqualizerViewModel _viewModel;

    public EqualizerWindow(EqualizerViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += EqualizerWindow_Loaded;
        Closed += EqualizerWindow_Closed;
    }

    private void EqualizerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResolvedAccent();

        WeakReferenceMessenger.Default.Register<UpdateAccentColorMessage>(this, static (recipient, _) =>
        {
            if (recipient is EqualizerWindow window)
            {
                window.Dispatcher.InvokeAsync(window.ApplyResolvedAccent);
            }
        });

        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, static (recipient, _) =>
        {
            if (recipient is EqualizerWindow window)
            {
                window.Dispatcher.InvokeAsync(window.ApplyResolvedAccent);
            }
        });
    }

    private void EqualizerWindow_Closed(object? sender, EventArgs e)
    {
        WeakReferenceMessenger.Default.UnregisterAll(this);
        _viewModel.Dispose();
    }

    private void ApplyResolvedAccent()
    {
        AccentResourceHelper.ApplyResolvedAccentResources(Resources);
    }
}
