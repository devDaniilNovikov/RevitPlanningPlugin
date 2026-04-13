using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Geometry;

namespace RevitPlanningPlugin.Revit.Geometry
{
    /// <summary>
    /// Построитель кривых Revit из доменных сегментов.
    /// Все входные координаты — в метрах; конвертируются в футы (Revit internal).
    /// </summary>
    public static class RevitCurveBuilder
    {
        /// <summary>
        /// Преобразует список доменных сегментов в список Revit Curve.
        /// </summary>
        public static List<Curve> BuildCurves(List<ContourSegment> segments, double elevation = 0.0)
        {
            var curves = new List<Curve>();
            foreach (var seg in segments)
            {
                var curve = BuildCurve(seg, elevation);
                if (curve != null)
                    curves.Add(curve);
            }
            return curves;
        }

        /// <summary>
        /// Строит CurveLoop из замкнутого набора сегментов.
        /// </summary>
        public static CurveLoop BuildCurveLoop(List<ContourSegment> segments, double elevation = 0.0)
        {
            var curves = BuildCurves(segments, elevation);
            var loop = new CurveLoop();
            foreach (var c in curves)
                loop.Append(c);
            return loop;
        }

        public static Curve? BuildCurve(ContourSegment segment, double elevation = 0.0)
        {
            var startPt = ToXYZ(segment.Start, elevation);
            var endPt = ToXYZ(segment.End, elevation);

            // Проверяем, что точки не совпадают
            if (startPt.DistanceTo(endPt) < 1e-9)
                return null;

            switch (segment.Type)
            {
                case SegmentType.Arc:
                    return BuildArc(segment, startPt, endPt, elevation);

                case SegmentType.Spline:
                    return BuildSpline(segment, elevation);

                case SegmentType.Line:
                default:
                    return Line.CreateBound(startPt, endPt);
            }
        }

        private static Curve BuildArc(ContourSegment segment, XYZ start, XYZ end, double elevation)
        {
            if (segment.ArcCenter != null)
            {
                var center = ToXYZ(segment.ArcCenter, elevation);
                var radius = segment.ArcRadius ?? center.DistanceTo(start);

                // Вычисляем среднюю точку дуги для Arc.Create (3-point)
                var midAngle = ComputeArcMidpoint(start, end, center, segment.ArcClockwise);
                try
                {
                    return Arc.Create(start, end, midAngle);
                }
                catch
                {
                    // Fallback to line
                    return Line.CreateBound(start, end);
                }
            }

            return Line.CreateBound(start, end);
        }

        private static Curve BuildSpline(ContourSegment segment, double elevation)
        {
            if (segment.SplineControlPoints != null && segment.SplineControlPoints.Count >= 2)
            {
                var pts = new List<XYZ>();
                foreach (var pt in segment.SplineControlPoints)
                    pts.Add(ToXYZ(pt, elevation));

                try
                {
                    return HermiteSpline.Create(pts, false, null);
                }
                catch
                {
                    // Fallback: соединяем start — end линией
                    return Line.CreateBound(ToXYZ(segment.Start, elevation), ToXYZ(segment.End, elevation));
                }
            }

            return Line.CreateBound(ToXYZ(segment.Start, elevation), ToXYZ(segment.End, elevation));
        }

        // ——— Утилиты ———

        /// <summary>
        /// Конвертирует Point2D (метры) → Revit XYZ (футы).
        /// </summary>
        public static XYZ ToXYZ(Point2D pt, double elevationMeters = 0.0)
        {
            return new XYZ(
                pt.X * UnitConverter.MetersToFeet,
                pt.Y * UnitConverter.MetersToFeet,
                elevationMeters * UnitConverter.MetersToFeet);
        }

        private static XYZ ComputeArcMidpoint(XYZ start, XYZ end, XYZ center, bool clockwise)
        {
            var v1 = (start - center).Normalize();
            var v2 = (end - center).Normalize();
            var radius = start.DistanceTo(center);

            var midDir = (v1 + v2).Normalize();
            if (midDir.GetLength() < 1e-9)
            {
                // start и end диаметрально противоположны
                midDir = new XYZ(-v1.Y, v1.X, 0);
            }

            if (clockwise)
                midDir = midDir.Negate();

            return center + midDir * radius;
        }
    }
}
