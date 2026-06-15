using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FluentFlyoutWPF.Classes;

public class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _isNotificationSuspended;

    public BulkObservableCollection() : base() { }
    public BulkObservableCollection(IEnumerable<T> collection) : base(collection) { }

    public void AddRange(IEnumerable<T> collection)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));

        _isNotificationSuspended = true;
        try
        {
            foreach (var item in collection)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _isNotificationSuspended = false;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    public void ReplaceAll(IEnumerable<T> collection)
    {
        _isNotificationSuspended = true;
        try
        {
            Items.Clear();
            if (collection != null)
            {
                foreach (var item in collection)
                {
                    Items.Add(item);
                }
            }
        }
        finally
        {
            _isNotificationSuspended = false;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_isNotificationSuspended)
        {
            base.OnCollectionChanged(e);
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!_isNotificationSuspended)
        {
            base.OnPropertyChanged(e);
        }
    }
}
