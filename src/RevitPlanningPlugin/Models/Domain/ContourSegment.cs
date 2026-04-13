using System;
using System.Collections.Generic;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Сегмент контура: линия, дуга, сплайн, эллипс или NURBS.
    /// Полная поддержка неортогональных и органичных форм.
    /// </summary>
    public class ContourSegment
    {
        public SegmentType Type { get; set; } = SegmentType.Line;
        public Point2D Start { get; set; } = new();
        public Point2D End { get; set; } = new();

        // ——— Arc ———
        /// <summary>Центр дуги (для Type == Arc).</summary>
        public Point2D? ArcCenter { get; set; }
        /// <summary>Радиус дуги (для Type == Arc).</summary>
        public double? ArcRadius { get; set; }
        /// <summary>Направление дуги по часовой стрелке.</summary>
        public bool ArcClockwise { get; set; }

        // ——— Spline / NurbsSpline ———
        /// <summary>Контрольные точки сплайна.</summary>
        public List<Point2D>? SplineControlPoints { get; set; }
        /// <summary>Веса контрольных точек (для NURBS).</summary>
        public List<double>? NurbsWeights { get; set; }
        /// <summary>Узловой вектор (для NURBS).</summary>
        public List<double>? NurbsKnots { get; set; }
        /// <summary>Степень NURBS-кривой (по умолчанию 3).</summary>
        public int NurbsDegree { get; set; } = 3;

        // ——— Ellipse ———
        /// <summary>Центр эллипса.</summary>
        public Point2D? EllipseCenter { get; set; }
        /// <summary>Большая полуось (радиус X).</summary>
        public double? EllipseRadiusX { get; set; }
        /// <summary>Малая полуось (радиус Y).</summary>
        public double? EllipseRadiusY { get; set; }
        /// <summary>Угол поворота эллипса, радианы.</summary>
        public double EllipseRotation { get; set; }
        /// <summary>Начальный угол дуги эллипса, радианы.</summary>
        public double? EllipseStartAngle { get; set; }
        /// <summary>Конечный угол дуги эллипса, радианы.</summary>
        public double? EllipseEndAngle { get; set; }

        /// <summary>Приблизительная длина сегмента.</summary>
        public double Length
        {
            get
            {
                switch (Type)
                {
                    case SegmentType.Line:
                        return Start.DistanceTo(End);

                    case SegmentType.Arc:
                        if (ArcCenter != null && ArcRadius.HasValue)
                        {
                            // Длина дуги = R * θ.
                            // Направление (ArcClockwise) определяет, какой угловой промежуток брать.
                            double r = ArcRadius.Value;
                            var dx1 = Start.X - ArcCenter.X;
                            var dy1 = Start.Y - ArcCenter.Y;
                            var dx2 = End.X - ArcCenter.X;
                            var dy2 = End.Y - ArcCenter.Y;
                            var angle1 = Math.Atan2(dy1, dx1);
                            var angle2 = Math.Atan2(dy2, dx2);
                            double sweep = angle2 - angle1;
                            if (ArcClockwise)
                            {
                                // CW: угол убывает; нормализуем в (-2π, 0]
                                if (sweep > 0) sweep -= 2 * Math.PI;
                            }
                            else
                            {
                                // CCW: угол возрастает; нормализуем в [0, 2π)
                                if (sweep < 0) sweep += 2 * Math.PI;
                            }
                            return r * Math.Abs(sweep);
                        }
                        return Start.DistanceTo(End);

                    case SegmentType.Ellipse:
                        // Приближение Рамануджана для периметра эллипса, масштаб по углу
                        if (EllipseRadiusX.HasValue && EllipseRadiusY.HasValue)
                        {
                            double a = EllipseRadiusX.Value, b = EllipseRadiusY.Value;
                            double perimeter = Math.PI * (3 * (a + b) - Math.Sqrt((3 * a + b) * (a + 3 * b)));
                            double startA = EllipseStartAngle ?? 0;
                            double endA = EllipseEndAngle ?? (2 * Math.PI);
                            return perimeter * Math.Abs(endA - startA) / (2 * Math.PI);
                        }
                        return Start.DistanceTo(End);

                    case SegmentType.Spline:
                    case SegmentType.NurbsSpline:
                        // Приблизительная длина по ломаной через контрольные точки
                        if (SplineControlPoints != null && SplineControlPoints.Count >= 2)
                        {
                            double len = 0;
                            for (int i = 0; i < SplineControlPoints.Count - 1; i++)
                                len += SplineControlPoints[i].DistanceTo(SplineControlPoints[i + 1]);
                            return len;
                        }
                        return Start.DistanceTo(End);

                    default:
                        return Start.DistanceTo(End);
                }
            }
        }

        /// <summary>Является ли сегмент криволинейным (не прямая линия).</summary>
        public bool IsCurved => Type != SegmentType.Line;
    }
}
