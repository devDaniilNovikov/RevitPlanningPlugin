using System.Collections.Generic;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Одно помещение в планировке.
    /// </summary>
    public class RoomLayout
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public RoomType Type { get; set; } = RoomType.Other;
        public double Area { get; set; }

        /// <summary>Контур помещения (замкнутый полигон).</summary>
        public List<ContourSegment> Boundary { get; set; } = new();

        /// <summary>Точка размещения подписи / Room-элемента.</summary>
        public Point2D? LabelPoint { get; set; }

        public Dictionary<string, string> Properties { get; set; } = new();
    }
}
