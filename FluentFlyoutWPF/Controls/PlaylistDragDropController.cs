using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.ViewModels;

namespace FluentFlyoutWPF.Controls;

internal sealed class PlaylistDragDropController
{
    private const double DragThreshold = 6;
    private readonly ListView _playlistListView;
    private readonly Border _dropIndicator;
    private readonly PlaylistViewModel _viewModel;
    private readonly ObservableCollection<TrackModel> _playlistItems;
    private Point _dragStartPoint;

    public PlaylistDragDropController(
        ListView playlistListView,
        Border dropIndicator,
        PlaylistViewModel viewModel,
        ObservableCollection<TrackModel> playlistItems)
    {
        _playlistListView = playlistListView;
        _dropIndicator = dropIndicator;
        _viewModel = viewModel;
        _playlistItems = playlistItems;
    }

    public void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(_playlistListView);
    }

    public void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var currentPosition = e.GetPosition(_playlistListView);
        var diff = currentPosition - _dragStartPoint;
        if (Math.Abs(diff.X) < DragThreshold && Math.Abs(diff.Y) < DragThreshold)
        {
            return;
        }

        var sourceItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (sourceItem == null)
        {
            return;
        }

        int index = _playlistListView.ItemContainerGenerator.IndexFromContainer(sourceItem);
        if (index < 0)
        {
            return;
        }

        var selectedIndices = new List<int>();
        if (_playlistListView.SelectedItems.Contains(_playlistItems[index]))
        {
            foreach (var item in _playlistListView.SelectedItems.OfType<TrackModel>())
            {
                int idx = _playlistItems.IndexOf(item);
                if (idx >= 0)
                {
                    selectedIndices.Add(idx);
                }
            }
        }
        else
        {
            selectedIndices.Add(index);
        }

        var data = new DataObject("PlaylistTrackIndices", selectedIndices);
        data.SetData("PlaylistTrackIndex", index);
        DragDrop.DoDragDrop(_playlistListView, data, DragDropEffects.Move);
        HideDropIndicator();
    }

    public void OnDragOver(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        if (!e.Data.GetDataPresent("PlaylistTrackIndices") && !e.Data.GetDataPresent("PlaylistTrackIndex"))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        ShowDropIndicator(GetDropTargetIndex(e));
    }

    public void OnDragLeave()
    {
        HideDropIndicator();
    }

    public void OnDrop(DragEventArgs e)
    {
        HideDropIndicator();

        var payload = new DropPayload { TargetIndex = GetDropTargetIndex(e) };
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            payload.Files = (string[])e.Data.GetData(DataFormats.FileDrop);
            payload.Operation = DropOperation.FileImport;
            _viewModel.ProcessDropCommand.Execute(payload);
            return;
        }

        if (e.Data.GetDataPresent("PlaylistTrackIndices"))
        {
            var oldIndices = (List<int>)e.Data.GetData("PlaylistTrackIndices");
            var movedTracks = oldIndices.Select(idx => _playlistItems[idx]).ToList();
            payload.MovedIndices = oldIndices;
            payload.Operation = DropOperation.Reorder;
            _viewModel.ProcessDropCommand.Execute(payload);

            _playlistListView.SelectedItems.Clear();
            foreach (var track in movedTracks)
            {
                _playlistListView.SelectedItems.Add(track);
            }

            e.Handled = true;
            return;
        }

        if (e.Data.GetDataPresent("PlaylistTrackIndex"))
        {
            payload.MovedIndices = new List<int> { (int)e.Data.GetData("PlaylistTrackIndex") };
            payload.Operation = DropOperation.Reorder;
            _viewModel.ProcessDropCommand.Execute(payload);
            e.Handled = true;
        }
    }

    public void SelectTrack(TrackModel? track)
    {
        if (track == null)
        {
            return;
        }

        for (int i = 0; i < _playlistItems.Count; i++)
        {
            if (_playlistItems[i].FilePath == track.FilePath)
            {
                _playlistListView.SelectedIndex = i;
                _playlistListView.ScrollIntoView(_playlistItems[i]);
                break;
            }
        }
    }

    private void ShowDropIndicator(int targetIndex)
    {
        double yPosition = 0;
        if (targetIndex >= 0 && _playlistListView.Items.Count > 0)
        {
            int clampedIndex = Math.Min(targetIndex, _playlistListView.Items.Count - 1);
            if (_playlistListView.ItemContainerGenerator.ContainerFromIndex(clampedIndex) is ListViewItem container)
            {
                var transform = container.TransformToAncestor(_playlistListView);
                var position = transform.Transform(new Point(0, 0));
                yPosition = targetIndex >= _playlistListView.Items.Count
                    ? position.Y + container.ActualHeight
                    : position.Y;
            }
        }

        Canvas.SetTop(_dropIndicator, yPosition);
        _dropIndicator.Width = Math.Max(0, _playlistListView.ActualWidth - 24);
        _dropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator()
    {
        _dropIndicator.Visibility = Visibility.Collapsed;
    }

    private int GetDropTargetIndex(DragEventArgs e)
    {
        var targetItem = FindAncestor<ListViewItem>((DependencyObject)e.OriginalSource);
        if (targetItem != null)
        {
            int targetIndex = _playlistListView.ItemContainerGenerator.IndexFromContainer(targetItem);
            var posInItem = e.GetPosition(targetItem);
            if (posInItem.Y > targetItem.ActualHeight / 2)
            {
                targetIndex++;
            }

            return targetIndex;
        }

        return _playlistListView.Items.Count;
    }

    private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
            {
                return match;
            }

            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            else
            {
                current = LogicalTreeHelper.GetParent(current);
            }
        }

        return null;
    }
}
