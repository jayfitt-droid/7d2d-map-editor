using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MapEditor.Core.Validate;

namespace MapEditor.App;

/// <summary>A one-character mark for an issue's severity.</summary>
public sealed class SeverityGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        IssueSeverity.Error => "✖",
        IssueSeverity.Warning => "⚠",
        _ => "ℹ",
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>The theme's critical, caution or neutral colour for an issue's severity.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value switch
        {
            IssueSeverity.Error => "SystemFillColorCriticalBrush",
            IssueSeverity.Warning => "SystemFillColorCautionBrush",
            _ => "TextFillColorSecondaryBrush",
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
