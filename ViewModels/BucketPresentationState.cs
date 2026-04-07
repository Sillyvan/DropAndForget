using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DropAndForget.Models;

namespace DropAndForget.ViewModels;

internal sealed class BucketPresentationState
{
    private readonly List<BucketListEntry> _allItems = [];
    private readonly List<BucketListEntry> _searchItems = [];
    private int _searchRequestId;

    internal BucketPresentationState()
    {
        BucketItems = [];
        Breadcrumbs = [];
    }

    internal ObservableCollection<BucketListEntry> BucketItems { get; }

    internal ObservableCollection<BreadcrumbItem> Breadcrumbs { get; }

    internal int VisibleFolderCount => BucketItems.Count(item => item.IsFolder);

    internal int VisibleFileCount => BucketItems.Count(item => item.IsFile);

    internal string VisibleTotalSizeText => BucketUiHelpers.FormatSize(BucketItems.Where(item => item.IsFile).Sum(item => item.SizeBytes ?? 0));

    internal int NextSearchRequestId()
    {
        return ++_searchRequestId;
    }

    internal bool IsSearchRequestCurrent(int requestId)
    {
        return requestId == _searchRequestId;
    }

    internal BucketListEntry? ReplaceItems(IEnumerable<BucketListEntry> items, string currentPrefix, string searchText, BucketListEntry? selectedItem)
    {
        _allItems.Clear();
        _allItems.AddRange(items);
        ReplaceBreadcrumbs(BucketUiHelpers.BuildBreadcrumbs(currentPrefix));
        return ApplyFilter(searchText, selectedItem);
    }

    internal BucketListEntry? ReplaceSearchResults(IEnumerable<BucketListEntry> items, string searchText, BucketListEntry? selectedItem)
    {
        _searchItems.Clear();
        _searchItems.AddRange(items);
        return ApplyFilter(searchText, selectedItem);
    }

    internal BucketListEntry? ClearSearchResults(string searchText, BucketListEntry? selectedItem)
    {
        _searchItems.Clear();
        return ApplyFilter(searchText, selectedItem);
    }

    internal BucketListEntry? AddPlaceholder(BucketListEntry item, string searchText, BucketListEntry? selectedItem)
    {
        _allItems.Insert(0, item);
        ApplyFilter(searchText, selectedItem);
        return item;
    }

    internal BucketListEntry? CancelEdit(BucketListEntry editingItem, string searchText, BucketListEntry? selectedItem)
    {
        if (editingItem.IsNewPlaceholder)
        {
            _allItems.Remove(editingItem);
            ApplyFilter(searchText, selectedItem);
            return ReferenceEquals(selectedItem, editingItem) ? null : selectedItem;
        }

        editingItem.EditName = editingItem.DisplayName;
        editingItem.IsEditing = false;
        return ApplyFilter(searchText, selectedItem);
    }

    internal BucketListEntry? RefreshFilter(string searchText, BucketListEntry? selectedItem)
    {
        return ApplyFilter(searchText, selectedItem);
    }

    internal BucketListEntry? FindEditingItem(BucketListEntry? item)
    {
        return item ?? _allItems.FirstOrDefault(entry => entry.IsEditing);
    }

    private BucketListEntry? ApplyFilter(string searchText, BucketListEntry? selectedItem)
    {
        var source = string.IsNullOrWhiteSpace(searchText) ? _allItems : _searchItems;
        var filtered = source.Where(item => MatchesSearch(item, searchText)).ToList();

        BucketItems.Clear();
        foreach (var item in filtered)
        {
            BucketItems.Add(item);
        }

        if (selectedItem is not null && !BucketItems.Contains(selectedItem))
        {
            return null;
        }

        return selectedItem;
    }

    private static bool MatchesSearch(BucketListEntry item, string searchText)
    {
        if (item.IsEditing || string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var term = searchText.Trim();
        return item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
            || item.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
            || item.FolderPath.Contains(term, StringComparison.OrdinalIgnoreCase)
            || item.KindText.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void ReplaceBreadcrumbs(IEnumerable<BreadcrumbItem> items)
    {
        Breadcrumbs.Clear();
        foreach (var item in items)
        {
            Breadcrumbs.Add(item);
        }
    }
}
