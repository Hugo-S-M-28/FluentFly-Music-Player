using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FluentFlyoutWPF.ViewModels;
using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Classes.Behaviors;

public static class LyricsInteractionBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(LyricsInteractionBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ScrollOffsetProperty =
        DependencyProperty.RegisterAttached(
            "ScrollOffset",
            typeof(double),
            typeof(LyricsInteractionBehavior),
            new PropertyMetadata(0.0, OnScrollOffsetChanged));

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(LyricsState),
            typeof(LyricsInteractionBehavior),
            new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    public static double GetScrollOffset(DependencyObject obj) => (double)obj.GetValue(ScrollOffsetProperty);
    public static void SetScrollOffset(DependencyObject obj, double value) => obj.SetValue(ScrollOffsetProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox) return;

        listBox.SelectionChanged -= ListBox_SelectionChanged;
        listBox.PreviewMouseLeftButtonDown -= ListBox_PreviewMouseLeftButtonDown;
        listBox.PreviewMouseLeftButtonUp -= ListBox_PreviewMouseLeftButtonUp;
        listBox.PreviewMouseWheel -= ListBox_PreviewMouseWheel;
        listBox.PreviewTouchDown -= ListBox_PreviewTouchDown;
        listBox.DataContextChanged -= ListBox_DataContextChanged;

        if (listBox.GetValue(StateProperty) is LyricsState oldState)
        {
            oldState.Dispose();
            listBox.ClearValue(StateProperty);
        }

        if ((bool)e.NewValue)
        {
            var state = new LyricsState(listBox);
            listBox.SetValue(StateProperty, state);

            listBox.SelectionChanged += ListBox_SelectionChanged;
            listBox.PreviewMouseLeftButtonDown += ListBox_PreviewMouseLeftButtonDown;
            listBox.PreviewMouseLeftButtonUp += ListBox_PreviewMouseLeftButtonUp;
            listBox.PreviewMouseWheel += ListBox_PreviewMouseWheel;
            listBox.PreviewTouchDown += ListBox_PreviewTouchDown;
            listBox.DataContextChanged += ListBox_DataContextChanged;
        }
    }

    private static void OnScrollOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListBox listBox && listBox.GetValue(StateProperty) is LyricsState state)
        {
            state.ScrollViewer?.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    private static void ListBox_DataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.GetValue(StateProperty) is LyricsState state)
        {
            state.UpdateViewModel(e.NewValue as NowPlayingViewModel);
        }
    }

    private static void ListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.GetValue(StateProperty) is LyricsState state)
        {
            state.UpdateLyricsScroll();
        }
    }

    private static void ListBox_PreviewMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox && listBox.GetValue(StateProperty) is LyricsState state)
        {
            state.MouseDownPos = e.GetPosition(listBox);
        }
    }

    private static void ListBox_PreviewMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox listBox && listBox.GetValue(StateProperty) is LyricsState state)
        {
            if (state.ViewModel == null || state.ViewModel.IsUserSeeking) return;

            var currentPos = e.GetPosition(listBox);
            if (Math.Abs(currentPos.X - state.MouseDownPos.X) > 10 || Math.Abs(currentPos.Y - state.MouseDownPos.Y) > 10)
            {
                return;
            }

            var element = e.OriginalSource as FrameworkElement;
            if (element?.DataContext is LyricLine or LyricWord)
            {
                state.ViewModel.SeekFromLyricsTarget(element.DataContext);
                state.ResumeAutoScroll();
            }
        }
    }

    private static void ListBox_PreviewMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (sender is ListBox listBox && listBox.GetValue(StateProperty) is LyricsState state)
        {
            state.PauseAutoScroll();
        }
    }

    private static void ListBox_PreviewTouchDown(object? sender, TouchEventArgs e)
    {
        if (sender is ListBox listBox && listBox.GetValue(StateProperty) is LyricsState state)
        {
            state.PauseAutoScroll();
        }
    }

    private class LyricsState : IDisposable
    {
        private readonly ListBox _listBox;
        private readonly DispatcherTimer _resumeAutoScrollTimer;
        private NowPlayingViewModel? _viewModel;
        private bool _isAutoScrollPaused;
        public ScrollViewer? ScrollViewer { get; private set; }
        public Point MouseDownPos { get; set; }

        public NowPlayingViewModel? ViewModel => _viewModel;

        public LyricsState(ListBox listBox)
        {
            _listBox = listBox;
            _resumeAutoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _resumeAutoScrollTimer.Tick += (s, e) => ResumeAutoScroll();

            UpdateViewModel(listBox.DataContext as NowPlayingViewModel);
        }

        public void UpdateViewModel(NowPlayingViewModel? newViewModel)
        {
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            _viewModel = newViewModel;

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(NowPlayingViewModel.CanResumeLyricsSync))
            {
                if (_viewModel != null && !_viewModel.CanResumeLyricsSync)
                {
                    _listBox.Dispatcher.Invoke(ResumeAutoScroll);
                }
            }
        }

        public void PauseAutoScroll()
        {
            _isAutoScrollPaused = true;
            _resumeAutoScrollTimer.Stop();
            _resumeAutoScrollTimer.Start();
            if (_viewModel != null)
            {
                _viewModel.CanResumeLyricsSync = true;
            }
        }

        public void ResumeAutoScroll()
        {
            _isAutoScrollPaused = false;
            _resumeAutoScrollTimer.Stop();
            if (_viewModel != null)
            {
                _viewModel.CanResumeLyricsSync = false;
            }
            UpdateLyricsScroll();
        }

        public void UpdateLyricsScroll()
        {
            if (_isAutoScrollPaused || _listBox.SelectedItem == null) return;

            if (ScrollViewer == null)
            {
                ScrollViewer = FindVisualChild<ScrollViewer>(_listBox);
            }

            if (ScrollViewer != null)
            {
                _listBox.UpdateLayout();
                if (_listBox.ItemContainerGenerator.ContainerFromItem(_listBox.SelectedItem) is FrameworkElement item)
                {
                    var transform = item.TransformToAncestor(ScrollViewer);
                    var positionInScrollViewer = transform.Transform(new Point(0, 0));

                    double targetOffset = ScrollViewer.VerticalOffset
                                          + positionInScrollViewer.Y
                                          - (ScrollViewer.ViewportHeight / 2)
                                          + (item.ActualHeight / 2);

                    if (targetOffset < 0) targetOffset = 0;
                    if (targetOffset > ScrollViewer.ScrollableHeight) targetOffset = ScrollViewer.ScrollableHeight;

                    if (Math.Abs(ScrollViewer.VerticalOffset - targetOffset) > 1.0)
                    {
                        SetScrollOffset(_listBox, ScrollViewer.VerticalOffset);
                        var anim = new DoubleAnimation
                        {
                            To = targetOffset,
                            Duration = TimeSpan.FromSeconds(0.6),
                            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                        };
                        _listBox.BeginAnimation(ScrollOffsetProperty, anim);
                    }
                }
                else
                {
                    _listBox.ScrollIntoView(_listBox.SelectedItem);
                }
            }
            else
            {
                _listBox.ScrollIntoView(_listBox.SelectedItem);
            }
        }

        private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child is T t) return t;
                T? childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null) return childOfChild;
            }
            return null;
        }

        public void Dispose()
        {
            _resumeAutoScrollTimer.Stop();
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }
        }
    }
}
