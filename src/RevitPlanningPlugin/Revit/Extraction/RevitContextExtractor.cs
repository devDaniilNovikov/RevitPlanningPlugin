using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Geometry;
using RevitPlanningPlugin.Services.Logging;

namespace RevitPlanningPlugin.Revit.Extraction
{
    /// <summary>
    /// Извлекает контур и проектный контекст из активной Revit-модели.
    /// </summary>
    public class RevitContextExtractor
    {
        private const double PointToleranceMeters = 0.01;
        private const double WallJoinToleranceMeters = 0.50;
        private const double LineIntersectionExtensionToleranceMeters = 1.00;
        private const double IntersectionMergeToleranceMeters = 0.02;
        private const double MinReconstructedSegmentLengthMeters = 0.05;
        private const int MaxContextElements = 200;

        public BuildingContour? TryExtractSelectedContour(UIDocument uiDocument, Document document, Level level)
        {
            var selectedIds = uiDocument.Selection.GetElementIds();
            var selectedIdValues = new HashSet<int>(
                (selectedIds ?? new List<ElementId>()).Select(id => id.IntegerValue));

            if (TryExtractSelectedRoomContour(document, selectedIds, level, out var roomContour))
                return roomContour;

            var segments = new List<ContourSegment>();
            foreach (var id in selectedIds ?? new List<ElementId>())
            {
                var element = document.GetElement(id);
                if (element == null || !IsPotentialContourElement(element))
                    continue;

                var curve = TryGetCurve(element);
                if (curve == null) continue;

                var segment = ToSegment(curve);
                if (segment != null)
                    segments.Add(segment);
            }

            if (TryCreateContourFromSegments(
                    segments,
                    $"revit-selection-{DateTime.Now:yyyyMMddHHmmss}",
                    $"Контур из Revit ({segments.Count} элементов)",
                    level,
                    selectedIdValues,
                    "revit_selection",
                    out var selectedContour))
            {
                PluginLogger.Info($"Извлечен замкнутый контур из выделения Revit: {selectedContour!.OuterLoop.Count} сегментов.");
                return selectedContour;
            }

            if (TryExtractSelectedBoundingContour(document, selectedIds, level, selectedIdValues, out var selectedBoundsContour))
            {
                PluginLogger.Info($"Извлечен габарит выделения Revit: {selectedBoundsContour!.OuterLoop.Count} сегментов.");
                return selectedBoundsContour;
            }

            if (TryExtractLevelBoundingContour(document, level, selectedIdValues, out var levelContour))
            {
                PluginLogger.Info($"Извлечен габаритный контур уровня Revit: {levelContour!.OuterLoop.Count} сегментов.");
                return levelContour;
            }

            return null;
        }

        public BuildingContour? TryExtractContourFromElementIds(
            Document document,
            ICollection<ElementId> elementIds,
            Level level,
            string source,
            string name)
        {
            var selectedIdValues = new HashSet<int>(
                (elementIds ?? new List<ElementId>()).Select(id => id.IntegerValue));
            var segments = new List<ContourSegment>();

            foreach (var id in elementIds ?? new List<ElementId>())
            {
                var element = document.GetElement(id);
                if (element == null || !IsPotentialContourElement(element))
                    continue;

                var curve = TryGetCurve(element);
                if (curve == null) continue;

                var segment = ToSegment(curve);
                if (segment != null)
                    segments.Add(segment);
            }

            if (TryCreateContourFromSegments(
                    segments,
                    $"revit-buffered-{DateTime.Now:yyyyMMddHHmmss}",
                    name,
                    level,
                    selectedIdValues,
                    source,
                    out var contour))
            {
                PluginLogger.Info($"Извлечен контур из накопленных элементов Revit: {contour!.OuterLoop.Count} сегментов.");
                return contour;
            }

            return null;
        }

        public GenerationRequestContext BuildRequestContext(
            UIDocument uiDocument,
            Document document,
            Level level,
            BuildingContour contour,
            GenerationParameters parameters)
        {
            var projectContext = ExtractProjectContext(uiDocument, document, level, contour);
            return new GenerationRequestContext
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Contour = contour,
                ProjectContext = projectContext,
                Parameters = parameters
            };
        }

        public RevitProjectContext ExtractProjectContext(
            UIDocument uiDocument,
            Document document,
            Level level,
            BuildingContour contour)
        {
            var project = new RevitProjectContext
            {
                DocumentTitle = document.Title ?? string.Empty,
                ActiveViewName = document.ActiveView?.Name ?? string.Empty,
                ActiveViewType = document.ActiveView?.ViewType.ToString() ?? string.Empty,
                LevelId = level.Id.IntegerValue.ToString(CultureInfo.InvariantCulture),
                LevelName = level.Name,
                LevelElevationMeters = level.Elevation * UnitConverter.FeetToMeters,
                ContourSource = contour.Metadata.TryGetValue("source", out var source) ? source : "api",
                ProjectParameters = ExtractProjectParameters(document),
                ExistingElements = ExtractExistingElements(document, level)
            };

            project.ProjectParameters["contour_id"] = contour.Id;
            project.ProjectParameters["contour_area_m2"] = contour.ApproximateArea.ToString("F2", CultureInfo.InvariantCulture);
            project.ProjectParameters["contour_segments"] = contour.OuterLoop.Count.ToString(CultureInfo.InvariantCulture);
            project.ProjectParameters["selected_elements"] = uiDocument.Selection.GetElementIds().Count.ToString(CultureInfo.InvariantCulture);

            return project;
        }

        public Level GetActiveLevel(Document document)
        {
            if (document.ActiveView?.GenLevel != null)
                return document.ActiveView.GenLevel;

            return new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .First();
        }

        private static Dictionary<string, string> ExtractProjectParameters(Document document)
        {
            var result = new Dictionary<string, string>();
            var info = document.ProjectInformation;
            if (info == null) return result;

            TryAdd(result, "project_name", info.Name);
            TryAdd(result, "project_number", info.Number);
            TryAdd(result, "client_name", info.ClientName);
            TryAdd(result, "address", info.Address);
            TryAdd(result, "status", info.Status);
            return result;
        }

        private static List<RevitModelElementContext> ExtractExistingElements(Document document, Level level)
        {
            var result = new List<RevitModelElementContext>();
            var activeViewId = document.ActiveView?.Id;
            var collector = activeViewId != null && activeViewId != ElementId.InvalidElementId
                ? new FilteredElementCollector(document, activeViewId)
                : new FilteredElementCollector(document);

            foreach (var element in collector.WhereElementIsNotElementType())
            {
                if (result.Count >= MaxContextElements) break;
                if (!IsRelevantContextElement(element)) continue;
                if (!BelongsToLevel(document, element, level)) continue;

                result.Add(ToElementContext(document, element));
            }

            return result;
        }

        private static bool IsRelevantContextElement(Element element)
        {
            var category = element.Category;
            if (category == null) return false;

            var categoryId = category.Id.IntegerValue;
            var categoryName = category.Name ?? string.Empty;
            return categoryId == (int)BuiltInCategory.OST_Walls
                || categoryId == (int)BuiltInCategory.OST_Doors
                || categoryId == (int)BuiltInCategory.OST_Windows
                || categoryId == (int)BuiltInCategory.OST_Rooms
                || categoryId == (int)BuiltInCategory.OST_RoomSeparationLines
                || categoryId == (int)BuiltInCategory.OST_Columns
                || categoryId == (int)BuiltInCategory.OST_StructuralColumns
                || categoryId == (int)BuiltInCategory.OST_Stairs
                || categoryName.IndexOf("Elevator", StringComparison.OrdinalIgnoreCase) >= 0
                || categoryName.IndexOf("Лифт", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool BelongsToLevel(Document document, Element element, Level level)
        {
            try
            {
                if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId)
                    return element.LevelId == level.Id;

                if (element is Room room && room.LevelId != null)
                    return room.LevelId == level.Id;

                var levelParam = element.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                    ?? element.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)
                    ?? element.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM);

                if (levelParam != null && levelParam.StorageType == StorageType.ElementId)
                    return levelParam.AsElementId() == level.Id;
            }
            catch (Exception ex)
            {
                PluginLogger.Debug($"Не удалось определить уровень элемента {element.Id.IntegerValue}: {ex.Message}");
            }

            return true;
        }

        private static RevitModelElementContext ToElementContext(Document document, Element element)
        {
            var type = document.GetElement(element.GetTypeId());
            var levelName = string.Empty;
            if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId)
                levelName = document.GetElement(element.LevelId)?.Name ?? string.Empty;

            return new RevitModelElementContext
            {
                ElementId = element.Id.IntegerValue.ToString(CultureInfo.InvariantCulture),
                Category = element.Category?.Name ?? string.Empty,
                Name = element.Name ?? string.Empty,
                ElementType = type?.Name ?? element.GetType().Name,
                LevelName = levelName,
                Parameters = ExtractElementParameters(element)
            };
        }

        private static Dictionary<string, string> ExtractElementParameters(Element element)
        {
            var result = new Dictionary<string, string>();
            foreach (Parameter parameter in element.Parameters)
            {
                if (result.Count >= 12) break;
                if (parameter.Definition == null) continue;

                var name = parameter.Definition.Name;
                if (string.IsNullOrWhiteSpace(name) || result.ContainsKey(name)) continue;

                var value = TryReadParameterValue(parameter);
                if (!string.IsNullOrWhiteSpace(value))
                    result[name] = value!;
            }

            return result;
        }

        private static string? TryReadParameterValue(Parameter parameter)
        {
            try
            {
                var display = parameter.AsValueString();
                if (!string.IsNullOrWhiteSpace(display))
                    return display;

                return parameter.StorageType switch
                {
                    StorageType.String => parameter.AsString(),
                    StorageType.Integer => parameter.AsInteger().ToString(CultureInfo.InvariantCulture),
                    StorageType.Double => parameter.AsDouble().ToString("G6", CultureInfo.InvariantCulture),
                    StorageType.ElementId => parameter.AsElementId().IntegerValue.ToString(CultureInfo.InvariantCulture),
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        private static bool TryExtractSelectedRoomContour(
            Document document,
            ICollection<ElementId>? selectedIds,
            Level level,
            out BuildingContour? contour)
        {
            contour = null;
            if (selectedIds == null || selectedIds.Count == 0)
                return false;

            var selectedRooms = selectedIds
                .Select(document.GetElement)
                .OfType<Room>()
                .Where(room => BelongsToLevel(document, room, level) && room.Area > 0)
                .ToList();

            if (selectedRooms.Count == 0)
                return false;

            var loops = selectedRooms
                .Select(room => (room, loop: GetLargestRoomBoundaryLoop(room)))
                .Where(item => item.loop != null && item.loop.Count >= 3)
                .OrderByDescending(item => CalculateLoopArea(item.loop!))
                .ToList();

            if (loops.Count == 0)
                return false;

            var best = loops[0];
            var bestLoop = best.loop!;
            if (!IsClosedConnectedLoop(bestLoop))
                return false;

            SnapConnectedLoop(bestLoop);
            contour = new BuildingContour
            {
                Id = $"revit-room-{best.room.Id.IntegerValue}-{DateTime.Now:yyyyMMddHHmmss}",
                Name = $"Контур помещения Revit: {best.room.Name}",
                SourceUnit = "m",
                OuterLoop = bestLoop,
                Metadata = new Dictionary<string, string>
                {
                    ["source"] = "revit_selected_room",
                    ["level"] = level.Name,
                    ["room_id"] = best.room.Id.IntegerValue.ToString(CultureInfo.InvariantCulture),
                    ["selected_element_count"] = selectedIds.Count.ToString(CultureInfo.InvariantCulture)
                }
            };
            return true;
        }

        private static bool TryExtractSelectedBoundingContour(
            Document document,
            ICollection<ElementId>? selectedIds,
            Level level,
            HashSet<int> selectedIdValues,
            out BuildingContour? contour)
        {
            contour = null;
            if (selectedIds == null || selectedIds.Count == 0)
                return false;

            var points = new List<Point2D>();
            var sourceElements = selectedIds
                .Select(document.GetElement)
                .Where(element => element != null && IsSelectedBoundingFallbackElement(document, element!, level))
                .Select(element => element!)
                .ToList();

            if (sourceElements.Count == 0)
                return false;

            if (sourceElements.Count == 1 && !CanUseSingleElementBoundingFallback(sourceElements[0]))
                return false;

            foreach (var element in sourceElements)
            {
                AddBoundingBoxPoints(points, element.get_BoundingBox(document.ActiveView)
                                            ?? element.get_BoundingBox(null));
            }

            return TryCreateBoundingContourFromPoints(
                points,
                $"revit-selection-bounds-{DateTime.Now:yyyyMMddHHmmss}",
                $"Габарит выделения Revit ({selectedIds.Count} элементов)",
                level,
                new Dictionary<string, string>
                {
                    ["source"] = "revit_selection_bounding_box",
                    ["selected_element_count"] = selectedIdValues.Count.ToString(CultureInfo.InvariantCulture),
                    ["source_element_count"] = sourceElements.Count.ToString(CultureInfo.InvariantCulture)
                },
                out contour);
        }

        private static bool TryExtractLevelBoundingContour(
            Document document,
            Level level,
            HashSet<int> selectedIdValues,
            out BuildingContour? contour)
        {
            contour = null;
            var points = new List<Point2D>();
            var sourceElements = 0;

            var activeViewId = document.ActiveView?.Id;
            var collector = activeViewId != null && activeViewId != ElementId.InvalidElementId
                ? new FilteredElementCollector(document, activeViewId)
                : new FilteredElementCollector(document);

            foreach (var element in collector.WhereElementIsNotElementType())
            {
                if (!IsPotentialContourElement(element)) continue;
                if (!BelongsToLevel(document, element, level)) continue;

                var curve = TryGetCurve(element);
                if (curve == null) continue;

                var segment = ToSegment(curve);
                if (segment == null) continue;

                points.Add(segment.Start);
                points.Add(segment.End);
                sourceElements++;
            }

            foreach (var room in GetRoomsOnLevel(document, level))
            {
                var loop = GetLargestRoomBoundaryLoop(room);
                if (loop == null) continue;

                points.AddRange(loop.Select(segment => segment.Start));
                sourceElements++;
            }

            if (TryCreateBoundingContourFromPoints(
                    points,
                    $"revit-level-bounds-{DateTime.Now:yyyyMMddHHmmss}",
                    $"Габарит уровня Revit: {level.Name}",
                    level,
                    new Dictionary<string, string>
                    {
                        ["source"] = "revit_level_bounding_box",
                        ["source_element_count"] = sourceElements.ToString(CultureInfo.InvariantCulture),
                        ["selected_element_count"] = selectedIdValues.Count.ToString(CultureInfo.InvariantCulture)
                    },
                    out contour))
            {
                return true;
            }

            return false;
        }

        private static bool TryCreateBoundingContourFromPoints(
            List<Point2D> points,
            string id,
            string name,
            Level level,
            Dictionary<string, string> metadata,
            out BuildingContour? contour)
        {
            contour = null;
            if (points.Count == 0)
                return false;

            var minX = points.Min(point => point.X);
            var maxX = points.Max(point => point.X);
            var minY = points.Min(point => point.Y);
            var maxY = points.Max(point => point.Y);

            const double minSpanMeters = 3.0;
            ExpandThinSpan(ref minX, ref maxX, minSpanMeters);
            ExpandThinSpan(ref minY, ref maxY, minSpanMeters);

            if (maxX - minX < PointToleranceMeters || maxY - minY < PointToleranceMeters)
                return false;

            const double marginMeters = 0.25;
            metadata["level"] = level.Name;
            contour = new BuildingContour
            {
                Id = id,
                Name = name,
                SourceUnit = "m",
                OuterLoop = MakeRectangleSegments(
                    minX - marginMeters,
                    minY - marginMeters,
                    maxX + marginMeters,
                    maxY + marginMeters),
                Metadata = metadata
            };
            return true;
        }

        private static void AddBoundingBoxPoints(List<Point2D> points, BoundingBoxXYZ? box)
        {
            if (box == null)
                return;

            var transform = box.Transform ?? Transform.Identity;
            var corners = new[]
            {
                new XYZ(box.Min.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Min.X, box.Max.Y, box.Min.Z),
                new XYZ(box.Max.X, box.Min.Y, box.Min.Z),
                new XYZ(box.Max.X, box.Max.Y, box.Min.Z)
            };

            foreach (var corner in corners)
                points.Add(ToPoint2D(transform.OfPoint(corner)));
        }

        private static void ExpandThinSpan(ref double min, ref double max, double minSpan)
        {
            var span = max - min;
            if (span >= minSpan)
                return;

            var center = (min + max) / 2.0;
            min = center - minSpan / 2.0;
            max = center + minSpan / 2.0;
        }

        private static bool TryCreateContourFromSegments(
            List<ContourSegment> segments,
            string id,
            string name,
            Level level,
            HashSet<int> selectedIdValues,
            string source,
            out BuildingContour? contour)
        {
            contour = null;
            if (segments.Count < 3)
                return false;

            var ordered = OrderConnectedLoop(segments);
            if (!IsClosedConnectedLoop(ordered)
                && !TryCreateLoopFromLineIntersections(segments, out ordered)
                && !TryCreateLoopFromLooseEndpoints(segments, out ordered))
            {
                return false;
            }

            SnapConnectedLoop(ordered);
            contour = new BuildingContour
            {
                Id = id,
                Name = name,
                SourceUnit = "m",
                OuterLoop = ordered,
                Metadata = new Dictionary<string, string>
                {
                    ["level"] = level.Name,
                    ["source"] = source,
                    ["selected_element_count"] = selectedIdValues.Count.ToString(CultureInfo.InvariantCulture)
                }
            };
            return true;
        }

        private static IEnumerable<Room> GetRoomsOnLevel(Document document, Level level)
        {
            return new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .OfType<Room>()
                .Where(room => room.Area > 0 && BelongsToLevel(document, room, level));
        }

        private static List<ContourSegment>? GetLargestRoomBoundaryLoop(Room room)
        {
            var options = new SpatialElementBoundaryOptions
            {
                SpatialElementBoundaryLocation = SpatialElementBoundaryLocation.Finish
            };

            var boundaries = room.GetBoundarySegments(options);
            if (boundaries == null || boundaries.Count == 0)
                return null;

            return boundaries
                .Select(loop => loop
                    .Select(segment => ToSegment(segment.GetCurve()))
                    .Where(segment => segment != null)
                    .Select(segment => segment!)
                    .ToList())
                .Where(loop => loop.Count >= 3)
                .OrderByDescending(CalculateLoopArea)
                .FirstOrDefault();
        }

        private static bool IsPotentialContourElement(Element element)
        {
            if (element is Wall)
                return true;

            var categoryId = element.Category?.Id.IntegerValue;
            if (categoryId == (int)BuiltInCategory.OST_RoomSeparationLines
                || categoryId == (int)BuiltInCategory.OST_Walls)
            {
                return true;
            }

            return element is CurveElement
                   && element.Category?.CategoryType == CategoryType.Model;
        }

        private static bool IsSelectedBoundingFallbackElement(Document document, Element element, Level level)
        {
            if (element is ElementType)
                return false;

            var category = element.Category;
            if (category == null || category.CategoryType != CategoryType.Model)
                return false;

            var categoryId = category.Id.IntegerValue;
            var isAllowedCategory =
                categoryId == (int)BuiltInCategory.OST_Walls
                || categoryId == (int)BuiltInCategory.OST_Floors
                || categoryId == (int)BuiltInCategory.OST_Rooms
                || categoryId == (int)BuiltInCategory.OST_RoomSeparationLines;

            return isAllowedCategory && BelongsToLevel(document, element, level);
        }

        private static bool CanUseSingleElementBoundingFallback(Element element)
        {
            var categoryId = element.Category?.Id.IntegerValue;
            return element is Floor
                   || element is Room
                   || categoryId == (int)BuiltInCategory.OST_Floors
                   || categoryId == (int)BuiltInCategory.OST_Rooms;
        }

        private static Curve? TryGetCurve(Element? element)
        {
            if (element == null) return null;

            if (element is CurveElement curveElement)
                return curveElement.GeometryCurve;

            if (element is Wall wall && wall.Location is LocationCurve wallCurve)
                return wallCurve.Curve;

            if (element.Location is LocationCurve locationCurve)
                return locationCurve.Curve;

            return null;
        }

        private static ContourSegment? ToSegment(Curve curve)
        {
            if (!curve.IsBound) return null;

            var start = ToPoint2D(curve.GetEndPoint(0));
            var end = ToPoint2D(curve.GetEndPoint(1));
            if (start.DistanceTo(end) < PointToleranceMeters)
                return null;

            if (curve is Arc arc)
            {
                return new ContourSegment
                {
                    Type = SegmentType.Arc,
                    Start = start,
                    End = end,
                    ArcCenter = ToPoint2D(arc.Center),
                    ArcRadius = arc.Radius * UnitConverter.FeetToMeters,
                    ArcClockwise = false
                };
            }

            return new ContourSegment
            {
                Type = SegmentType.Line,
                Start = start,
                End = end
            };
        }

        private static Point2D ToPoint2D(XYZ point)
            => new(point.X * UnitConverter.FeetToMeters, point.Y * UnitConverter.FeetToMeters);

        private static bool TryCreateLoopFromLooseEndpoints(
            List<ContourSegment> segments,
            out List<ContourSegment> loop)
        {
            loop = OrderConnectedLoop(segments, WallJoinToleranceMeters);
            return IsClosedConnectedLoop(loop, WallJoinToleranceMeters);
        }

        private static bool TryCreateLoopFromLineIntersections(
            List<ContourSegment> source,
            out List<ContourSegment> loop)
        {
            loop = new List<ContourSegment>();
            if (source.Count < 3 || source.Any(segment => segment.Type != SegmentType.Line))
                return false;

            var intersections = source
                .Select(_ => new List<LineIntersectionCandidate>())
                .ToList();

            for (int i = 0; i < source.Count; i++)
            {
                for (int j = i + 1; j < source.Count; j++)
                {
                    if (!TryIntersectInfiniteLines(source[i].Start, source[i].End, source[j].Start, source[j].End, out var point))
                        continue;

                    if (!TryGetLineParameter(source[i].Start, source[i].End, point, out var ti)
                        || !TryGetLineParameter(source[j].Start, source[j].End, point, out var tj))
                    {
                        continue;
                    }

                    if (!IsParameterWithinExtendedSegment(ti, source[i], LineIntersectionExtensionToleranceMeters)
                        || !IsParameterWithinExtendedSegment(tj, source[j], LineIntersectionExtensionToleranceMeters))
                    {
                        continue;
                    }

                    AddIntersectionCandidate(intersections[i], point, ti);
                    AddIntersectionCandidate(intersections[j], point, tj);
                }
            }

            var reconstructed = new List<ContourSegment>();
            for (int i = 0; i < source.Count; i++)
            {
                var candidates = intersections[i]
                    .OrderBy(candidate => candidate.Parameter)
                    .ToList();

                if (candidates.Count < 2)
                    return false;

                var first = candidates.First();
                var last = candidates.Last();
                if (first.Point.DistanceTo(last.Point) < MinReconstructedSegmentLengthMeters)
                    return false;

                reconstructed.Add(new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = first.Point,
                    End = last.Point
                });
            }

            var ordered = OrderConnectedLoop(reconstructed, IntersectionMergeToleranceMeters);
            if (!IsClosedConnectedLoop(ordered, IntersectionMergeToleranceMeters))
                return false;

            loop = ordered;
            return true;
        }

        private static List<ContourSegment> OrderConnectedLoop(List<ContourSegment> source)
            => OrderConnectedLoop(source, PointToleranceMeters);

        private static List<ContourSegment> OrderConnectedLoop(List<ContourSegment> source, double toleranceMeters)
        {
            var remaining = new List<ContourSegment>(source);
            var ordered = new List<ContourSegment> { remaining[0] };
            remaining.RemoveAt(0);

            while (remaining.Count > 0)
            {
                var end = ordered[ordered.Count - 1].End;
                var nextIndex = remaining.FindIndex(s => end.DistanceTo(s.Start) <= toleranceMeters);
                if (nextIndex >= 0)
                {
                    ordered.Add(remaining[nextIndex]);
                    remaining.RemoveAt(nextIndex);
                    continue;
                }

                var reverseIndex = remaining.FindIndex(s => end.DistanceTo(s.End) <= toleranceMeters);
                if (reverseIndex >= 0)
                {
                    ordered.Add(Reverse(remaining[reverseIndex]));
                    remaining.RemoveAt(reverseIndex);
                    continue;
                }

                ordered.AddRange(remaining);
                break;
            }

            return ordered;
        }

        private static bool IsClosedConnectedLoop(List<ContourSegment> loop)
            => IsClosedConnectedLoop(loop, PointToleranceMeters);

        private static bool IsClosedConnectedLoop(List<ContourSegment> loop, double toleranceMeters)
        {
            if (loop.Count < 3)
                return false;

            for (int i = 0; i < loop.Count - 1; i++)
            {
                if (loop[i].End.DistanceTo(loop[i + 1].Start) > toleranceMeters)
                    return false;
            }

            return loop[loop.Count - 1].End.DistanceTo(loop[0].Start) <= toleranceMeters;
        }

        private static void SnapConnectedLoop(List<ContourSegment> loop)
        {
            for (int i = 0; i < loop.Count - 1; i++)
                loop[i + 1].Start = new Point2D(loop[i].End.X, loop[i].End.Y);

            loop[loop.Count - 1].End = new Point2D(loop[0].Start.X, loop[0].Start.Y);
        }

        private static double CalculateLoopArea(List<ContourSegment> loop)
        {
            var points = loop.Select(segment => segment.Start).ToList();
            if (points.Count < 3)
                return 0;

            double area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var next = points[(i + 1) % points.Count];
                area += points[i].X * next.Y - next.X * points[i].Y;
            }

            return Math.Abs(area) / 2.0;
        }

        private static List<ContourSegment> MakeRectangleSegments(double minX, double minY, double maxX, double maxY)
        {
            return new List<ContourSegment>
            {
                new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = new Point2D(minX, minY),
                    End = new Point2D(maxX, minY)
                },
                new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = new Point2D(maxX, minY),
                    End = new Point2D(maxX, maxY)
                },
                new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = new Point2D(maxX, maxY),
                    End = new Point2D(minX, maxY)
                },
                new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = new Point2D(minX, maxY),
                    End = new Point2D(minX, minY)
                }
            };
        }

        private static ContourSegment Reverse(ContourSegment segment)
        {
            return new ContourSegment
            {
                Type = segment.Type,
                Start = segment.End,
                End = segment.Start,
                ArcCenter = segment.ArcCenter,
                ArcRadius = segment.ArcRadius,
                ArcClockwise = !segment.ArcClockwise,
                SplineControlPoints = segment.SplineControlPoints != null
                    ? Enumerable.Reverse(segment.SplineControlPoints).ToList()
                    : null,
                NurbsWeights = segment.NurbsWeights != null ? Enumerable.Reverse(segment.NurbsWeights).ToList() : null,
                NurbsKnots = segment.NurbsKnots != null ? new List<double>(segment.NurbsKnots) : null,
                NurbsDegree = segment.NurbsDegree,
                EllipseCenter = segment.EllipseCenter,
                EllipseRadiusX = segment.EllipseRadiusX,
                EllipseRadiusY = segment.EllipseRadiusY,
                EllipseRotation = segment.EllipseRotation,
                EllipseStartAngle = segment.EllipseEndAngle,
                EllipseEndAngle = segment.EllipseStartAngle
            };
        }

        private static bool TryIntersectInfiniteLines(
            Point2D a1,
            Point2D a2,
            Point2D b1,
            Point2D b2,
            out Point2D point)
        {
            point = new Point2D();
            var ax = a2.X - a1.X;
            var ay = a2.Y - a1.Y;
            var bx = b2.X - b1.X;
            var by = b2.Y - b1.Y;
            var denominator = Cross(ax, ay, bx, by);

            if (Math.Abs(denominator) < 1e-9)
                return false;

            var qx = b1.X - a1.X;
            var qy = b1.Y - a1.Y;
            var t = Cross(qx, qy, bx, by) / denominator;
            point = new Point2D(a1.X + t * ax, a1.Y + t * ay);
            return true;
        }

        private static bool TryGetLineParameter(Point2D start, Point2D end, Point2D point, out double parameter)
        {
            parameter = 0;
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-12)
                return false;

            parameter = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
            return true;
        }

        private static bool IsParameterWithinExtendedSegment(
            double parameter,
            ContourSegment segment,
            double extensionToleranceMeters)
        {
            var length = segment.Start.DistanceTo(segment.End);
            if (length < MinReconstructedSegmentLengthMeters)
                return false;

            var normalizedTolerance = extensionToleranceMeters / length;
            return parameter >= -normalizedTolerance
                   && parameter <= 1.0 + normalizedTolerance;
        }

        private static void AddIntersectionCandidate(
            List<LineIntersectionCandidate> candidates,
            Point2D point,
            double parameter)
        {
            if (candidates.Any(candidate => candidate.Point.DistanceTo(point) <= IntersectionMergeToleranceMeters))
                return;

            candidates.Add(new LineIntersectionCandidate
            {
                Point = point,
                Parameter = parameter
            });
        }

        private static double Cross(double ax, double ay, double bx, double by)
            => ax * by - ay * bx;

        private sealed class LineIntersectionCandidate
        {
            public Point2D Point { get; set; } = new();
            public double Parameter { get; set; }
        }

        private static void TryAdd(Dictionary<string, string> target, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                target[key] = value!;
        }
    }
}
