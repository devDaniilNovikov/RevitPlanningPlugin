using System.Collections.Generic;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Параметры запуска AI-генерации планировок.
    /// Передаются во внешний AI-сервис вместе с геометрией контура.
    /// </summary>
    public class GenerationParameters
    {
        /// <summary>Количество вариантов (1–20).</summary>
        public int VariantCount { get; set; } = 3;

        /// <summary>Тип генерации: жилье, офис, смешанное использование или пользовательский сценарий.</summary>
        public GenerationType GenerationType { get; set; } = GenerationType.Residential;

        /// <summary>Режим проверки результата генерации.</summary>
        public ValidationMode ValidationMode { get; set; } = ValidationMode.Advisory;

        /// <summary>Текстовое пожелание пользователя к AI-генерации.</summary>
        public string TextPrompt { get; set; } = string.Empty;

        // ——— Параметры квартир ———

        /// <summary>Требуемое количество студий.</summary>
        public int StudioCount { get; set; } = 0;

        /// <summary>Требуемое количество 1-комнатных квартир.</summary>
        public int OneRoomCount { get; set; } = 2;

        /// <summary>Требуемое количество 2-комнатных квартир.</summary>
        public int TwoRoomCount { get; set; } = 4;

        /// <summary>Требуемое количество 3-комнатных квартир.</summary>
        public int ThreeRoomCount { get; set; } = 2;

        /// <summary>Требуемое количество 4-комнатных квартир.</summary>
        public int FourRoomCount { get; set; } = 0;

        /// <summary>Минимальная площадь квартиры, м².</summary>
        public double MinApartmentArea { get; set; } = 25.0;

        /// <summary>Максимальная площадь квартиры, м².</summary>
        public double MaxApartmentArea { get; set; } = 120.0;

        // ——— Параметры МОП ———

        /// <summary>
        /// Целевая площадь МОПов (мест общего пользования), м².
        /// 0 = определить автоматически.
        /// </summary>
        public double MopAreaTarget { get; set; } = 0;

        /// <summary>Минимальная ширина коридора МОП, м.</summary>
        public double MinCorridorWidth { get; set; } = 1.4;

        // ——— Общие параметры ———

        /// <summary>Требуемые типы помещений (расширенный список для AI).</summary>
        public List<RoomType> RequiredRoomTypes { get; set; } = new();

        /// <summary>Минимальная площадь помещения (для не-квартирных зон), м².</summary>
        public double MinRoomArea { get; set; } = 8.0;

        /// <summary>Максимальная площадь помещения, м².</summary>
        public double MaxRoomArea { get; set; } = 80.0;

        /// <summary>Приоритет оптимизации: «efficiency» | «area» | «rooms».</summary>
        public string OptimizationPriority { get; set; } = "efficiency";

        /// <summary>Дополнительные пользовательские параметры.</summary>
        public Dictionary<string, string> CustomParameters { get; set; } = new();

        // ——— Вспомогательные методы ———

        /// <summary>
        /// Возвращает состав квартир в виде словаря для передачи в API.
        /// </summary>
        public Dictionary<string, int> GetApartmentTypeRequirements()
        {
            var dict = new Dictionary<string, int>();
            if (StudioCount > 0)     dict["Studio"]    = StudioCount;
            if (OneRoomCount > 0)    dict["OneRoom"]   = OneRoomCount;
            if (TwoRoomCount > 0)    dict["TwoRoom"]   = TwoRoomCount;
            if (ThreeRoomCount > 0)  dict["ThreeRoom"] = ThreeRoomCount;
            if (FourRoomCount > 0)   dict["FourRoom"]  = FourRoomCount;
            return dict;
        }

        /// <summary>Общее запрошенное количество квартир.</summary>
        public int TotalApartmentsRequested =>
            StudioCount + OneRoomCount + TwoRoomCount + ThreeRoomCount + FourRoomCount;
    }
}
