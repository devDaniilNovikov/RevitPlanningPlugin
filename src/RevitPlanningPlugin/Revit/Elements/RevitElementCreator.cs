using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Revit.Geometry;
using RevitPlanningPlugin.Revit.Transactions;
using RevitPlanningPlugin.Services.Logging;

namespace RevitPlanningPlugin.Revit.Elements
{
    /// <summary>
    /// Хранилище созданных элементов Revit для управления жизненным циклом.
    /// </summary>
    public class CreatedElementsTracker
    {
        private readonly List<ElementId> _elementIds = new();

        public IReadOnlyList<ElementId> ElementIds => _elementIds;

        public void Track(ElementId id)
        {
            if (id != null && id != ElementId.InvalidElementId)
                _elementIds.Add(id);
        }

        public void TrackRange(IEnumerable<ElementId> ids)
        {
            foreach (var id in ids)
                Track(id);
        }

        /// <summary>
        /// Удаляет все отслеживаемые элементы из документа.
        /// </summary>
        public int DeleteAll(Document doc)
        {
            if (_elementIds.Count == 0) return 0;

            var toDelete = _elementIds
                .Where(id => doc.GetElement(id) != null)
                .ToList();

            if (toDelete.Count == 0)
            {
                _elementIds.Clear();
                return 0;
            }

            var collection = new List<ElementId>(toDelete);
            doc.Delete(collection);

            var count = toDelete.Count;
            _elementIds.Clear();
            PluginLogger.Info($"Удалено {count} элементов.");
            return count;
        }
    }

    /// <summary>
    /// Создание элементов Revit из доменных моделей контура и планировки.
    /// </summary>
    public class RevitElementCreator
    {
        // Трекер оставлен для обратной совместимости и очистки старых временных элементов,
        // но текущий preview не создает элементы Revit до подтверждения пользователя.
        public CreatedElementsTracker ContourTracker { get; } = new();

        // Трекер оставлен для обратной совместимости со старыми сессиями плагина.
        public CreatedElementsTracker PreviewTracker { get; } = new();

        // Трекер для окончательно примененного варианта
        public CreatedElementsTracker AppliedTracker { get; } = new();

        /// <summary>
        /// Регистрирует UI-предпросмотр контура без изменения Revit-модели.
        /// </summary>
        public void DrawContour(Document doc, View activeView, BuildingContour contour, Level level)
        {
            PluginLogger.Info(
                $"Контур '{contour.Name}' выбран для UI-предпросмотра. Revit-модель не изменена до применения варианта.");
        }

        /// <summary>
        /// Регистрирует UI-предпросмотр варианта без создания постоянных элементов Revit.
        /// </summary>
        public void DrawLayoutPreview(Document doc, View activeView, LayoutVariant variant, Level level)
        {
            PluginLogger.Info(
                $"Вариант '{variant.Name}' выбран для UI-предпросмотра: " +
                $"{variant.Partitions.Count} перегородок, {variant.Rooms.Count} помещений. Revit-модель не изменена.");
        }

        /// <summary>
        /// Применяет вариант в модель: создаёт Room Separation Lines + Room элементы.
        /// </summary>
        public void ApplyLayout(Document doc, LayoutVariant variant, Level level)
        {
            SafeTransaction.ExecuteGroup(doc, $"Применение варианта '{variant.Name}'", () =>
            {
                // 1. Удаляем ранее применённый вариант (если есть)
                SafeTransaction.Execute(doc, "Очистка предыдущего варианта", tx =>
                {
                    AppliedTracker.DeleteAll(doc);
                    PreviewTracker.DeleteAll(doc);
                });

                // 2. Создаём Room Separation Lines для перегородок
                SafeTransaction.Execute(doc, "Создание разделителей помещений", tx =>
                {
                    CreateRoomSeparators(doc, variant, level);
                });

                // 3. Создаём Room-элементы
                SafeTransaction.Execute(doc, "Создание помещений", tx =>
                {
                    CreateRooms(doc, variant, level);
                });

                PluginLogger.Info($"Вариант '{variant.Name}' применён в модель.");
            });
        }

        // ——— Приватные методы ———

        private void CreateRoomSeparators(Document doc, LayoutVariant variant, Level level)
        {
            var sketchPlane = GetSketchPlane(doc, level);
            var targetView = GetTargetPlanView(doc, level);
            var elevationMeters = level.Elevation * Services.Geometry.UnitConverter.FeetToMeters;
            var failures = new List<string>();

            foreach (var partition in GetUniqueBoundarySegments(variant))
            {
                var curve = RevitCurveBuilder.BuildCurve(partition, elevationMeters);
                if (curve == null)
                {
                    failures.Add($"не удалось построить кривую {partition.Start} -> {partition.End}");
                    continue;
                }

                var curveArray = new CurveArray();
                curveArray.Append(curve);

                var sepLines = doc.Create.NewRoomBoundaryLines(
                    sketchPlane, curveArray, targetView);

                var createdCount = 0;
                if (sepLines != null)
                {
                    foreach (ModelCurve mc in sepLines)
                    {
                        createdCount++;
                        AppliedTracker.Track(mc.Id);
                    }
                }

                if (createdCount == 0)
                {
                    failures.Add($"Revit не создал разделитель {partition.Start} -> {partition.End}");
                }
            }

            if (failures.Count > 0)
            {
                var message = "Не удалось создать разделители помещений: " + string.Join("; ", failures);
                PluginLogger.Warn(message);
                throw new InvalidOperationException(message);
            }
        }

        private void CreateRooms(Document doc, LayoutVariant variant, Level level)
        {
            var failures = new List<string>();

            foreach (var roomLayout in variant.Rooms)
            {
                if (roomLayout.LabelPoint == null)
                {
                    failures.Add($"{roomLayout.Name}: не задана точка размещения");
                    continue;
                }

                var pt = RevitCurveBuilder.ToXYZ(roomLayout.LabelPoint, 0);
                var uv = new UV(pt.X, pt.Y);

                try
                {
                    var room = doc.Create.NewRoom(level, uv);
                    if (room != null)
                    {
                        room.Name = roomLayout.Name;
                        AppliedTracker.Track(room.Id);
                    }
                    else
                    {
                        failures.Add($"{roomLayout.Name}: Revit вернул null при создании Room");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{roomLayout.Name}: {ex.Message}");
                }
            }

            if (failures.Count > 0)
            {
                var message = "Не удалось создать помещения: " + string.Join("; ", failures);
                PluginLogger.Warn(message);
                throw new InvalidOperationException(message);
            }
        }

        private static SketchPlane GetSketchPlane(Document doc, Level level)
        {
            var plane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ,
                new XYZ(0, 0, level.Elevation));
            return SketchPlane.Create(doc, plane);
        }

        private static View GetTargetPlanView(Document doc, Level level)
        {
            if (doc.ActiveView is ViewPlan activePlan
                && !activePlan.IsTemplate
                && activePlan.ViewType == ViewType.FloorPlan
                && activePlan.GenLevel != null
                && activePlan.GenLevel.Id == level.Id)
            {
                return activePlan;
            }

            var matchingPlan = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(view => !view.IsTemplate
                                        && view.GenLevel != null
                                        && view.GenLevel.Id == level.Id
                                        && view.ViewType == ViewType.FloorPlan);

            if (matchingPlan != null)
                return matchingPlan;

            throw new InvalidOperationException(
                $"Не найден план этажа для уровня '{level.Name}'. Откройте план нужного уровня перед применением варианта.");
        }

        private static IEnumerable<ContourSegment> GetUniqueBoundarySegments(LayoutVariant variant)
        {
            var seen = new HashSet<string>();
            var segments = variant.Partitions
                .Concat(variant.Rooms.SelectMany(room => room.Boundary));

            foreach (var segment in segments)
            {
                if (seen.Add(SegmentKey(segment)))
                    yield return segment;
            }
        }

        private static string SegmentKey(ContourSegment segment)
        {
            var a = PointKey(segment.Start);
            var b = PointKey(segment.End);
            return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
        }

        private static string PointKey(Point2D point)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:F3},{1:F3}",
                Math.Round(point.X, 3),
                Math.Round(point.Y, 3));
        }
    }
}
