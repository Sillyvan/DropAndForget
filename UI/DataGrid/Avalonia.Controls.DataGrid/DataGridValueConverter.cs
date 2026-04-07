// (c) Copyright Microsoft Corporation.
// This source is subject to the Microsoft Public License (Ms-PL).
// Please see http://go.microsoft.com/fwlink/?LinkID=131993 for details.
// All other rights reserved.

using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Avalonia.Controls
{
    internal class DataGridValueConverter : IValueConverter
    {
        public static DataGridValueConverter Instance = new DataGridValueConverter();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is null)
            {
                return null;
            }

            if (targetType == typeof(string))
            {
                return value.ToString();
            }

            return value;
        }


        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (targetType == typeof(string))
            {
                return value?.ToString();
            }

            return value;
        }
    }
}
