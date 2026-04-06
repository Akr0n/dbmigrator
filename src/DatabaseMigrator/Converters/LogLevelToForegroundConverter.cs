using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DatabaseMigrator.Core.Models;

namespace DatabaseMigrator.Converters;

public sealed class LogLevelToForegroundConverter : IValueConverter
{
    public static readonly LogLevelToForegroundConverter Instance = new();

    private static readonly IBrush InfoBrush    = new SolidColorBrush(Color.Parse("#e0e0e0"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#ff9800"));
    private static readonly IBrush ErrorBrush   = new SolidColorBrush(Color.Parse("#ef5350"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is LogLevel level ? level switch
        {
            LogLevel.Warning => WarningBrush,
            LogLevel.Error   => ErrorBrush,
            _                => InfoBrush
        } : InfoBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
