using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Controls;

public partial class PlaylistControl : UserControl
{
    // Drag state
    private Point _dragStartPoint;
    private int _dragSourceIndex = -1;
    private const double DragThreshold = 6; // px of movement before drag starts

    public ObservableCollection<TrackModel> PlaylistItems { get; } = new();

    public PlaylistControl()
    {
        InitializeComponent();
        PlaylistListView.ItemsSource = PlaylistItems;
        
        Loaded += PlaylistControl_Loaded;
        Unloaded += PlaylistControl_Unloaded;
    }

    private void PlaylistControl_Loaded(object sender, RoutedEventArgs e)
    {
        MusicPlayerService.Instance.QueueChanged += MusicPlayerService_QueueChanged;
        MusicPlayerService.Instance.TrackChanged += MusicPlayerService_TrackChanged;
        UpdatePlaylist();
    }

    private void PlaylistControl_Unloaded(object sender, RoutedEventArgs e)
    {
        MusicPlayerService.Instance.QueueChanged -= MusicPlayerService_QueueChanged;
        MusicPlayerService.Instance.TrackChanged -= MusicPlayerService_TrackChanged;
    }

    private void MusicPlayerService_QueueChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(UpdatePlaylist);
    }

    private void MusicPlayerService_TrackChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() => SelectTrack(MusicPlayerService.Instance.CurrentTrack));
    }

    private void UpdatePlaylist()
    {
        var queue = MusicPlayerService.Instance.CurrentQueue;
        PlaylistItems.Clear();
        foreach (var track in queue)
        {
            PlaylistItems.Add(track);
        }
        SelectTrack(MusicPlayerService.Instance.CurrentTrack);
    }

    // ═══════════════════════════════════════════
    // Double-click to play
    // ═══════════════════════════════════════════

    private void PlaylistListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistListView.SelectedItem is TrackModel track)
        {
            var queue = MusicPlayerService.Instance.CurrentQueue;
            int index = -1;
            for (int i = 0; i < queue.Count; i++)
            {
                if (ReferenceEquals(queue[i], track))
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
            {
                MusicPlayerService.Instance.PlayAtIndex(index);
            }
            else
            {
                MusicPlayerService.Instance.Play(track);
            }
        }
    }

    // ═══════════════════════════════════════════
    // Drag & Drop: Initiation
    // ═══════════════════════════════════════════

    private void PlaylistListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(PlaylistListView);
    }

    private void PlaylistListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentPosition = e.GetPosition(PlaylistListView);
        var diff = currentPosition - _dragStartPoint;

        // Only start drag if we've moved beyond the threshold (avoid accidental drags)
        if (Math.Abs(diff.X) < DragThreshold && Math.Abs(diff.Y) < DragThreshold)
            return;

        // Find the ListViewItem under the mouse
        var sourceItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (sourceItem == null)
            return;

        _dragSourceIndex = PlaylistListView.ItemContainerGenerator.IndexFromContainer(sourceItem);
        if (_dragSourceIndex < 0)
            return;

        // Start WPF drag-drop operation
        var data = new DataObject("PlaylistTrackIndex", _dragSourceIndex);
        DragDrop.DoDragDrop(PlaylistListView, data, DragDropEffects.Move);

        // Cleanup after drop completes
        HideDropIndicator();
    }

    // ═══════════════════════════════════════════
    // Drag & Drop: Visual Feedback
    // ═══════════════════════════════════════════

    private void PlaylistListView_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("PlaylistTrackIndex"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        // Show drop indicator at the target position
        var targetIndex = GetDropTargetIndex(e);
        ShowDropIndicator(targetIndex);
    }

    private void PlaylistListView_DragLeave(object sender, DragEventArgs e)
    {
        HideDropIndicator();
    }

    // ═══════════════════════════════════════════
    // Drag & Drop: Execute Move
    // ═══════════════════════════════════════════

    private void PlaylistListView_Drop(object sender, DragEventArgs e)
    {
        HideDropIndicator();

        if (!e.Data.GetDataPresent("PlaylistTrackIndex"))
            return;

        int oldIndex = (int)e.Data.GetData("PlaylistTrackIndex");
        int newIndex = GetDropTargetIndex(e);

        // Clamp to valid range
        var queue = MusicPlayerService.Instance.CurrentQueue;
        if (newIndex > queue.Count - 1)
            newIndex = queue.Count - 1;
        if (newIndex < 0)
            newIndex = 0;

        if (oldIndex == newIndex)
            return;

        // Execute the move in the service (adjusts playback index)
        MusicPlayerService.Instance.MoveTrack(oldIndex, newIndex);

        e.Handled = true;
    }

    // ═══════════════════════════════════════════
    // Drop Indicator Helpers
    // ═══════════════════════════════════════════

    private void ShowDropIndicator(int targetIndex)
    {
        // Calculate the Y position of the target item in the ListView
        double yPosition = 0;

        if (targetIndex >= 0 && PlaylistListView.Items.Count > 0)
        {
            int clampedIndex = Math.Min(targetIndex, PlaylistListView.Items.Count - 1);
            var container = PlaylistListView.ItemContainerGenerator.ContainerFromIndex(clampedIndex) as ListViewItem;

            if (container != null)
            {
                var transform = container.TransformToAncestor(PlaylistListView);
                var position = transform.Transform(new Point(0, 0));

                if (targetIndex >= PlaylistListView.Items.Count)
                {
                    // Drop at the end — place indicator below the last item
                    yPosition = position.Y + container.ActualHeight;
                }
                else
                {
                    // Drop before this item — place indicator at its top
                    yPosition = position.Y;
                }
            }
        }

        Canvas.SetTop(DropIndicator, yPosition);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator()
    {
        DropIndicator.Visibility = Visibility.Collapsed;
    }

    // ═══════════════════════════════════════════
    // Helper: Determine Drop Target Index
    // ═══════════════════════════════════════════

    private int GetDropTargetIndex(DragEventArgs e)
    {
        var targetItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);

        if (targetItem != null)
        {
            int targetIndex = PlaylistListView.ItemContainerGenerator.IndexFromContainer(targetItem);

            // Determine if we're in the top or bottom half of the target item
            var posInItem = e.GetPosition(targetItem);
            if (posInItem.Y > targetItem.ActualHeight / 2)
            {
                targetIndex++; // Drop AFTER this item
            }

            return targetIndex;
        }

        // If not over any item, drop at the end
        return PlaylistListView.Items.Count;
    }

    // ═══════════════════════════════════════════
    // Public API
    // ═══════════════════════════════════════════

    public void SelectTrack(TrackModel? track)
    {
        if (track != null)
        {
            var queue = MusicPlayerService.Instance.CurrentQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                if (queue[i].FilePath == track.FilePath)
                {
                    PlaylistListView.SelectedIndex = i;
                    PlaylistListView.ScrollIntoView(queue[i]);
                    break;
                }
            }
        }
    }

    // ═══════════════════════════════════════════
    // Helper: Walk the Visual Tree
    // ═══════════════════════════════════════════

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
