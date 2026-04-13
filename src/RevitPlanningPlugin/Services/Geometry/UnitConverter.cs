using System;
using System.Collections.Generic;
using System.Linq;
using RevitPlanningPlugin.Models.Domain;

namespace RevitPlanningPlugin.Services.Geometry
{
    /// <summary>
    /// Конвертер единиц измерения.
    /// Revit внутренне работает в футах (до 2021) и в собственных единицах (2022+).
    /// Плагин хранит промежуточные данные в метрах.
    /// </summary>
    public static class UnitConverter
    {
        // Коэффициенты перевода в метры
        private static readonly Dictionary<string, double> ToMeters = new(StringComparer.OrdinalIgnoreCase)
        {
            ["mm"] = 0.001,
            ["cm"] = 0.01,
            ["m"] = 1.0,
            ["ft"] = 0.3048,
            ["in"] = 0.0254
        };

        public const double MetersToFeet = 1.0 / 0.3048;
        public const double FeetToMeters = 0.3048;

        /// <summary>
        /// Перевести координаты контура из исходных единиц в метры.
        /// </summary>
        public static BuildingContour ConvertToMeters(BuildingContour contour)
        {
            if (!ToMeters.TryGetValue(contour.SourceUnit, out var factor))
                throw new InvalidOperationException($"Неизвестная единица измерения: {contour.SourceUnit}");

            if (Math.Abs(factor - 1.0) < 1e-10)
                return contour; // уже в метрах

            ScaleSegments(contour.OuterLoop, factor);
            foreach (var inner in contour.InnerLoops)
                ScaleSegments(inner, factor);

            contour.SourceUnit = "m";
            return contour;
        }

        /// <summary>
        /// Перевести координаты из метров во внутренние единицы Revit (футы).
        /// </summary>
        public static Point2D MetersToRevit(Point2D pt)
            => new(pt.X * MetersToFeet, pt.Y * MetersToFeet);

        public static double MetersToRevit(double meters) => meters * MetersToFeet;

        public static List<ContourSegment> SegmentsToRevitUnits(List<ContourSegment> segments)
        {
            return segments.Select(s => new ContourSegment
            {
                Type = s.Type,
                Start = MetersToRevit(s.Start),
                End = MetersToRevit(s.End),
                ArcCenter = s.ArcCenter != null ? MetersToRevit(s.ArcCenter) : null,
                ArcRadius = s.ArcRadius.HasValue ? MetersToRevit(s.ArcRadius.Value) : null,
                ArcClockwise = s.ArcClockwise,
                SplineControlPoints = s.SplineControlPoints?.Select(MetersToRevit).ToList()
            }).ToList();
        }

        // ——— Internal ———

        private static void ScaleSegments(List<ContourSegment> segments, double factor)
        {
            foreach (var seg in segments)
            {
                ScalePoint(seg.Start, factor);
                ScalePoint(seg.End, factor);
                if (seg.ArcCenter != null) ScalePoint(seg.ArcCenter, factor);
                if (seg.ArcRadius.HasValue) seg.ArcRadius *= factor;
                if (seg.SplineControlPoints != null)
                    foreach (var pt in seg.SplineControlPoints)
                        ScalePoint(pt, factor);
            }
        }

        private static void ScalePoint(Point2D pt, double factor)
        {
            pt.X *= factor;
            pt.Y *= factor;
        }
    }
}
