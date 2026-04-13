using System.Collections.Generic;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Параметры запуска генерации планировок.
    /// </summary>
    public class GenerationParameters
    {
        /// <summary>Количество вариантов (1–20).</summary>
        public int VariantCount { get; set; } = 3;

        /// <summary>Требуемые типы помещений.</summary>
        public List<RoomType> RequiredRoomTypes { get; set; } = new();

        /// <summary>Минимальная площадь помещения, м².</summary>
        public double MinRoomArea { get; set; } = 8.0;

        /// <summary>Максимальная площадь помещения, м².</summary>
        public double MaxRoomArea { get; set; } = 80.0;

        /// <summary>Минимальная ширина коридора, м.</summary>
        public double MinCorridorWidth { get; set; } = 1.2;

        /// <summary>Приоритет оптимизации: «area» | «rooms» | «efficiency».</summary>
        public string OptimizationPriority { get; set; } = "efficiency";

        /// <summary>Дополнительные пользовательские параметры.</summary>
        public Dictionary<string, string> CustomParameters { get; set; } = new();
    }
}
