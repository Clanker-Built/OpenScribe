using Microsoft.UI.Xaml.Data;
using System;

namespace OpenScribe.App.Converters;

/// <summary>
/// Returns 0.4 opacity for excluded (true) steps, 1.0 for included steps.
/// </summary>
public sealed class ExcludedOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool excluded && excluded)
            return 0.4;
        return 1.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
