using System.Collections.Generic;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Один вариант планировочного решения.
    /// </summary>
    public class LayoutVariant
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int VariantIndex { get; set; }

        /// <summary>Помещения варианта.</summary>
        public List<RoomLayout> Rooms { get; set; } = new();

        /// <summary>Разделительные линии / перегородки (линейная геометрия).</summary>
        public List<ContourSegment> Partitions { get; set; } = new();

        // ——— Метрики качества ———
        public double TotalArea { get; set; }
        public double UsableArea { get; set; }
        public int RoomCount { get; set; }
        public double CorridorArea { get; set; }

        /// <summary>Интегральный балл эффективности (0–100).</summary>
        public double EfficiencyScore { get; set; }

        /// <summary>Коэффициент полезной площади = UsableArea / TotalArea.</summary>
        public double UsableRatio => TotalArea > 0 ? UsableArea / TotalArea : 0;

        public Dictionary<string, double> CustomMetrics { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();
    }
}
