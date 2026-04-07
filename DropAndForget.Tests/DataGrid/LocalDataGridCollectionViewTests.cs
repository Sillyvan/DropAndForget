using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Collections;
using DropAndForget.Models;
using DropAndForget.ViewModels;
using FluentAssertions;

namespace DropAndForget.Tests.DataGrid;

public sealed class LocalDataGridCollectionViewTests
{
    [Fact]
    public void SortByDisplayName_ShouldOrderAscending()
    {
        var alpha = CreateEntry("b.txt", "Beta");
        var beta = CreateEntry("a.txt", "Alpha");
        var view = CreateView(alpha, beta);

        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(BucketListEntry.DisplayName)));

        view.Cast<BucketListEntry>().Select(item => item.DisplayName).Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public void UnknownSortPath_ShouldBeDeterministicNoOp()
    {
        var first = CreateEntry("b.txt", "Beta");
        var second = CreateEntry("a.txt", "Alpha");
        var view = CreateView(first, second);

        view.SortDescriptions.Add(DataGridSortDescription.FromPath("UnknownProperty"));

        view.Cast<BucketListEntry>().Should().ContainInOrder(first, second);
    }

    [Fact]
    public void AddItem_WhileSorted_ShouldInsertInSortedOrder()
    {
        var items = new ObservableCollection<BucketListEntry>
        {
            CreateEntry("b.txt", "Beta"),
            CreateEntry("d.txt", "Delta")
        };
        var view = new LocalDataGridCollectionView(items);
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(BucketListEntry.DisplayName)));

        items.Add(CreateEntry("c.txt", "Charlie"));

        view.Cast<BucketListEntry>().Select(item => item.DisplayName).Should().Equal("Beta", "Charlie", "Delta");
    }

    [Fact]
    public void SortKeyChange_ShouldRefreshOrder()
    {
        var alpha = CreateEntry("a.txt", "Alpha");
        var beta = CreateEntry("b.txt", "Beta");
        var view = CreateView(alpha, beta);
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(BucketListEntry.DisplayName)));

        beta.DisplayName = "Aardvark";

        view.Cast<BucketListEntry>().Select(item => item.DisplayName).Should().Equal("Aardvark", "Alpha");
    }

    [Fact]
    public void CurrentItem_ShouldSurviveSort_WhenReferenceStillExists()
    {
        var alpha = CreateEntry("a.txt", "Alpha");
        var beta = CreateEntry("b.txt", "Beta");
        var view = CreateView(alpha, beta);
        view.MoveCurrentTo(beta).Should().BeTrue();

        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(BucketListEntry.DisplayName)));

        view.CurrentItem.Should().BeSameAs(beta);
    }

    private static LocalDataGridCollectionView CreateView(params BucketListEntry[] items)
    {
        return new LocalDataGridCollectionView(new ObservableCollection<BucketListEntry>(items));
    }

    private static BucketListEntry CreateEntry(string key, string displayName)
    {
        return new BucketListEntry(new BucketItem
        {
            Key = key,
            DisplayName = displayName,
            SizeBytes = key.Length,
            SizeText = key.Length.ToString(),
            ModifiedAt = new DateTime(2025, 1, 1),
            ModifiedText = "2025-01-01"
        });
    }
}
