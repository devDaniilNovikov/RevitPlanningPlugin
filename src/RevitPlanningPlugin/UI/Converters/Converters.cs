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

    /// <summary>
    /// Отображает внутренние enum/string значения человекочитаемыми русскими названиями.
    /// </summary>
    public class DisplayNameConverter : IValueConverter
    {
        public static readonly DisplayNameConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value switch
            {
                GenerationBackend.LmStudio => "AI Tunnel",
                GenerationBackend.ExternalApi => "Внешний API",
                GenerationBackend.Mock => "Демо-режим",

                ApiEnvironment.Development => "Разработка",
                ApiEnvironment.Staging => "Тестовый стенд",
                ApiEnvironment.Production => "Продакшн",

                GenerationType.Residential => "Жилая планировка",
                GenerationType.Office => "Офисная планировка",
                GenerationType.MixedUse => "Смешанное назначение",
                GenerationType.Custom => "Пользовательский сценарий",

                PlanningDetailMode.FloorLayout => "Планировка этажа",
                PlanningDetailMode.ApartmentRooms => "Планировка квартиры",

                ValidationMode.Off => "Без проверки",
                ValidationMode.Advisory => "Предупреждения",
                ValidationMode.Strict => "Строгая проверка",

                MockScenario.HappyPath => "Успешная генерация",
                MockScenario.GenerationError => "Ошибка генерации",
                MockScenario.Hallucination => "Галлюцинация геометрии",

                RoomType.LivingRoom => "Жилое помещение",
                RoomType.Bedroom => "Спальня",
                RoomType.Kitchen => "Кухня",
                RoomType.Bathroom => "Санузел",
                RoomType.Corridor => "Коридор",
                RoomType.Storage => "Кладовая",
                RoomType.Office => "Кабинет",
                RoomType.MeetingRoom => "Переговорная",
                RoomType.OpenSpace => "Открытое пространство",
                RoomType.Lobby => "Холл",
                RoomType.Technical => "Техническое помещение",
                RoomType.Staircase => "Лестничная клетка",
                RoomType.Elevator => "Лифт",
                RoomType.Balcony => "Балкон",
                RoomType.CommonArea => "МОП",
                RoomType.Other => "Другое",

                ValidationSeverity.Info => "Информация",
                ValidationSeverity.Warning => "Предупреждение",
                ValidationSeverity.Error => "Ошибка",

                string s => s.ToLowerInvariant() switch
                {
                    "efficiency" => "Эффективность",
                    "area" => "Площади",
                    "rooms" => "Состав помещений",
                    _ => s
                },
                _ => value?.ToString() ?? string.Empty
            };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Конвертирует (ratio 0–1, parentWidth) → ширина полоски для визуального бара эффективности.
    /// </summary>
    public class RatioToWidthConverter : IMultiValueConverter
    {
        public static readonly RatioToWidthConverter Instance = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2
                && values[0] is double ratio
                && values[1] is double parentWidth)
            {
                return Math.Max(0, Math.Min(parentWidth, parentWidth * ratio));
            }
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
