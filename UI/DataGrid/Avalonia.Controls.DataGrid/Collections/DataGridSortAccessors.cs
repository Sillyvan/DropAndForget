using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using DropAndForget.ViewModels;

namespace Avalonia.Collections;

internal readonly record struct RegisteredSortAccessor(Func<object, object?> GetValue, IComparer<object> Comparer);

internal static class DataGridSortAccessorRegistry
{
    private static readonly Dictionary<Type, Dictionary<string, Func<CultureInfo?, RegisteredSortAccessor>>> Accessors = new()
    {
        [typeof(BucketListEntry)] = new(StringComparer.Ordinal)
        {
            [nameof(BucketListEntry.DisplayName)] = culture => CreateStringAccessor(culture, static item => item.DisplayName),
            [nameof(BucketListEntry.FolderPath)] = culture => CreateStringAccessor(culture, static item => item.FolderPath),
            [nameof(BucketListEntry.SizeBytes)] = _ => CreateNullableLongAccessor(static item => item.SizeBytes),
            [nameof(BucketListEntry.ModifiedAt)] = _ => CreateNullableDateTimeAccessor(static item => item.ModifiedAt)
        }
    };

    public static bool TryCreate(Type itemType, string propertyPath, CultureInfo? culture, out RegisteredSortAccessor accessor)
    {
        if (Accessors.TryGetValue(itemType, out var typeAccessors)
            && typeAccessors.TryGetValue(propertyPath, out var factory))
        {
            accessor = factory(culture);
            return true;
        }

        Debug.WriteLine($"DataGrid sort path '{propertyPath}' is not registered for '{itemType.FullName}'.");
        accessor = new RegisteredSortAccessor(static _ => null, Comparer<object>.Create(static (_, _) => 0));
        return false;
    }

    private static RegisteredSortAccessor CreateStringAccessor(CultureInfo? culture, Func<BucketListEntry, string?> getter)
    {
        var compareInfo = (culture ?? CultureInfo.CurrentCulture).CompareInfo;
        return new RegisteredSortAccessor(
            item => getter((BucketListEntry)item),
            Comparer<object>.Create((left, right) => CompareNullable(left as string, right as string, compareInfo.Compare)));
    }

    private static RegisteredSortAccessor CreateNullableLongAccessor(Func<BucketListEntry, long?> getter)
    {
        return new RegisteredSortAccessor(
            item => getter((BucketListEntry)item),
            Comparer<object>.Create((left, right) => CompareNullable((long?)left, (long?)right, static (x, y) => x.CompareTo(y))));
    }

    private static RegisteredSortAccessor CreateNullableDateTimeAccessor(Func<BucketListEntry, DateTime?> getter)
    {
        return new RegisteredSortAccessor(
            item => getter((BucketListEntry)item),
            Comparer<object>.Create((left, right) => CompareNullable((DateTime?)left, (DateTime?)right, static (x, y) => x.CompareTo(y))));
    }

    private static int CompareNullable<T>(T? left, T? right, Func<T, T, int> compare)
        where T : struct
    {
        if (!left.HasValue)
        {
            return right.HasValue ? -1 : 0;
        }

        if (!right.HasValue)
        {
            return 1;
        }

        return compare(left.Value, right.Value);
    }

    private static int CompareNullable(string? left, string? right, Func<string, string, int> compare)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        if (right is null)
        {
            return 1;
        }

        return compare(left, right);
    }
}
