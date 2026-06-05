using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentFlyoutWPF.ViewModels;

namespace FluentFlyoutWPF.Classes.Behaviors;

public static class SeekSliderBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SeekSliderBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.RegisterAttached(
            "ViewModel",
            typeof(NowPlayingViewModel),
            typeof(SeekSliderBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static NowPlayingViewModel? GetViewModel(DependencyObject obj) => (NowPlayingViewModel?)obj.GetValue(ViewModelProperty);
    public static void SetViewModel(DependencyObject obj, NowPlayingViewModel? value) => obj.SetValue(ViewModelProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider) return;

        slider.PreviewMouseLeftButtonDown -= Slider_PreviewMouseLeftButtonDown;
        slider.PreviewMouseLeftButtonUp -= Slider_PreviewMouseLeftButtonUp;
        slider.ValueChanged -= Slider_ValueChanged;

        if ((bool)e.NewValue)
        {
            slider.PreviewMouseLeftButtonDown += Slider_PreviewMouseLeftButtonDown;
            slider.PreviewMouseLeftButtonUp += Slider_PreviewMouseLeftButtonUp;
            slider.ValueChanged += Slider_ValueChanged;
        }
    }

    private static void Slider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider && GetViewModel(slider) is { } viewModel)
        {
            viewModel.BeginSeekInteraction();
        }
    }

    private static void Slider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Slider slider && GetViewModel(slider) is { } viewModel)
        {
            viewModel.EndSeekInteraction();
            viewModel.CommitSeek();
        }
    }

    private static void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && GetViewModel(slider) is { } viewModel)
        {
            if (viewModel.IsUserSeeking)
            {
                viewModel.UpdateSeekPreview(viewModel.SeekValue);
            }
        }
    }
}
