using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MapEditor.Core.Validate;

namespace MapEditor.App;

/// <summary>
/// A favourite star from a prefab name and the set of favourites: the glyph, or with the parameter "brush" its colour
/// (the theme's caution colour when starred).
/// </summary>
public sealed class FavouriteStarConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        bool on = values is [string name, IReadOnlySet<string> set, ..] && set.Contains(name);
        if (parameter as string != "brush") return on ? "★" : "☆";
        return Application.Current.TryFindResource(on ? "SystemFillColorCautionBrush" : "TextFillColorSecondaryBrush") ?? Brushes.Gray;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

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
