using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace WorldBuilder.Converters;

public static class ObjectConverters {
    public static IsNotNullConverter IsNotNull { get; } = new();
    public static IsNullConverter IsNull { get; } = new();

    public class IsNotNullConverter : IValueConverter {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value != null;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    public class IsNullConverter : IValueConverter {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value == null;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
