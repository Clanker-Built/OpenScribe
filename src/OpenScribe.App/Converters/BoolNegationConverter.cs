using Microsoft.UI.Xaml.Data;
using System;

namespace OpenScribe.App.Converters;

/// <summary>
/// Negates a boolean value. Useful for inverting IsEnabled bindings.
/// </summary>
public sealed class BoolNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
            return !b;
        return value;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is bool b)
            return !b;
        return value;
    }
}
