using System;
using System.Collections.Generic;
using System.Linq;
using RevitPlanningPlugin.Models.Domain;

namespace RevitPlanningPlugin.Services.Geometry
{
    /// <summary>
    /// Валидация геометрии контура: замкнутость, самопересечения,
    /// площадь, ориентация.
    /// </summary>
    public class ContourValidator
    {
        private const double ClosureTolerance = 0.001;   // 1 мм
        private const double MinAreaSqM = 1.0;            // мин. площадь 1 м²
        private const double MaxAreaSqM = 1_000_000.0;    // макс. площадь

        public ValidationResult Validate(BuildingContour contour)
        {
            var result = new ValidationResult();

            if (contour.OuterLoop == null || contour.OuterLoop.Count < 3)
            {
                result.AddError("Внешний контур должен содержать не менее 3 сегментов.", "MIN_SEGMENTS");
                return result;
            }

            ValidateClosure(contour, result);
            ValidateSelfIntersections(contour, result);
            ValidateArea(contour, result);
            ValidateOrientation(contour, result);

            for (int i = 0; i < contour.InnerLoops.Count; i++)
            {
                ValidateInnerLoop(contour, i, result);
            }

            return result;
        }

        private void ValidateClosure(BuildingContour contour, ValidationResult result)
        {
            var first = contour.OuterLoop.First().Start;
            var last = contour.OuterLoop.Last().End;
            var gap = first.DistanceTo(last);

            if (gap > ClosureTolerance)
            {
                result.AddError(
                    $"Контур не замкнут: зазор {gap:F4} м между началом и концом.",
                    "NOT_CLOSED");
            }

            // Проверяем непрерывность: End[i] == Start[i+1]
            for (int i = 0; i < contour.OuterLoop.Count - 1; i++)
            {
                var endPt = contour.OuterLoop[i].End;
                var nextStart = contour.OuterLoop[i + 1].Start;
                if (endPt.DistanceTo(nextStart) > ClosureTolerance)
                {
                    result.AddError(
                        $"Разрыв между сегментами #{i} и #{i + 1}: {endPt.DistanceTo(nextStart):F4} м.",
                        "DISCONTINUITY");
                }
            }
        }

        private void ValidateSelfIntersections(BuildingContour contour, ValidationResult result)
        {
            var segments = contour.OuterLoop;
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 2; j < segments.Count; j++)
                {
                    // Не проверяем смежные сегменты
                    if (i == 0 && j == segments.Count - 1) continue;

                    if (SegmentsIntersect(
                        segments[i].Start, segments[i].End,
                        segments[j].Start, segments[j].End))
                    {
                        result.AddError(
                            $"Самопересечение: сегменты #{i} и #{j}.",
                            "SELF_INTERSECTION");
                        return; // достаточно одного сообщения
                    }
                }
            }
        }

        private void ValidateArea(BuildingContour contour, ValidationResult result)
        {
            var area = contour.ApproximateArea;
            if (area < MinAreaSqM)
                result.AddError($"Площадь контура слишком мала: {area:F2} м².", "AREA_TOO_SMALL");
            else if (area > MaxAreaSqM)
                result.AddWarning($"Площадь контура очень велика: {area:F0} м². Проверьте единицы измерения.", "AREA_TOO_LARGE");
        }

        private void ValidateOrientation(BuildingContour contour, ValidationResult result)
        {
            // Формула Шёлейса: положительная площадь → CCW (правильная ориентация),
            // отрицательная площадь → CW (предупреждение).
            var signedArea = ComputeSignedArea(contour.GetOuterVertices());
            if (signedArea < 0)
                result.AddWarning("Контур имеет обход по часовой стрелке. Рекомендуется против часовой.", "CW_ORIENTATION");
        }

        private void ValidateInnerLoop(BuildingContour contour, int index, ValidationResult result)
        {
            var inner = contour.InnerLoops[index];
            var prefix = $"Внутренний контур #{index + 1}";

            if (inner.Count < 3)
            {
                result.AddError($"{prefix}: менее 3 сегментов.", "INNER_MIN_SEGMENTS");
                return;
            }

            ValidateLoopClosure(inner, prefix, result);

            var outerPolygon = contour.GetOuterVertices();
            if (outerPolygon.Count >= 3)
            {
                foreach (var point in inner.Select(s => s.Start))
                {
                    if (!PointInPolygonOrOnBoundary(point, outerPolygon))
                    {
                        result.AddError($"{prefix}: точка {point} находится вне внешнего контура.", "INNER_LOOP_OUTSIDE_OUTER");
                        break;
                    }
                }
            }

            var area = ComputePolygonArea(inner.Select(s => s.Start).ToList());
            if (area < MinAreaSqM)
                result.AddError($"{prefix}: площадь слишком мала ({area:F2} м²).", "INNER_AREA_TOO_SMALL");
        }

        private static void ValidateLoopClosure(List<ContourSegment> loop, string prefix, ValidationResult result)
        {
            var first = loop.First().Start;
            var last = loop.Last().End;
            var gap = first.DistanceTo(last);
            if (gap > ClosureTolerance)
                result.AddError($"{prefix} не замкнут: зазор {gap:F4} м.", "INNER_NOT_CLOSED");

            for (int i = 0; i < loop.Count - 1; i++)
            {
                var gapBetweenSegments = loop[i].End.DistanceTo(loop[i + 1].Start);
                if (gapBetweenSegments > ClosureTolerance)
                {
                    result.AddError(
                        $"{prefix}: разрыв между сегментами #{i} и #{i + 1}: {gapBetweenSegments:F4} м.",
                        "INNER_DISCONTINUITY");
                }
            }
        }

        private static double ComputePolygonArea(List<Point2D> pts)
        {
            return Math.Abs(ComputeSignedArea(pts));
        }

        // ——— Утилиты ———

        private static double ComputeSignedArea(List<Point2D> pts)
        {
            double area = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var j = (i + 1) % pts.Count;
                area += pts[i].X * pts[j].Y;
                area -= pts[j].X * pts[i].Y;
            }
            return area / 2.0;
        }

        /// <summary>Проверка пересечения двух отрезков (2D).</summary>
        private static bool SegmentsIntersect(Point2D a1, Point2D a2, Point2D b1, Point2D b2)
        {
            double d1 = Cross(b1, b2, a1);
            double d2 = Cross(b1, b2, a2);
            double d3 = Cross(a1, a2, b1);
            double d4 = Cross(a1, a2, b2);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
                ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;

            return false;
        }

        private static double Cross(Point2D o, Point2D a, Point2D b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        private static bool PointInPolygonOrOnBoundary(Point2D point, List<Point2D> polygon)
        {
            for (int i = 0; i < polygon.Count; i++)
            {
                if (PointOnSegment(point, polygon[i], polygon[(i + 1) % polygon.Count]))
                    return true;
            }

            var inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];
                var intersects = ((pi.Y > point.Y) != (pj.Y > point.Y))
                                 && (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / (pj.Y - pi.Y) + pi.X);
                if (intersects)
                    inside = !inside;
            }

            return inside;
        }

        private static bool PointOnSegment(Point2D point, Point2D a, Point2D b)
        {
            var cross = Math.Abs((point.Y - a.Y) * (b.X - a.X) - (point.X - a.X) * (b.Y - a.Y));
            if (cross > ClosureTolerance) return false;

            var dot = (point.X - a.X) * (b.X - a.X) + (point.Y - a.Y) * (b.Y - a.Y);
            if (dot < -ClosureTolerance) return false;

            var lengthSquared = Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2);
            return dot <= lengthSquared + ClosureTolerance;
        }
    }
}
