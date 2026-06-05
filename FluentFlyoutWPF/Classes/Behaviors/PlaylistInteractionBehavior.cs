using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentFlyoutWPF.Classes.Services;
using FluentFlyoutWPF.Controls;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.ViewModels;

namespace FluentFlyoutWPF.Classes.Behaviors;

public static class PlaylistInteractionBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(PlaylistInteractionBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.RegisterAttached(
            "ViewModel",
            typeof(PlaylistViewModel),
            typeof(PlaylistInteractionBehavior),
            new PropertyMetadata(null));

    public static readonly DependencyProperty DropIndicatorProperty =
        DependencyProperty.RegisterAttached(
            "DropIndicator",
            typeof(Border),
            typeof(PlaylistInteractionBehavior),
            new PropertyMetadata(null));

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(PlaylistDragDropController),
            typeof(PlaylistInteractionBehavior));

    private static readonly DependencyProperty PlaybackHandlerProperty =
        DependencyProperty.RegisterAttached(
            "PlaybackHandler",
            typeof(PlaybackSubscriptions),
            typeof(PlaylistInteractionBehavior));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);
    public static PlaylistViewModel? GetViewModel(DependencyObject obj) => (PlaylistViewModel?)obj.GetValue(ViewModelProperty);
    public static void SetViewModel(DependencyObject obj, PlaylistViewModel? value) => obj.SetValue(ViewModelProperty, value);
    public static Border? GetDropIndicator(DependencyObject obj) => (Border?)obj.GetValue(DropIndicatorProperty);
    public static void SetDropIndicator(DependencyObject obj, Border? value) => obj.SetValue(DropIndicatorProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView listView)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            Attach(listView);
        }
        else
        {
            Detach(listView);
        }
    }

    private static void Attach(ListView listView)
    {
        listView.Loaded -= OnLoaded;
        listView.Unloaded -= OnUnloaded;
        listView.MouseDoubleClick -= OnMouseDoubleClick;
        listView.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        listView.PreviewMouseMove -= OnPreviewMouseMove;
        listView.DragOver -= OnDragOver;
        listView.DragLeave -= OnDragLeave;
        listView.Drop -= OnDrop;
        listView.PreviewKeyDown -= OnPreviewKeyDown;

        listView.Loaded += OnLoaded;
        listView.Unloaded += OnUnloaded;
        listView.MouseDoubleClick += OnMouseDoubleClick;
        listView.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
        listView.PreviewMouseMove += OnPreviewMouseMove;
        listView.DragOver += OnDragOver;
        listView.DragLeave += OnDragLeave;
        listView.Drop += OnDrop;
        listView.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void Detach(ListView listView)
    {
        listView.Loaded -= OnLoaded;
        listView.Unloaded -= OnUnloaded;
        listView.MouseDoubleClick -= OnMouseDoubleClick;
        listView.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        listView.PreviewMouseMove -= OnPreviewMouseMove;
        listView.DragOver -= OnDragOver;
        listView.DragLeave -= OnDragLeave;
        listView.Drop -= OnDrop;
        listView.PreviewKeyDown -= OnPreviewKeyDown;

        if (listView.GetValue(PlaybackHandlerProperty) is PlaybackSubscriptions subscriptions)
        {
            subscriptions.Dispose();
            listView.ClearValue(PlaybackHandlerProperty);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ListView listView)
        {
            return;
        }

        if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(listView))
        {
            return;
        }

        var viewModel = GetViewModel(listView);
        var indicator = GetDropIndicator(listView);
        if (viewModel == null || indicator == null)
        {
            return;
        }

        var controller = new PlaylistDragDropController(listView, indicator, viewModel, viewModel.PlaylistItems);
        listView.SetValue(ControllerProperty, controller);

        var playbackService = App.GetRequiredService<IPlaybackService>();
        var subscriptions = new PlaybackSubscriptions(listView, viewModel, controller, playbackService);
        listView.SetValue(PlaybackHandlerProperty, subscriptions);
        subscriptions.SyncFromPlayer();
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListView listView && listView.GetValue(PlaybackHandlerProperty) is PlaybackSubscriptions subscriptions)
        {
            subscriptions.Dispose();
            listView.ClearValue(PlaybackHandlerProperty);
            listView.ClearValue(ControllerProperty);
        }
    }

    private static void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView listView &&
            GetViewModel(listView) is { } viewModel &&
            listView.SelectedItem is TrackModel track &&
            viewModel.PlayTrackCommand.CanExecute(track))
        {
            viewModel.PlayTrackCommand.Execute(track);
        }
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListView listView && listView.GetValue(ControllerProperty) is PlaylistDragDropController controller)
        {
            controller.OnPreviewMouseLeftButtonDown(e);
        }
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is ListView listView && listView.GetValue(ControllerProperty) is PlaylistDragDropController controller)
        {
            controller.OnPreviewMouseMove(e);
        }
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is ListView listView && listView.GetValue(ControllerProperty) is PlaylistDragDropController controller)
        {
            controller.OnDragOver(e);
        }
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is ListView listView && listView.GetValue(ControllerProperty) is PlaylistDragDropController controller)
        {
            controller.OnDragLeave();
        }
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is ListView listView && listView.GetValue(ControllerProperty) is PlaylistDragDropController controller)
        {
            controller.OnDrop(e);
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ListView listView || GetViewModel(listView) is not { } viewModel)
        {
            return;
        }

        if (e.Key == Key.Delete)
        {
            foreach (var track in listView.SelectedItems.OfType<TrackModel>().ToList())
            {
                if (viewModel.RemoveFromQueueCommand.CanExecute(track))
                {
                    viewModel.RemoveFromQueueCommand.Execute(track);
                }
            }

            e.Handled = listView.SelectedItems.Count > 0;
            return;
        }

        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && viewModel.UndoCommand.CanExecute(null))
        {
            viewModel.UndoCommand.Execute(null);
            e.Handled = true;
        }
    }

    private sealed class PlaybackSubscriptions : IDisposable
    {
        private readonly ListView _listView;
        private readonly PlaylistViewModel _viewModel;
        private readonly PlaylistDragDropController _controller;
        private readonly IPlaybackService _playbackService;

        public PlaybackSubscriptions(ListView listView, PlaylistViewModel viewModel, PlaylistDragDropController controller, IPlaybackService playbackService)
        {
            _listView = listView;
            _viewModel = viewModel;
            _controller = controller;
            _playbackService = playbackService;

            _playbackService.QueueChanged += PlaybackServiceOnQueueChanged;
            _playbackService.TrackChanged += PlaybackServiceOnTrackChanged;
            _playbackService.PropertyChanged += PlaybackServiceOnPropertyChanged;
        }

        public void Dispose()
        {
            _playbackService.QueueChanged -= PlaybackServiceOnQueueChanged;
            _playbackService.TrackChanged -= PlaybackServiceOnTrackChanged;
            _playbackService.PropertyChanged -= PlaybackServiceOnPropertyChanged;
        }

        public void SyncFromPlayer()
        {
            _viewModel.SyncQueue(_playbackService.CurrentQueue);
            _viewModel.UpdateUndoState();
            _controller.SelectTrack(_playbackService.CurrentTrack);
        }

        private void PlaybackServiceOnQueueChanged(object? sender, EventArgs e)
        {
            _listView.Dispatcher.Invoke(SyncFromPlayer);
        }

        private void PlaybackServiceOnTrackChanged(object? sender, EventArgs e)
        {
            _listView.Dispatcher.Invoke(() => _controller.SelectTrack(_playbackService.CurrentTrack));
        }

        private void PlaybackServiceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IPlaybackService.CanUndo))
            {
                _listView.Dispatcher.Invoke(_viewModel.UpdateUndoState);
            }
        }
    }
}
