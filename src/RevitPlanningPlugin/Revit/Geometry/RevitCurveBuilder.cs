using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Geometry;
using RevitPlanningPlugin.Services.Logging;

namespace RevitPlanningPlugin.Revit.Geometry
{
    /// <summary>
    /// Построитель кривых Revit из доменных сегментов.
    /// Полная поддержка неортогональных и органичных форм:
    /// Line, Arc, HermiteSpline, Ellipse, NurbSpline.
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

        /// <summary>
        /// Определяет, содержит ли набор сегментов криволинейные элементы.
        /// </summary>
        public static bool HasCurvedSegments(List<ContourSegment> segments)
            => segments.Any(s => s.IsCurved);

        /// <summary>
        /// Строит единичную кривую Revit из доменного сегмента.
        /// </summary>
        public static Curve? BuildCurve(ContourSegment segment, double elevation = 0.0)
        {
            var startPt = ToXYZ(segment.Start, elevation);
            var endPt = ToXYZ(segment.End, elevation);

            if (startPt.DistanceTo(endPt) < 1e-9 && segment.Type == SegmentType.Line)
                return null;

            try
            {
                switch (segment.Type)
                {
                    case SegmentType.Arc:
                        return BuildArc(segment, startPt, endPt, elevation);

                    case SegmentType.Spline:
                        return BuildHermiteSpline(segment, elevation);

                    case SegmentType.Ellipse:
                        return BuildEllipseArc(segment, elevation);

                    case SegmentType.NurbsSpline:
                        return BuildNurbsSpline(segment, elevation);

                    case SegmentType.Line:
                    default:
                        return Line.CreateBound(startPt, endPt);
                }
            }
            catch (Exception ex)
            {
                PluginLogger.Warn($"Не удалось создать {segment.Type}-кривую, fallback на линию: {ex.Message}");
                if (startPt.DistanceTo(endPt) > 1e-9)
                    return Line.CreateBound(startPt, endPt);
                return null;
            }
        }

        // ——— Arc ———

        private static Curve BuildArc(ContourSegment segment, XYZ start, XYZ end, double elevation)
        {
            if (segment.ArcCenter != null)
            {
                var center = ToXYZ(segment.ArcCenter, elevation);
                var midPt = ComputeArcMidpoint(start, end, center, segment.ArcClockwise);
                return Arc.Create(start, end, midPt);
            }
            return Line.CreateBound(start, end);
        }

        // ——— Hermite Spline (органичные кривые через точки) ———

        private static Curve BuildHermiteSpline(ContourSegment segment, double elevation)
        {
            if (segment.SplineControlPoints != null && segment.SplineControlPoints.Count >= 2)
            {
                var pts = new List<XYZ>();
                pts.Add(ToXYZ(segment.Start, elevation));
                foreach (var pt in segment.SplineControlPoints)
                    pts.Add(ToXYZ(pt, elevation));
                pts.Add(ToXYZ(segment.End, elevation));

                pts = DeduplicatePoints(pts);
                if (pts.Count >= 2)
                    return HermiteSpline.Create(pts, false, null);
            }
            return Line.CreateBound(ToXYZ(segment.Start, elevation), ToXYZ(segment.End, elevation));
        }

        // ——— Ellipse Arc (органичные скруглённые формы) ———

        private static Curve BuildEllipseArc(ContourSegment segment, double elevation)
        {
            if (segment.EllipseCenter == null || !segment.EllipseRadiusX.HasValue || !segment.EllipseRadiusY.HasValue)
                return Line.CreateBound(ToXYZ(segment.Start, elevation), ToXYZ(segment.End, elevation));

            var center = ToXYZ(segment.EllipseCenter, elevation);
            double rx = segment.EllipseRadiusX.Value * UnitConverter.MetersToFeet;
            double ry = segment.EllipseRadiusY.Value * UnitConverter.MetersToFeet;

            double rot = segment.EllipseRotation;
            var xDir = new XYZ(Math.Cos(rot), Math.Sin(rot), 0);
            var yDir = new XYZ(-Math.Sin(rot), Math.Cos(rot), 0);

            double startAngle = segment.EllipseStartAngle ?? 0;
            double endAngle = segment.EllipseEndAngle ?? (2 * Math.PI);

            return Ellipse.CreateCurve(center, rx, ry, xDir, yDir, startAngle, endAngle);
        }

        // ——— NURBS Spline (произвольные органичные кривые фасадов) ———

        private static Curve BuildNurbsSpline(ContourSegment segment, double elevation)
        {
            if (segment.SplineControlPoints == null || segment.SplineControlPoints.Count < 2)
                return Line.CreateBound(ToXYZ(segment.Start, elevation), ToXYZ(segment.End, elevation));

            var pts = segment.SplineControlPoints.Select(p => ToXYZ(p, elevation)).ToList();

            if (segment.NurbsWeights != null && segment.NurbsWeights.Count == pts.Count
                && segment.NurbsKnots != null && segment.NurbsKnots.Count > 0)
            {
                return NurbSpline.CreateCurve(segment.NurbsDegree, segment.NurbsKnots, pts, segment.NurbsWeights);
            }

            pts = DeduplicatePoints(pts);
            if (pts.Count >= 2)
                return HermiteSpline.Create(pts, false, null);

            return Line.CreateBound(ToXYZ(segment.Start, elevation), ToXYZ(segment.End, elevation));
        }

        // ——— Утилиты ———

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
                midDir = new XYZ(-v1.Y, v1.X, 0);

            if (clockwise)
                midDir = midDir.Negate();

            return center + midDir * radius;
        }

        private static List<XYZ> DeduplicatePoints(List<XYZ> pts)
        {
            const double tol = 0.003; // ~1 мм в футах
            var result = new List<XYZ> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                if (pts[i].DistanceTo(result[result.Count - 1]) > tol)
                    result.Add(pts[i]);
            }
            return result;
        }
    }
}
