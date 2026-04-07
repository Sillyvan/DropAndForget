using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace Avalonia.Collections;

internal sealed class LocalDataGridCollectionView : IDataGridCollectionView, IList, INotifyPropertyChanged
{
    private static readonly AvaloniaList<object> EmptyGroups = [];

    private readonly IEnumerable _sourceCollection;
    private readonly IList? _sourceList;
    private readonly HashSet<INotifyPropertyChanged> _observedItems = [];
    private readonly DataGridSortDescriptionCollection _sortDescriptions = [];
    private List<object> _items = [];
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private Func<object, bool>? _filter;
    private object? _currentItem;
    private int _currentPosition = -1;
    private int _deferLevel;
    private bool _refreshPending;
    private bool _initializedCurrency;

    public LocalDataGridCollectionView(IEnumerable source)
    {
        _sourceCollection = source ?? throw new ArgumentNullException(nameof(source));
        _sourceList = source as IList;
        _sortDescriptions.CollectionChanged += SortDescriptionsChanged;

        if (source is INotifyCollectionChanged notifyingCollection)
        {
            notifyingCollection.CollectionChanged += SourceCollectionChanged;
        }

        Refresh();
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    event NotifyCollectionChangedEventHandler INotifyCollectionChanged.CollectionChanged
    {
        add => CollectionChanged += value;
        remove => CollectionChanged -= value;
    }

    public event EventHandler? CurrentChanged;
    public event EventHandler<DataGridCurrentChangingEventArgs>? CurrentChanging;
    public event PropertyChangedEventHandler? PropertyChanged;
    event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
    }

    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            var next = value ?? CultureInfo.CurrentCulture;
            if (Equals(_culture, next))
            {
                return;
            }

            _culture = next;
            OnPropertyChanged(nameof(Culture));
            RefreshOrDefer();
        }
    }

    public bool Contains(object item) => _items.Contains(item);

    public IEnumerable SourceCollection => _sourceCollection;

    public Func<object, bool>? Filter
    {
        get => _filter;
        set
        {
            if (_filter == value)
            {
                return;
            }

            _filter = value;
            OnPropertyChanged(nameof(Filter));
            RefreshOrDefer();
        }
    }

    public bool CanFilter => true;
    public DataGridSortDescriptionCollection SortDescriptions => _sortDescriptions;
    public bool CanSort => true;
    public bool CanGroup => false;
    public bool IsGrouping => false;
    public int GroupingDepth => 0;
    public string GetGroupingPropertyNameAtDepth(int level) => throw new NotSupportedException();
    public IAvaloniaReadOnlyList<object> Groups => EmptyGroups;
    public bool IsEmpty => _items.Count == 0;
    public object? CurrentItem => _currentItem;
    public int CurrentPosition => _currentPosition;
    public bool IsCurrentAfterLast => _currentPosition >= _items.Count && _items.Count > 0;
    public bool IsCurrentBeforeFirst => _currentPosition < 0;
    public int Count => _items.Count;
    public object? this[int index]
    {
        get => _items[index];
        set => throw new NotSupportedException();
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => throw new NotSupportedException();
    }

    public bool IsFixedSize => false;
    public bool IsReadOnly => _sourceList?.IsReadOnly ?? true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    public IDisposable DeferRefresh()
    {
        _deferLevel++;
        return new DeferredRefresh(this);
    }

    public bool MoveCurrentToFirst() => MoveCurrentToPosition(_items.Count == 0 ? -1 : 0);
    public bool MoveCurrentToLast() => MoveCurrentToPosition(_items.Count == 0 ? -1 : _items.Count - 1);
    public bool MoveCurrentToNext() => MoveCurrentToPosition(_currentPosition + 1);
    public bool MoveCurrentToPrevious() => MoveCurrentToPosition(_currentPosition - 1);

    public bool MoveCurrentTo(object item)
    {
        if (item is null)
        {
            return MoveCurrentToPosition(-1);
        }

        return MoveCurrentToPosition(_items.IndexOf(item));
    }

    public bool MoveCurrentToPosition(int position)
    {
        if (position < -1)
        {
            position = -1;
        }

        if (position >= _items.Count)
        {
            position = _items.Count == 0 ? -1 : _items.Count;
        }

        var nextItem = position >= 0 && position < _items.Count ? _items[position] : null;
        if (ReferenceEquals(_currentItem, nextItem) && _currentPosition == position)
        {
            return nextItem is not null;
        }

        var changing = new DataGridCurrentChangingEventArgs();
        CurrentChanging?.Invoke(this, changing);
        if (changing.Cancel)
        {
            return false;
        }

        SetCurrent(nextItem, position);
        return nextItem is not null;
    }

    public void Refresh()
    {
        var previousCurrent = _currentItem;
        var hadCurrent = _initializedCurrency;
        var items = _sourceCollection.Cast<object>();

        if (_filter is not null)
        {
            items = items.Where(_filter);
        }

        var materialized = items.ToList();
        if (_sortDescriptions.Count > 0)
        {
            IEnumerable<object> ordered = materialized;
            foreach (var sort in _sortDescriptions)
            {
                sort.Initialize(GetItemType(materialized));
                ordered = ordered is IOrderedEnumerable<object> chained ? sort.ThenBy(chained) : sort.OrderBy(ordered);
            }

            materialized = ordered.ToList();
        }

        _items = materialized;
        UpdateObservedItems();

        if (!hadCurrent)
        {
            _initializedCurrency = true;
            SetCurrent(_items.FirstOrDefault(), _items.Count > 0 ? 0 : -1);
        }
        else if (previousCurrent is not null)
        {
            var newIndex = _items.IndexOf(previousCurrent);
            SetCurrent(newIndex >= 0 ? previousCurrent : null, newIndex);
        }
        else
        {
            SetCurrent(null, -1);
        }

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public int Add(object? value)
    {
        if (_sourceList is null || value is null)
        {
            throw new NotSupportedException();
        }

        var index = _sourceList.Add(value);
        if (_sourceCollection is not INotifyCollectionChanged)
        {
            Refresh();
        }

        return index;
    }

    public void Clear()
    {
        if (_sourceList is null)
        {
            throw new NotSupportedException();
        }

        _sourceList.Clear();
        if (_sourceCollection is not INotifyCollectionChanged)
        {
            Refresh();
        }
    }

    public int IndexOf(object? value) => value is null ? -1 : _items.IndexOf(value);
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value)
    {
        if (_sourceList is null || value is null)
        {
            throw new NotSupportedException();
        }

        _sourceList.Remove(value);
        if (_sourceCollection is not INotifyCollectionChanged)
        {
            Refresh();
        }
    }

    public void RemoveAt(int index)
    {
        if (_sourceList is null)
        {
            throw new NotSupportedException();
        }

        _sourceList.RemoveAt(index);
        if (_sourceCollection is not INotifyCollectionChanged)
        {
            Refresh();
        }
    }

    public void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
    public IEnumerator GetEnumerator() => _items.GetEnumerator();

    private void SourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshOrDefer();
    }

    private void SortDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshOrDefer();
        OnPropertyChanged(nameof(SortDescriptions));
    }

    private void RefreshOrDefer()
    {
        if (_deferLevel > 0)
        {
            _refreshPending = true;
            return;
        }

        Refresh();
    }

    private void EndDefer()
    {
        _deferLevel--;
        if (_deferLevel == 0 && _refreshPending)
        {
            _refreshPending = false;
            Refresh();
        }
    }

    private void UpdateObservedItems()
    {
        foreach (var item in _observedItems)
        {
            item.PropertyChanged -= ObservedItemPropertyChanged;
        }

        _observedItems.Clear();
        if (_sortDescriptions.Count == 0)
        {
            return;
        }

        foreach (var item in _items.OfType<INotifyPropertyChanged>())
        {
            if (_observedItems.Add(item))
            {
                item.PropertyChanged += ObservedItemPropertyChanged;
            }
        }
    }

    private void ObservedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || _sortDescriptions.Any(sort => string.Equals(sort.PropertyPath, e.PropertyName, StringComparison.Ordinal)))
        {
            RefreshOrDefer();
        }
    }

    private void SetCurrent(object? item, int position)
    {
        var changed = !ReferenceEquals(_currentItem, item) || _currentPosition != position;
        _currentItem = item;
        _currentPosition = position;
        if (!changed)
        {
            return;
        }

        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(CurrentPosition));
        OnPropertyChanged(nameof(IsCurrentAfterLast));
        OnPropertyChanged(nameof(IsCurrentBeforeFirst));
        CurrentChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Type GetItemType(List<object> items)
    {
        return items.FirstOrDefault()?.GetType() ?? typeof(object);
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class DeferredRefresh(LocalDataGridCollectionView owner) : IDisposable
    {
        private LocalDataGridCollectionView? _owner = owner;

        public void Dispose()
        {
            _owner?.EndDefer();
            _owner = null;
        }
    }
}
