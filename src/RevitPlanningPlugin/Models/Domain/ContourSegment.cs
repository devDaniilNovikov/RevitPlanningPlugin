using System.Collections.Generic;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Сегмент контура: линия, дуга или сплайн.
    /// </summary>
    public class ContourSegment
    {
        public SegmentType Type { get; set; } = SegmentType.Line;
        public Point2D Start { get; set; } = new();
        public Point2D End { get; set; } = new();

        /// <summary>Центр дуги (для Type == Arc).</summary>
        public Point2D? ArcCenter { get; set; }

        /// <summary>Радиус дуги (для Type == Arc).</summary>
        public double? ArcRadius { get; set; }

        /// <summary>Направление дуги по часовой стрелке.</summary>
        public bool ArcClockwise { get; set; }

        /// <summary>Контрольные точки сплайна (для Type == Spline).</summary>
        public List<Point2D>? SplineControlPoints { get; set; }

        public double Length
        {
            get
            {
                switch (Type)
                {
                    case SegmentType.Line:
                        return Start.DistanceTo(End);
                    // Для дуги и сплайна — упрощенная оценка
                    default:
                        return Start.DistanceTo(End);
                }
            }
        }
    }
}
