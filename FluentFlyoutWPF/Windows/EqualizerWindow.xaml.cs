// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.ViewModels;
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
        Closed += (_, _) => _viewModel.Dispose();
    }
}
