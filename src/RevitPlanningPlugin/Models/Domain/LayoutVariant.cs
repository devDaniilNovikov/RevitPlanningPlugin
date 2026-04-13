using System.Collections.Generic;
using System.Linq;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Один вариант планировочного решения, возвращаемый AI-сервисом.
    /// Содержит геометрию помещений, МОПов, перегородок и метрики качества.
    /// </summary>
    public class LayoutVariant
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int VariantIndex { get; set; }

        /// <summary>Все помещения варианта (квартиры + МОПы).</summary>
        public List<RoomLayout> Rooms { get; set; } = new();

        /// <summary>Разделительные линии / перегородки (линейная геометрия).</summary>
        public List<ContourSegment> Partitions { get; set; } = new();

        // ——— Метрики площадей ———

        /// <summary>Общая площадь пятна здания, м².</summary>
        public double TotalArea { get; set; }

        /// <summary>Суммарная жилая (полезная) площадь квартир, м².</summary>
        public double UsableArea { get; set; }

        /// <summary>
        /// Суммарная площадь МОПов (лифтовые холлы, общие коридоры,
        /// места общего пользования), м².
        /// </summary>
        public double MopArea { get; set; }

        /// <summary>Площадь коридоров (может пересекаться с MopArea), м².</summary>
        public double CorridorArea { get; set; }

        // ——— Метрики по квартирам ———

        /// <summary>Общее количество помещений (включая МОПы).</summary>
        public int RoomCount { get; set; }

        /// <summary>Количество квартирных единиц.</summary>
        public int ApartmentCount { get; set; }

        /// <summary>
        /// Распределение квартир по типам.
        /// Ключ — тип (Studio, OneRoom, TwoRoom, ThreeRoom, FourRoom),
        /// значение — количество квартир данного типа.
        /// </summary>
        public Dictionary<string, int> ApartmentTypeDistribution { get; set; } = new();

        // ——— Интегральная оценка ———

        /// <summary>Интегральный балл эффективности (0–100).</summary>
        public double EfficiencyScore { get; set; }

        /// <summary>Коэффициент полезной площади = UsableArea / TotalArea.</summary>
        public double UsableRatio => TotalArea > 0 ? UsableArea / TotalArea : 0;

        public Dictionary<string, double> CustomMetrics { get; set; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new();

        // ——— Миниатюра ———

        /// <summary>SVG-миниатюра варианта для отображения в галерее.</summary>
        public string? ThumbnailSvg { get; set; }

        // ——— Вычисляемые строки для UI ———

        /// <summary>Краткая строка метрик для карточки в каталоге.</summary>
        public string CatalogSummary
        {
            get
            {
                var apartInfo = ApartmentCount > 0
                    ? $"{ApartmentCount} кв."
                    : $"{RoomCount} пом.";
                var mopInfo = MopArea > 0 ? $" | МОП {MopArea:F0}м²" : string.Empty;
                return $"{apartInfo}{mopInfo} | {UsableRatio:P0} | {EfficiencyScore:F0}";
            }
        }

        /// <summary>Детальная строка метрик для панели деталей варианта.</summary>
        public string MetricsDetail
        {
            get
            {
                var parts = new List<string>
                {
                    $"Общая: {TotalArea:F1} м²",
                    $"Полезная: {UsableArea:F1} м²",
                };
                if (ApartmentCount > 0)
                    parts.Add($"Квартир: {ApartmentCount}");
                if (MopArea > 0)
                    parts.Add($"МОП: {MopArea:F1} м²");
                if (CorridorArea > 0)
                    parts.Add($"Коридоры: {CorridorArea:F1} м²");
                parts.Add($"Эффективность: {UsableRatio:P1}");
                parts.Add($"Score: {EfficiencyScore:F0}");
                return string.Join(" | ", parts);
            }
        }

        /// <summary>
        /// Распределение квартир по типам в виде читаемой строки.
        /// Например: «Студии: 2 | 1К: 4 | 2К: 6 | 3К: 2».
        /// </summary>
        public string ApartmentTypeSummary
        {
            get
            {
                if (ApartmentTypeDistribution == null || ApartmentTypeDistribution.Count == 0)
                    return string.Empty;

                var typeLabels = new Dictionary<string, string>
                {
                    ["Studio"]    = "Студии",
                    ["OneRoom"]   = "1К",
                    ["TwoRoom"]   = "2К",
                    ["ThreeRoom"] = "3К",
                    ["FourRoom"]  = "4К"
                };

                var parts = ApartmentTypeDistribution
                    .OrderBy(kv => kv.Key)
                    .Select(kv =>
                    {
                        var label = typeLabels.TryGetValue(kv.Key, out var l) ? l : kv.Key;
                        return $"{label}: {kv.Value}";
                    });

                return string.Join(" | ", parts);
            }
        }

        /// <summary>Список только МОП-помещений.</summary>
        public IEnumerable<RoomLayout> MopRooms =>
            Rooms.Where(r => r.Type == RoomType.CommonArea
                          || r.Type == RoomType.Lobby
                          || r.Type == RoomType.Elevator);

        /// <summary>Список только жилых (квартирных) помещений.</summary>
        public IEnumerable<RoomLayout> ResidentialRooms =>
            Rooms.Where(r => r.Type == RoomType.LivingRoom
                          || r.Type == RoomType.Bedroom
                          || r.Type == RoomType.Kitchen);
    }
}
