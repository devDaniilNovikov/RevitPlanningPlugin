using System;
using System.Collections.Generic;
using System.Linq;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Services.Geometry
{
    /// <summary>
    /// Проверяет сгенерированные варианты перед предпросмотром и записью в модель.
    /// </summary>
    public class LayoutVariantValidator
    {
        private const double GeometryToleranceMeters = 0.001;
        private const double MopTargetTolerance = 0.25;

        public ValidationResult Validate(
            IEnumerable<LayoutVariant> variants,
            GenerationParameters parameters,
            BuildingContour contour)
        {
            var result = new ValidationResult();
            var list = variants?.ToList() ?? new List<LayoutVariant>();

            if (list.Count == 0)
            {
                result.AddError("AI-сервис не вернул ни одного варианта.", "NO_VARIANTS");
                return result;
            }

            foreach (var variant in list)
                ValidateApartmentUnitAreas(variant, parameters, result);

            if (parameters.ValidationMode == ValidationMode.Off)
            {
                result.AddInfo("Мягкая проверка результата отключена; явные лимиты площади квартир проверены.", "VALIDATION_OFF");
                return result;
            }

            if (list.Count < parameters.VariantCount)
            {
                AddByMode(result, parameters,
                    $"AI-сервис вернул {list.Count} вариантов вместо {parameters.VariantCount}.",
                    "VARIANT_COUNT_MISMATCH");
            }

            foreach (var variant in list)
                ValidateVariant(variant, parameters, contour, result);

            return result;
        }

        private static void ValidateVariant(
            LayoutVariant variant,
            GenerationParameters parameters,
            BuildingContour contour,
            ValidationResult result)
        {
            if (variant.Rooms == null || variant.Rooms.Count == 0)
            {
                AddByMode(result, parameters, $"Вариант '{variant.Name}' не содержит помещений.", "ROOMS_MISSING");
                return;
            }

            ValidateApartmentRequirements(variant, parameters, result);
            ValidateRequiredRoomTypes(variant, parameters, result);
            ValidateAreas(variant, parameters, result);
            ValidateMopRequirements(variant, parameters, result);
            ValidateGeometry(variant, parameters, contour, result);
        }

        private static void ValidateApartmentRequirements(
            LayoutVariant variant,
            GenerationParameters parameters,
            ValidationResult result)
        {
            var requested = parameters.GetApartmentTypeRequirements();
            if (parameters.PlanningDetailMode == PlanningDetailMode.ApartmentRooms)
                requested = parameters.GetEffectiveApartmentTypeRequirements();
            if (requested.Count == 0) return;
            var actualProgram = GetActualApartmentProgram(variant);
            var distribution = actualProgram.Distribution;

            foreach (var kv in requested)
            {
                distribution.TryGetValue(kv.Key, out var actual);
                if (actual < kv.Value)
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}': тип {kv.Key} сгенерирован {actual} из {kv.Value}.",
                        "APARTMENT_TYPE_MISSING");
                }
            }

            var requiredCount = parameters.PlanningDetailMode == PlanningDetailMode.ApartmentRooms
                ? 1
                : parameters.TotalApartmentsRequested;
            if (actualProgram.Count != requiredCount)
            {
                AddByMode(result, parameters,
                    $"Вариант '{variant.Name}': всего квартир {actualProgram.Count}, требуется {requiredCount}.",
                    "APARTMENT_COUNT_MISMATCH");
            }

            var distributionTotal = distribution.Values.Sum();
            if (distributionTotal != actualProgram.Count)
            {
                AddByMode(result, parameters,
                    $"Вариант '{variant.Name}': сумма квартирографии {distributionTotal} не совпадает с количеством квартир {actualProgram.Count}.",
                    "APARTMENT_DISTRIBUTION_MISMATCH");
            }
        }

        private static void ValidateRequiredRoomTypes(
            LayoutVariant variant,
            GenerationParameters parameters,
            ValidationResult result)
        {
            if (parameters.RequiredRoomTypes == null || parameters.RequiredRoomTypes.Count == 0)
                return;

            foreach (var roomType in parameters.GetEffectiveRequiredRoomTypes())
            {
                if (variant.Rooms.Any(r => r.Type == roomType))
                    continue;

                AddByMode(result, parameters,
                    $"Вариант '{variant.Name}': отсутствует обязательный тип помещения {roomType}.",
                    "ROOM_TYPE_MISSING");
            }
        }

        private static void ValidateAreas(
            LayoutVariant variant,
            GenerationParameters parameters,
            ValidationResult result)
        {
            if (variant.TotalArea <= 0)
                AddByMode(result, parameters, $"Вариант '{variant.Name}': общая площадь не задана.", "TOTAL_AREA_MISSING");

            if (variant.UsableArea < 0 || variant.UsableArea > Math.Max(variant.TotalArea, 0) * 1.05)
            {
                AddByMode(result, parameters,
                    $"Вариант '{variant.Name}': полезная площадь выходит за допустимые пределы.",
                    "USABLE_AREA_INVALID");
            }

            foreach (var room in variant.Rooms)
            {
                if (room.Area <= 0)
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}', '{room.Name}': площадь должна быть положительной.",
                        "ROOM_AREA_NON_POSITIVE");
                    continue;
                }

                if (!ShouldApplyRoomAreaBounds(room))
                    continue;

                var minArea = parameters.MinRoomArea;
                var maxArea = parameters.MaxRoomArea;
                var minCode = "ROOM_AREA_TOO_SMALL";
                var maxCode = "ROOM_AREA_TOO_LARGE";

                if (minArea > 0 && room.Area < minArea)
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}', '{room.Name}': площадь {room.Area:F1} м² меньше минимума {minArea:F1} м².",
                        minCode);
                }

                if (maxArea > 0 && room.Area > maxArea)
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}', '{room.Name}': площадь {room.Area:F1} м² больше максимума {maxArea:F1} м².",
                        maxCode);
                }
            }
        }

        private static bool ShouldApplyRoomAreaBounds(RoomLayout room)
        {
            if (room.Properties.TryGetValue("apartment_type", out var apartmentType)
                && !string.IsNullOrWhiteSpace(apartmentType))
            {
                return false;
            }

            return room.Type != RoomType.CommonArea
                   && room.Type != RoomType.Corridor
                   && room.Type != RoomType.Lobby
                   && room.Type != RoomType.Elevator
                   && room.Type != RoomType.Staircase
                   && room.Type != RoomType.Technical;
        }

        private static void ValidateMopRequirements(
            LayoutVariant variant,
            GenerationParameters parameters,
            ValidationResult result)
        {
            if (parameters.PlanningDetailMode == PlanningDetailMode.ApartmentRooms)
                return;

            if (!variant.MopRooms.Any())
            {
                AddByMode(result, parameters, $"Вариант '{variant.Name}' не содержит МОП.", "MOP_MISSING");
            }

            if (parameters.MopAreaTarget > 0)
            {
                var delta = Math.Abs(variant.MopArea - parameters.MopAreaTarget);
                var allowed = Math.Max(1.0, parameters.MopAreaTarget * MopTargetTolerance);
                if (delta > allowed)
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}': площадь МОП {variant.MopArea:F1} м² отличается от цели {parameters.MopAreaTarget:F1} м².",
                        "MOP_AREA_OUT_OF_RANGE");
                }
            }

            if (parameters.MinCorridorWidth <= 0) return;

            foreach (var room in variant.MopRooms.Where(r => r.Type == RoomType.CommonArea || r.Type == RoomType.Corridor))
            {
                var width = EstimateMinimumDimension(room);
                if (width > 0 && width < parameters.MinCorridorWidth)
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}', '{room.Name}': расчетная ширина МОП {width:F2} м меньше {parameters.MinCorridorWidth:F2} м.",
                        "MOP_CORRIDOR_TOO_NARROW");
                }
            }
        }

        private static void ValidateGeometry(
            LayoutVariant variant,
            GenerationParameters parameters,
            BuildingContour contour,
            ValidationResult result)
        {
            var contourPolygon = contour.GetOuterVertices();
            var innerPolygons = contour.GetInnerVertices();
            foreach (var room in variant.Rooms)
            {
                if (room.Boundary == null || room.Boundary.Count < 3)
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}', '{room.Name}': контур помещения содержит менее 3 сегментов.",
                        "ROOM_BOUNDARY_TOO_SHORT");
                    continue;
                }

                var first = room.Boundary.First().Start;
                var last = room.Boundary.Last().End;
                if (first.DistanceTo(last) > GeometryToleranceMeters)
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}', '{room.Name}': контур помещения не замкнут.",
                        "ROOM_BOUNDARY_NOT_CLOSED");
                }

                if (room.Boundary.Any(s => HasInvalidPoint(s.Start) || HasInvalidPoint(s.End)))
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}', '{room.Name}': геометрия содержит некорректные координаты.",
                        "ROOM_GEOMETRY_INVALID");
                }

                if (HasSelfIntersection(room.Boundary))
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}', '{room.Name}': контур помещения самопересекается.",
                        "ROOM_BOUNDARY_SELF_INTERSECTION");
                }

                if (contourPolygon.Count >= 3 && !RoomInsideContour(room, contourPolygon, innerPolygons))
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}', '{room.Name}': помещение выходит за выбранный контур.",
                        "ROOM_OUTSIDE_CONTOUR");
                }

                if (room.LabelPoint == null)
                {
                    AddByMode(result, parameters,
                        $"Вариант '{variant.Name}', '{room.Name}': не задана точка размещения помещения.",
                        "ROOM_LABEL_POINT_MISSING");
                }
                else
                {
                    var roomPolygon = GetBoundaryVertices(room);
                    if (roomPolygon.Count >= 3 && !PointInPolygonOrOnBoundary(room.LabelPoint, roomPolygon))
                    {
                        AddByMode(result, parameters,
                            $"Вариант '{variant.Name}', '{room.Name}': точка размещения находится вне помещения.",
                            "ROOM_LABEL_POINT_OUTSIDE_ROOM");
                    }

                    if (contourPolygon.Count >= 3
                        && (!PointInPolygonOrOnBoundary(room.LabelPoint, contourPolygon)
                            || innerPolygons.Any(inner => inner.Count >= 3 && PointInPolygonOrOnBoundary(room.LabelPoint, inner))))
                    {
                        AddByMode(result, parameters,
                            $"Вариант '{variant.Name}', '{room.Name}': точка размещения находится вне выбранного контура.",
                            "ROOM_LABEL_POINT_OUTSIDE_CONTOUR");
                    }
                }
            }

            ValidateRoomOverlaps(variant, parameters, result);

            if (variant.TotalArea > 0 && contour.ApproximateArea > 0)
            {
                var areaDelta = Math.Abs(variant.TotalArea - contour.ApproximateArea);
                if (areaDelta > Math.Max(5.0, contour.ApproximateArea * 0.05))
                {
                    result.AddWarning(
                        $"Вариант '{variant.Name}': total_area {variant.TotalArea:F1} м² заметно отличается от площади контура {contour.ApproximateArea:F1} м².",
                        "TOTAL_AREA_CONTOUR_MISMATCH");
                }
            }
        }

        private static void ValidateApartmentUnitAreas(
            LayoutVariant variant,
            GenerationParameters parameters,
            ValidationResult result)
        {
            foreach (var apartment in GetApartmentAreas(variant))
            {
                if (parameters.MinApartmentArea > 0 && apartment.area < parameters.MinApartmentArea)
                {
                    result.AddError(
                        $"Вариант '{variant.Name}', квартира '{apartment.id}': площадь {apartment.area:F1} м² меньше минимума {parameters.MinApartmentArea:F1} м².",
                        "APARTMENT_AREA_TOO_SMALL");
                }

                var maxApartmentArea = parameters.GetMaxApartmentAreaForType(apartment.type);
                if (maxApartmentArea > 0 && apartment.area > maxApartmentArea)
                {
                    result.AddError(
                        $"Вариант '{variant.Name}', квартира '{apartment.id}': площадь {apartment.area:F1} м² больше максимума для типа {apartment.type} {maxApartmentArea:F1} м².",
                        "APARTMENT_AREA_TOO_LARGE");
                }
            }
        }

        private static (int Count, Dictionary<string, int> Distribution) GetActualApartmentProgram(LayoutVariant variant)
        {
            var apartmentRooms = variant.Rooms
                .Where(room => room.Properties.TryGetValue("apartment_type", out var type)
                               && !string.IsNullOrWhiteSpace(type))
                .ToList();

            if (apartmentRooms.Count == 0)
            {
                return (variant.ApartmentCount,
                    new Dictionary<string, int>(
                        variant.ApartmentTypeDistribution ?? new Dictionary<string, int>(),
                        StringComparer.OrdinalIgnoreCase));
            }

            var distribution = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var groups = apartmentRooms
                .GroupBy(room => room.Properties.TryGetValue("apartment_id", out var id)
                                 && !string.IsNullOrWhiteSpace(id)
                    ? id
                    : room.Id)
                .ToList();

            foreach (var group in groups)
            {
                var type = group.First().Properties["apartment_type"];
                distribution.TryGetValue(type, out var count);
                distribution[type] = count + 1;
            }

            return (groups.Count, distribution);
        }

        private static IEnumerable<(string id, string type, double area)> GetApartmentAreas(LayoutVariant variant)
        {
            return variant.Rooms
                .Where(room => room.Properties.ContainsKey("apartment_type"))
                .GroupBy(room => room.Properties.TryGetValue("apartment_id", out var id)
                                 && !string.IsNullOrWhiteSpace(id)
                    ? id
                    : room.Id)
                .Select(group =>
                {
                    var type = group
                        .Select(room => room.Properties.TryGetValue("apartment_type", out var apartmentType)
                            ? apartmentType
                            : string.Empty)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                        ?? string.Empty;
                    return (id: group.Key, type, area: group.Sum(room => room.Area));
                });
        }

        private static double EstimateMinimumDimension(RoomLayout room)
        {
            var points = room.Boundary?.Select(s => s.Start).ToList() ?? new List<Point2D>();
            if (points.Count < 3) return 0;

            var width = points.Max(p => p.X) - points.Min(p => p.X);
            var height = points.Max(p => p.Y) - points.Min(p => p.Y);
            return Math.Min(Math.Abs(width), Math.Abs(height));
        }

        private static bool HasInvalidPoint(Point2D point)
            => double.IsNaN(point.X) || double.IsNaN(point.Y)
               || double.IsInfinity(point.X) || double.IsInfinity(point.Y);

        private static bool RoomInsideContour(
            RoomLayout room,
            List<Point2D> contourPolygon,
            List<List<Point2D>> innerPolygons)
        {
            return room.Boundary.All(segment => SegmentInsideAllowedArea(segment, contourPolygon, innerPolygons));
        }

        private static List<Point2D> GetBoundaryVertices(RoomLayout room)
            => room.Boundary?.Select(s => s.Start).ToList() ?? new List<Point2D>();

        private static bool SegmentInsideAllowedArea(
            ContourSegment segment,
            List<Point2D> contourPolygon,
            List<List<Point2D>> innerPolygons)
        {
            var probePoints = new[]
            {
                segment.Start,
                Interpolate(segment.Start, segment.End, 0.25),
                Interpolate(segment.Start, segment.End, 0.5),
                Interpolate(segment.Start, segment.End, 0.75),
                segment.End
            };

            return probePoints.All(point =>
                PointInPolygonOrOnBoundary(point, contourPolygon)
                && !innerPolygons.Any(inner => inner.Count >= 3 && PointInPolygonOrOnBoundary(point, inner)));
        }

        private static Point2D Interpolate(Point2D start, Point2D end, double t)
            => new Point2D(
                start.X + (end.X - start.X) * t,
                start.Y + (end.Y - start.Y) * t);

        private static bool HasSelfIntersection(List<ContourSegment> boundary)
        {
            for (int i = 0; i < boundary.Count; i++)
            {
                for (int j = i + 2; j < boundary.Count; j++)
                {
                    if (i == 0 && j == boundary.Count - 1) continue;
                    if (SegmentsIntersect(boundary[i].Start, boundary[i].End, boundary[j].Start, boundary[j].End))
                        return true;
                }
            }

            return false;
        }

        private static void ValidateRoomOverlaps(
            LayoutVariant variant,
            GenerationParameters parameters,
            ValidationResult result)
        {
            for (int i = 0; i < variant.Rooms.Count; i++)
            {
                for (int j = i + 1; j < variant.Rooms.Count; j++)
                {
                    var a = variant.Rooms[i];
                    var b = variant.Rooms[j];
                    if (RoomsOverlap(a, b))
                    {
                        AddByMode(result, parameters,
                            $"Вариант '{variant.Name}': помещения '{a.Name}' и '{b.Name}' пересекаются.",
                            "ROOMS_OVERLAP");
                    }
                }
            }
        }

        private static bool RoomsOverlap(RoomLayout a, RoomLayout b)
        {
            var polygonA = GetBoundaryVertices(a);
            var polygonB = GetBoundaryVertices(b);
            if (polygonA.Count < 3 || polygonB.Count < 3)
                return false;

            if (!BoundingBoxesOverlapWithArea(polygonA, polygonB))
                return false;

            if (EdgesProperlyIntersect(a.Boundary, b.Boundary))
                return true;

            return polygonA.Any(p => PointStrictlyInsidePolygon(p, polygonB))
                   || polygonB.Any(p => PointStrictlyInsidePolygon(p, polygonA))
                   || PointStrictlyInsidePolygon(PolygonCentroid(polygonA), polygonB)
                   || PointStrictlyInsidePolygon(PolygonCentroid(polygonB), polygonA);
        }

        private static bool EdgesProperlyIntersect(
            List<ContourSegment> boundaryA,
            List<ContourSegment> boundaryB)
        {
            return boundaryA.Any(a => boundaryB.Any(b =>
                SegmentsIntersect(a.Start, a.End, b.Start, b.End)));
        }

        private static bool BoundingBoxesOverlapWithArea(List<Point2D> a, List<Point2D> b)
        {
            var minAx = a.Min(p => p.X);
            var maxAx = a.Max(p => p.X);
            var minAy = a.Min(p => p.Y);
            var maxAy = a.Max(p => p.Y);
            var minBx = b.Min(p => p.X);
            var maxBx = b.Max(p => p.X);
            var minBy = b.Min(p => p.Y);
            var maxBy = b.Max(p => p.Y);

            var overlapX = Math.Min(maxAx, maxBx) - Math.Max(minAx, minBx);
            var overlapY = Math.Min(maxAy, maxBy) - Math.Max(minAy, minBy);
            return overlapX > GeometryToleranceMeters && overlapY > GeometryToleranceMeters;
        }

        private static Point2D PolygonCentroid(List<Point2D> polygon)
        {
            return new Point2D(
                polygon.Average(p => p.X),
                polygon.Average(p => p.Y));
        }

        private static bool PointInPolygonOrOnBoundary(Point2D point, List<Point2D> polygon)
        {
            for (int i = 0; i < polygon.Count; i++)
            {
                if (PointOnSegment(point, polygon[i], polygon[(i + 1) % polygon.Count]))
                    return true;
            }

            return PointStrictlyInsidePolygon(point, polygon);
        }

        private static bool PointStrictlyInsidePolygon(Point2D point, List<Point2D> polygon)
        {
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
            if (cross > GeometryToleranceMeters) return false;

            var dot = (point.X - a.X) * (b.X - a.X) + (point.Y - a.Y) * (b.Y - a.Y);
            if (dot < -GeometryToleranceMeters) return false;

            var lengthSquared = Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2);
            return dot <= lengthSquared + GeometryToleranceMeters;
        }

        private static bool SegmentsIntersect(Point2D a1, Point2D a2, Point2D b1, Point2D b2)
        {
            var d1 = Direction(b1, b2, a1);
            var d2 = Direction(b1, b2, a2);
            var d3 = Direction(a1, a2, b1);
            var d4 = Direction(a1, a2, b2);

            return ((d1 > GeometryToleranceMeters && d2 < -GeometryToleranceMeters)
                    || (d1 < -GeometryToleranceMeters && d2 > GeometryToleranceMeters))
                   && ((d3 > GeometryToleranceMeters && d4 < -GeometryToleranceMeters)
                       || (d3 < -GeometryToleranceMeters && d4 > GeometryToleranceMeters));
        }

        private static double Direction(Point2D a, Point2D b, Point2D c)
            => (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);

        private static void AddByMode(
            ValidationResult result,
            GenerationParameters parameters,
            string message,
            string code)
        {
            if (parameters.ValidationMode == ValidationMode.Strict)
                result.AddError(message, code);
            else
                result.AddWarning(message, code);
        }
    }
}
