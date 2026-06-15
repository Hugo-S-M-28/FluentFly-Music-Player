// Copyright © 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyoutWPF.Classes.Messages;
using FluentFlyoutWPF.Classes.Utils;
using FluentFlyoutWPF.Pages;
using CommunityToolkit.Mvvm.Messaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace FluentFlyoutWPF;

public partial class SettingsWindow : FluentWindow
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private Type? _currentPageType;

    public ViewModels.SettingsShellViewModel ViewModel { get; }

    public SettingsWindow(ViewModels.SettingsShellViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        RootNavigation.SetCurrentValue(NavigationView.IsPaneOpenProperty, false);
        PreviewMouseWheel += SettingsWindow_PreviewMouseWheel;
        SizeChanged += SettingsWindow_SizeChanged;
    }

    public void NavigateToPage(Type pageType)
    {
        _currentPageType = pageType;
        RootNavigation.Navigate(pageType);
    }

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyResolvedAccentToNavigation();

        RootNavigation.IsPaneOpen = false;

        _currentPageType ??= typeof(HomePage);
        RootNavigation.Navigate(_currentPageType);

        // wrkaround for WPF-UI NavigationView theme change bug:
        // force pane initialization by toggling it once to prevent width corruption on theme changes
        // not sure why this has to be done
        await Task.Delay(100);
        RootNavigation.IsPaneOpen = true;
        await Task.Delay(10);
        RootNavigation.IsPaneOpen = false;

        await LicenseManager.GetPremiumProductInfoAsync();

        RootNavigation.Navigated += (s, args) =>
        {
            _currentPageType = args.Page?.GetType();
            SyncCurrentPageScrollViewerViewport();
            ResetScrollPosition();
        };

        ViewModel.Settings.PropertyChanged += OnSettingsPropertyChanged;

        WeakReferenceMessenger.Default.Register<UpdateAccentColorMessage>(this, static (recipient, _) =>
        {
            if (recipient is SettingsWindow window)
            {
                window.Dispatcher.InvokeAsync(window.ApplyResolvedAccentToNavigation);
            }
        });

        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, static (recipient, _) =>
        {
            if (recipient is SettingsWindow window)
            {
                window.Dispatcher.InvokeAsync(window.ApplyResolvedAccentToNavigation);
            }
        });
    }

    private async void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ViewModels.UserSettings.AppTheme))
        {
            // force fix pane state after theme change
            await Dispatcher.InvokeAsync(async () =>
            {
                var wasPaneOpen = RootNavigation.IsPaneOpen;

                await Task.Delay(100);
                if (!IsLoaded) return;

                try
                {
                    RootNavigation.IsPaneOpen = !wasPaneOpen;
                    await Task.Delay(10);
                    if (!IsLoaded) return;
                    RootNavigation.IsPaneOpen = wasPaneOpen;

                    await Task.Delay(300);
                    if (!IsLoaded) return;
                    RootNavigation.Navigate(_currentPageType ?? typeof(HomePage));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error during NavigationView theme change workaround");
                }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void SettingsWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        ViewModel.Settings.PropertyChanged -= OnSettingsPropertyChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        PreviewMouseWheel -= SettingsWindow_PreviewMouseWheel;
        SizeChanged -= SettingsWindow_SizeChanged;
        SettingsManager.SaveSettings();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsManager.SaveSettings();
        Close();
    }

    private void ResetScrollPosition()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                RootNavigation.UpdateLayout();
                SyncCurrentPageScrollViewerViewport();
                var contentScrollViewer = FindCurrentPageScrollViewer();

                if (contentScrollViewer != null)
                {
                    contentScrollViewer.ScrollToVerticalOffset(0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error resetting scroll position in SettingsWindow");
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // helper functions to traverse visual tree

    private static T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild && typedChild.Name == name)
            {
                return typedChild;
            }

            var result = FindChildByName<T>(child, name);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private static ScrollViewer? FindNamedPageScrollViewer(DependencyObject parent)
    {
        if (parent is FrameworkElement namedElement)
        {
            if (namedElement.Name == "PageScrollViewer" && namedElement is ScrollViewer namedScrollViewer)
            {
                return namedScrollViewer;
            }
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            var result = FindNamedPageScrollViewer(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private ScrollViewer? FindCurrentPageScrollViewer()
    {
        var frame = FindVisualChild<Frame>(RootNavigation);
        if (frame?.Content is DependencyObject page)
        {
            return FindNamedPageScrollViewer(page);
        }

        return FindNamedPageScrollViewer(RootNavigation);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var result = FindVisualChild<T>(child);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private void NavigationViewItem_Click(object sender, RoutedEventArgs e)
    {

    }

    private void SettingsWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncCurrentPageScrollViewerViewport();
    }

    private void SettingsWindow_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled)
            return;

        if (e.OriginalSource is DependencyObject source &&
            (FindVisualParent<Slider>(source) != null ||
             FindVisualParent<ComboBox>(source) != null ||
             FindVisualParent<ScrollBar>(source) != null ||
             FindVisualParent<ListBox>(source) != null))
        {
            return;
        }

        var scrollViewer = FindCurrentPageScrollViewer();
        if (scrollViewer == null)
            return;

        if (scrollViewer.ScrollableHeight <= 0 && scrollViewer.ComputedVerticalScrollBarVisibility != Visibility.Visible)
            return;

        double targetOffset = scrollViewer.VerticalOffset - e.Delta;
        targetOffset = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, targetOffset));
        scrollViewer.ScrollToVerticalOffset(targetOffset);
        e.Handled = true;
    }

    private void SyncCurrentPageScrollViewerViewport()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                RootNavigation.UpdateLayout();

                var frame = FindVisualChild<Frame>(RootNavigation);
                var scrollViewer = FindCurrentPageScrollViewer();
                if (frame == null || scrollViewer == null)
                    return;

                double viewportHeight = Math.Max(120, frame.ActualHeight - 8);
                if (!double.IsNaN(viewportHeight) && viewportHeight > 0)
                {
                    scrollViewer.Height = viewportHeight;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error syncing settings page scroll viewport");
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T typed)
                return typed;

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void ApplyResolvedAccentToNavigation()
    {
        AccentResourceHelper.RefreshAccentResources();
        AccentResourceHelper.ApplyResolvedAccentResources(Resources);
        AccentResourceHelper.ApplyResolvedAccentResources(RootNavigation.Resources);

        var frame = FindVisualChild<Frame>(RootNavigation);
        if (frame?.Content is FrameworkElement page)
        {
            AccentResourceHelper.ApplyResolvedAccentResources(page.Resources);
        }
    }

    private void RootNavigation_OnNavigated(object sender, NavigatedEventArgs e)
    {
        ApplyResolvedAccentToNavigation();
    }
}
