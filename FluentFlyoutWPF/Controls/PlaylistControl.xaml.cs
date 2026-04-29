using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Models;

namespace FluentFlyoutWPF.Controls;

public partial class PlaylistControl : UserControl
{
    // Drag state
    private Point _dragStartPoint;
    private int _dragSourceIndex = -1;
    private const double DragThreshold = 6; // px of movement before drag starts

    public ObservableCollection<TrackModel> PlaylistItems { get; } = [];

    public PlaylistControl()
    {
        InitializeComponent();
        PlaylistListView.ItemsSource = PlaylistItems;
        
        // Setup Search filtering
        var view = CollectionViewSource.GetDefaultView(PlaylistItems);
        view.Filter = FilterPlaylist;

        Loaded += PlaylistControl_Loaded;
        Unloaded += PlaylistControl_Unloaded;
    }

    private bool FilterPlaylist(object item)
    {
        if (item is TrackModel track)
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text)) return true;
            string query = SearchBox.Text;
            return (track.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                   (track.Artist?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
        }
        return false;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        CollectionViewSource.GetDefaultView(PlaylistItems).Refresh();
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        var current = MusicPlayerService.Instance.CurrentTrack;
        if (current != null)
            MusicPlayerService.Instance.PlaySingle(current);
        else
            MusicPlayerService.Instance.PlayQueue([]);
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

    // ═══════════════════════════════════════════
    // Incremental Diffing Update
    // ═══════════════════════════════════════════

    /// <summary>
    /// Incrementally synchronizes PlaylistItems with the service queue.
    /// Uses reference equality to detect insertions, removals, and moves
    /// without clearing the collection (preserving scroll position and 
    /// avoiding flicker).
    /// </summary>
    private void UpdatePlaylist()
    {
        var source = MusicPlayerService.Instance.CurrentQueue;

        // Fast path: if the source is identical (same references, same order), skip entirely
        if (PlaylistItems.Count == source.Count)
        {
            bool identical = true;
            for (int i = 0; i < source.Count; i++)
            {
                if (!ReferenceEquals(PlaylistItems[i], source[i]))
                {
                    identical = false;
                    break;
                }
            }
            if (identical)
            {
                SelectTrack(MusicPlayerService.Instance.CurrentTrack);
                return;
            }
        }

        // Build a set of references in the new source for O(1) lookup
        var sourceSet = new HashSet<TrackModel>(source, ReferenceEqualityComparer.Instance);

        // Phase 1: Remove items from PlaylistItems that are no longer in source
        for (int i = PlaylistItems.Count - 1; i >= 0; i--)
        {
            if (!sourceSet.Contains(PlaylistItems[i]))
            {
                PlaylistItems.RemoveAt(i);
            }
        }

        // Phase 2: Insert / reorder to match source order
        for (int i = 0; i < source.Count; i++)
        {
            var expected = source[i];

            if (i < PlaylistItems.Count)
            {
                if (ReferenceEquals(PlaylistItems[i], expected))
                {
                    // Already in the correct position
                    continue;
                }

                // Check if it exists elsewhere in our list
                int existingIndex = -1;
                for (int j = i + 1; j < PlaylistItems.Count; j++)
                {
                    if (ReferenceEquals(PlaylistItems[j], expected))
                    {
                        existingIndex = j;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    // Move it to the correct position
                    PlaylistItems.Move(existingIndex, i);
                }
                else
                {
                    // New item — insert at correct position
                    PlaylistItems.Insert(i, expected);
                }
            }
            else
            {
                // Past end of current list — append
                PlaylistItems.Add(expected);
            }
        }

        // Phase 3: Trim any excess items that shouldn't be there
        while (PlaylistItems.Count > source.Count)
        {
            PlaylistItems.RemoveAt(PlaylistItems.Count - 1);
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
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

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

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            int targetIndex = GetDropTargetIndex(e);
            
            foreach (var path in files)
            {
                var track = LibraryManager.Instance.Tracks.FirstOrDefault(t => t.FilePath == path);
                if (track != null)
                {
                    MusicPlayerService.Instance.AddToQueue(track);
                    int addedIndex = MusicPlayerService.Instance.CurrentQueue.Count - 1;
                    if (addedIndex != targetIndex && addedIndex > 0)
                    {
                        MusicPlayerService.Instance.MoveTrack(addedIndex, targetIndex);
                        targetIndex++;
                    }
                }
            }
            return;
        }

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
            if (PlaylistListView.ItemContainerGenerator.ContainerFromIndex(clampedIndex) is ListViewItem container)
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

    // ═══════════════════════════════════════════
    // Context Menu Handlers
    // ═══════════════════════════════════════════

    /// <summary>
    /// Gets the list of selected tracks. Works for both single and multi-select.
    /// Falls back to the DataContext of the clicked MenuItem if nothing is selected.
    /// </summary>
    private List<TrackModel> GetContextTracks(object sender)
    {
        // If multiple items are selected, use the selection
        if (PlaylistListView.SelectedItems.Count > 1)
        {
            return [.. PlaylistListView.SelectedItems.Cast<TrackModel>()];
        }

        // Otherwise use the DataContext of the right-clicked item
        if (sender is MenuItem mi && mi.DataContext is TrackModel track)
        {
            return [track];
        }

        return [];
    }

    private void PlayNext_Click(object sender, RoutedEventArgs e)
    {
        var tracks = GetContextTracks(sender);
        int currentIndex = MusicPlayerService.Instance.CurrentQueueIndex;
        if (currentIndex < 0) return;

        int insertAt = currentIndex + 1;
        foreach (var track in tracks)
        {
            int trackIndex = PlaylistItems.IndexOf(track);
            if (trackIndex >= 0 && trackIndex != insertAt)
            {
                MusicPlayerService.Instance.MoveTrack(trackIndex, insertAt);
                insertAt++;
            }
        }
    }

    private void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
    {
        var tracks = GetContextTracks(sender);
        foreach (var track in tracks)
        {
            MusicPlayerService.Instance.RemoveFromQueue(track);
        }
    }

    private void ViewInfo_Click(object sender, RoutedEventArgs e)
    {
        // This requires an EditWindow
        if (sender is MenuItem { DataContext: TrackModel _ })
        {
            // Similar to LibraryPage.xaml.cs OnEditTrackCommand
        }
    }

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.DataContext is TrackModel track)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{track.FilePath}\"");
            }
            catch { }
        }
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

/// <summary>
/// Reference equality comparer for TrackModel. Used by the incremental
/// diffing algorithm to distinguish between the same logical track
/// appearing multiple times in a queue (e.g. repeat scenarios).
/// </summary>
internal sealed class ReferenceEqualityComparer : IEqualityComparer<TrackModel>
{
    public static readonly ReferenceEqualityComparer Instance = new();
    private ReferenceEqualityComparer() { }

    public bool Equals(TrackModel? x, TrackModel? y) => ReferenceEquals(x, y);
    public int GetHashCode(TrackModel obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
