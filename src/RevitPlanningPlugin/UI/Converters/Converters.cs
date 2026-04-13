using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.UI.Converters
{
    /// <summary>
    /// GenerationStatus → цвет фона статус-бара.
    /// </summary>
    public class StatusToColorConverter : IValueConverter
    {
        public static readonly StatusToColorConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is GenerationStatus status)
            {
                return status switch
                {
                    GenerationStatus.Idle => new SolidColorBrush(Color.FromRgb(0x75, 0x75, 0x75)),
                    GenerationStatus.Loading => new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                    GenerationStatus.Validating => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
                    GenerationStatus.Generating => new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)),
                    GenerationStatus.Completed => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
                    GenerationStatus.Error => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// null → Collapsed, not null → Visible.
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public static readonly NullToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// bool → Visibility.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public static readonly BoolToVisibilityConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is Visibility.Visible;
    }
}
