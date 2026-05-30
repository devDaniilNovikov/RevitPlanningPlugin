using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitPlanningPlugin.Models.Api;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Logging;

namespace RevitPlanningPlugin.Services.Api
{
    /// <summary>
    /// Мок-клиент для тестирования плагина без реального API.
    /// Генерирует демо-контуры и варианты планировок с квартирами и МОПами.
    /// </summary>
    public class MockPlanningApiClient : IPlanningApiClient
    {
        private readonly MockScenario _scenario;

        public MockPlanningApiClient(MockScenario scenario = MockScenario.HappyPath)
        {
            _scenario = scenario;
        }

        public Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            PluginLogger.Info("[Mock] TestConnection → OK");
            return Task.FromResult(true);
        }

        public Task<List<ApiContourSummaryDto>> GetContourListAsync(CancellationToken ct = default)
        {
            var list = new List<ApiContourSummaryDto>
            {
                new() { Id = "demo-rect",      Name = "Прямоугольный 30×20м",      Area = 600,
                    Description = "Ортогональный типовой этаж" },
                new() { Id = "demo-lshape",    Name = "Г-образный контур",          Area = 450,
                    Description = "Неортогональный L-shape этаж" },
                new() { Id = "demo-polygon",   Name = "Пятиугольник",               Area = 520,
                    Description = "Неортогональная форма с наклонными гранями" },
                new() { Id = "demo-organic",   Name = "Органичная форма (дуги)",    Area = 480,
                    Description = "Криволинейный контур с дугами и скруглениями" },
                new() { Id = "demo-spline",    Name = "Свободная форма (сплайн)",   Area = 550,
                    Description = "Органичный фасад со сплайновыми кривыми" },
                new() { Id = "demo-courtyard", Name = "С внутренним двором",        Area = 700,
                    Description = "Прямоугольник с внутренним вырезом" }
            };
            PluginLogger.Info($"[Mock] GetContourList → {list.Count} контуров");
            return Task.FromResult(list);
        }

        public async Task<BuildingContour> GetContourAsync(string contourId, CancellationToken ct = default)
        {
            await Task.Delay(200, ct);

            var contour = contourId switch
            {
                "demo-rect"      => CreateRectContour(),
                "demo-lshape"    => CreateLShapeContour(),
                "demo-polygon"   => CreatePolygonContour(),
                "demo-organic"   => CreateOrganicContour(),
                "demo-spline"    => CreateSplineContour(),
                "demo-courtyard" => CreateCourtyardContour(),
                _                => CreateRectContour()
            };

            PluginLogger.Info($"[Mock] GetContour '{contourId}' → {contour.OuterLoop.Count} сегментов, " +
                $"тип: {contour.GeometryDescription}");
            return contour;
        }

        public async Task<List<LayoutVariant>> GenerateLayoutsAsync(
            string contourId, GenerationParameters parameters, CancellationToken ct = default)
        {
            ThrowIfScenarioRequiresGenerationError();

            var delay = Math.Min(parameters.VariantCount * 100, 2000);
            await Task.Delay(delay, ct);

            var contour = await GetContourAsync(contourId, ct);
            var totalArea = contour.ApproximateArea;
            var variants = new List<LayoutVariant>();

            for (int i = 0; i < parameters.VariantCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                variants.Add(GenerateDemoVariant(contour, i, totalArea, parameters, contextSeed: 0));
            }

            ApplyMockScenario(variants, contour);
            PluginLogger.Info($"[Mock] Генерация → {variants.Count} вариантов для '{contourId}' за {delay}ms, сценарий {_scenario}");
            return variants;
        }

        public async Task<List<LayoutVariant>> GenerateLayoutsAsync(
            GenerationRequestContext context, CancellationToken ct = default)
        {
            ThrowIfScenarioRequiresGenerationError();

            var parameters = context.Parameters;
            var delay = Math.Min(parameters.VariantCount * 100, 2000);
            await Task.Delay(delay, ct);

            var contour = context.Contour;
            var totalArea = contour.ApproximateArea;
            var variants = new List<LayoutVariant>();
            var contextSeed = CalculateContextSeed(context);

            for (int i = 0; i < parameters.VariantCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                variants.Add(GenerateDemoVariant(contour, i, totalArea, parameters, contextSeed));
            }

            ApplyMockScenario(variants, contour);
            PluginLogger.Info($"[Mock] Генерация из Revit-контекста → {variants.Count} вариантов, сценарий {_scenario}.");
            return variants;
        }

        // ═══════════════════════════════════════════
        //  Демо-контуры
        // ═══════════════════════════════════════════

        private static BuildingContour CreateRectContour()
        {
            var pts = new[] {
                new Point2D(0, 0), new Point2D(30, 0),
                new Point2D(30, 20), new Point2D(0, 20)
            };
            return ContourFromPoints("demo-rect", "Прямоугольный 30×20м", pts);
        }

        private static BuildingContour CreateLShapeContour()
        {
            var pts = new[] {
                new Point2D(0, 0), new Point2D(30, 0),
                new Point2D(30, 12), new Point2D(18, 12),
                new Point2D(18, 20), new Point2D(0, 20)
            };
            return ContourFromPoints("demo-lshape", "Г-образный контур", pts);
        }

        private static BuildingContour CreatePolygonContour()
        {
            var pts = new[] {
                new Point2D(5, 0), new Point2D(25, 0),
                new Point2D(32, 8), new Point2D(28, 22),
                new Point2D(15, 25), new Point2D(0, 18), new Point2D(0, 6)
            };
            return ContourFromPoints("demo-polygon", "Пятиугольник (неортогональный)", pts);
        }

        private static BuildingContour CreateOrganicContour()
        {
            var segments = new List<ContourSegment>
            {
                new() { Type = SegmentType.Line, Start = new Point2D(5, 0),  End = new Point2D(25, 0) },
                new() { Type = SegmentType.Arc,  Start = new Point2D(25, 0), End = new Point2D(30, 5),
                    ArcCenter = new Point2D(25, 5), ArcRadius = 5, ArcClockwise = false },
                new() { Type = SegmentType.Line, Start = new Point2D(30, 5),  End = new Point2D(30, 15) },
                new() { Type = SegmentType.Arc,  Start = new Point2D(30, 15), End = new Point2D(25, 20),
                    ArcCenter = new Point2D(25, 15), ArcRadius = 5, ArcClockwise = false },
                new() { Type = SegmentType.Line, Start = new Point2D(25, 20), End = new Point2D(5, 20) },
                new() { Type = SegmentType.Arc,  Start = new Point2D(5, 20),  End = new Point2D(0, 15),
                    ArcCenter = new Point2D(5, 15), ArcRadius = 5, ArcClockwise = false },
                new() { Type = SegmentType.Line, Start = new Point2D(0, 15), End = new Point2D(0, 5) },
                new() { Type = SegmentType.Arc,  Start = new Point2D(0, 5),  End = new Point2D(5, 0),
                    ArcCenter = new Point2D(5, 5), ArcRadius = 5, ArcClockwise = false }
            };
            return new BuildingContour
            {
                Id = "demo-organic", Name = "Органичная форма (дуги)", SourceUnit = "m",
                OuterLoop = segments
            };
        }

        private static BuildingContour CreateSplineContour()
        {
            var segments = new List<ContourSegment>
            {
                new() { Type = SegmentType.Line, Start = new Point2D(0, 0), End = new Point2D(30, 0) },
                new() {
                    Type = SegmentType.Spline, Start = new Point2D(30, 0), End = new Point2D(28, 22),
                    SplineControlPoints = new List<Point2D> {
                        new(32, 5), new(34, 10), new(33, 15), new(30, 20)
                    }
                },
                new() {
                    Type = SegmentType.Spline, Start = new Point2D(28, 22), End = new Point2D(0, 20),
                    SplineControlPoints = new List<Point2D> { new(20, 24), new(10, 19) }
                },
                new() { Type = SegmentType.Line, Start = new Point2D(0, 20), End = new Point2D(0, 0) }
            };
            return new BuildingContour
            {
                Id = "demo-spline", Name = "Свободная форма (сплайн)", SourceUnit = "m",
                OuterLoop = segments
            };
        }

        private static BuildingContour CreateCourtyardContour()
        {
            var outer = new[] {
                new Point2D(0, 0), new Point2D(35, 0),
                new Point2D(35, 25), new Point2D(0, 25)
            };
            var inner = new[] {
                new Point2D(10, 8), new Point2D(25, 8),
                new Point2D(25, 17), new Point2D(10, 17)
            };
            var contour = ContourFromPoints("demo-courtyard", "С внутренним двором", outer);
            contour.InnerLoops.Add(LoopFromPoints(inner));
            return contour;
        }

        // ═══════════════════════════════════════════
        //  Генерация демо-вариантов планировки
        // ═══════════════════════════════════════════

        private static LayoutVariant GenerateDemoVariant(
            BuildingContour contour, int index, double totalArea, GenerationParameters parms, int contextSeed)
        {
            var seedOffset = PositiveModulo(contextSeed, 10_000);
            var rng = new Random(42 + index + seedOffset);

            var demoBounds = FindSafeDemoBounds(contour);
            double minX = demoBounds.MinX;
            double maxX = demoBounds.MaxX;
            double minY = demoBounds.MinY;
            double maxY = demoBounds.MaxY;
            double width = maxX - minX;
            double height = maxY - minY;

            var rooms = new List<RoomLayout>();
            var partitions = new List<ContourSegment>();
            var geometryIndex = index + PositiveModulo(contextSeed, 17);

            // ——— Квартиры: размещаются вокруг выбранной схемы МОП ———
            // Mock обязан быть применяемым в demo-сценарии: количество квартир
            // и квартирография соответствуют пользовательским параметрам.
            var requestedApartments = ExpandApartmentProgram(parms);
            var aptDist = new Dictionary<string, int>();
            int apartmentIndex = 0;
            var layoutStyle = GetMockLayoutStyle(geometryIndex, width, height);

            if (layoutStyle == "vertical-core" || layoutStyle == "vertical-shifted-core")
            {
                GenerateVerticalCoreVariant(rooms, partitions, requestedApartments, aptDist,
                    ref apartmentIndex, geometryIndex, minX, maxX, minY, maxY, parms, rng,
                    layoutStyle == "vertical-shifted-core");
            }
            else
            {
                GenerateHorizontalCoreVariant(rooms, partitions, requestedApartments, aptDist,
                    ref apartmentIndex, geometryIndex, minX, maxX, minY, maxY, parms, rng, layoutStyle);
            }

            // ——— Подсчёт метрик ———
            double mopArea = rooms
                .Where(r => r.Type == RoomType.CommonArea
                         || r.Type == RoomType.Lobby
                         || r.Type == RoomType.Elevator)
                .Sum(r => r.Area);

            double corridorArea = rooms
                .Where(r => r.Type == RoomType.CommonArea)
                .Sum(r => r.Area);

            double usableArea = rooms
                .Where(r => r.Type == RoomType.LivingRoom)
                .Sum(r => r.Area);

            int apartmentCount = aptDist.Values.Sum();

            double score = 55 + rng.Next(0, 40);

            var variant = new LayoutVariant
            {
                Id = $"variant_{index}",
                Name = $"Вариант {index + 1} ({apartmentCount} квартир, МОП {mopArea:F0}м²)",
                VariantIndex = index + 1,
                Rooms = rooms,
                Partitions = partitions,
                TotalArea = totalArea,
                UsableArea = usableArea,
                MopArea = mopArea,
                CorridorArea = corridorArea,
                RoomCount = rooms.Count,
                ApartmentCount = apartmentCount,
                ApartmentTypeDistribution = aptDist,
                EfficiencyScore = score
            };

            variant.Metadata["mock_layout_style"] = layoutStyle;
            variant.Metadata["mock_context_seed"] = contextSeed.ToString(CultureInfo.InvariantCulture);
            return variant;
        }

        // ═══════════════════════════════════════════
        //  Вспомогательные методы
        // ═══════════════════════════════════════════

        private static List<string> ExpandApartmentProgram(GenerationParameters parms)
        {
            var requested = parms.GetApartmentTypeRequirements();
            if (requested.Count == 0)
                requested["OneRoom"] = 1;

            var order = new[] { "Studio", "OneRoom", "TwoRoom", "ThreeRoom", "FourRoom" };
            return order
                .Where(requested.ContainsKey)
                .SelectMany(type => Enumerable.Repeat(type, requested[type]))
                .ToList();
        }

        private void ThrowIfScenarioRequiresGenerationError()
        {
            if (_scenario == MockScenario.GenerationError)
            {
                throw new PlanningApiException(
                    "Mock-сценарий: AI-сервис вернул ошибку генерации. Проверьте обработку красного статуса и журнал.",
                    errorCode: "MOCK_GENERATION_ERROR");
            }
        }

        private void ApplyMockScenario(List<LayoutVariant> variants, BuildingContour contour)
        {
            if (_scenario != MockScenario.Hallucination || variants.Count == 0)
                return;

            var variant = variants[0];
            var room = variant.Rooms.FirstOrDefault(r => r.Properties.ContainsKey("apartment_type"))
                       ?? variant.Rooms.FirstOrDefault();
            if (room == null)
                return;

            var outer = contour.GetOuterVertices();
            var maxX = outer.Count > 0 ? outer.Max(p => p.X) : 30;
            var maxY = outer.Count > 0 ? outer.Max(p => p.Y) : 20;
            room.Name = "Галлюцинация AI: помещение вне контура";
            room.Boundary = MakeRectangleSegments(maxX + 2, maxY + 2, maxX + 8, maxY + 7);
            room.Area = 30;
            room.LabelPoint = new Point2D(maxX + 5, maxY + 4.5);
            variant.Metadata["mock_scenario"] = "hallucination";
            variant.Metadata["mock_issue"] = "Помещение намеренно вынесено за выбранный контур для демонстрации валидации галлюцинации.";
        }

        private static int CalculateContextSeed(GenerationRequestContext context)
        {
            unchecked
            {
                var hash = 17;
                AddStableHash(ref hash, context.ProjectContext.DocumentTitle);
                AddStableHash(ref hash, context.ProjectContext.ActiveViewName);
                AddStableHash(ref hash, context.ProjectContext.LevelName);
                AddStableHash(ref hash, context.ProjectContext.ContourSource);
                AddStableHash(ref hash, context.Contour.Id);
                AddStableHash(ref hash, context.Contour.Name);
                hash = hash * 31 + context.Contour.OuterLoop.Count;
                hash = hash * 31 + context.ProjectContext.ExistingElements.Count;

                foreach (var element in context.ProjectContext.ExistingElements.OrderBy(e => e.ElementId).Take(80))
                {
                    AddStableHash(ref hash, element.ElementId);
                    AddStableHash(ref hash, element.Category);
                    AddStableHash(ref hash, element.Name);
                    AddStableHash(ref hash, element.ElementType);
                    AddStableHash(ref hash, element.LevelName ?? string.Empty);
                }

                return hash;
            }
        }

        private static void AddStableHash(ref int hash, string value)
        {
            unchecked
            {
                foreach (var ch in value ?? string.Empty)
                    hash = hash * 31 + ch;
            }
        }

        private static int PositiveModulo(int value, int divisor)
        {
            var result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static string GetMockLayoutStyle(int index, double width, double height)
        {
            var style = index % 5;
            var canUseVerticalCore = width >= Math.Max(8.0, height * 0.75);

            if (style == 1 && canUseVerticalCore)
                return "vertical-core";
            if (style == 3 && canUseVerticalCore)
                return "vertical-shifted-core";
            if (style == 2)
                return "horizontal-lower-core";
            if (style == 4)
                return "horizontal-upper-core";

            return "horizontal-center-core";
        }

        private static void GenerateHorizontalCoreVariant(
            List<RoomLayout> rooms,
            List<ContourSegment> partitions,
            IReadOnlyList<string> requestedApartments,
            Dictionary<string, int> apartmentDistribution,
            ref int apartmentIndex,
            int variantIndex,
            double minX,
            double maxX,
            double minY,
            double maxY,
            GenerationParameters parms,
            Random rng,
            string layoutStyle)
        {
            var width = maxX - minX;
            var height = maxY - minY;
            var mopHeight = CalculateMopStripSize(parms.MopAreaTarget, width, height, parms.MinCorridorWidth);
            var centerRatio = layoutStyle == "horizontal-lower-core"
                ? 0.38
                : layoutStyle == "horizontal-upper-core" ? 0.62 : 0.5;
            var mopY0 = ClampStripStart(minY, maxY, mopHeight, centerRatio);
            var mopY1 = mopY0 + mopHeight;

            var lobbyW = Math.Min(4.0, width * 0.15);
            var elevatorW = Math.Min(3.0, width * 0.10);
            NormalizeCoreSizes(width, ref lobbyW, ref elevatorW);

            var coreRatio = GetCoreRatio(variantIndex);
            var coreWidth = lobbyW + elevatorW;
            var sideClearance = Math.Min(Math.Max(parms.MinCorridorWidth, 1.4),
                Math.Max(0.1, (width - coreWidth) / 2.0));
            var coreX0 = Clamp(minX + width * coreRatio - coreWidth / 2.0,
                minX + sideClearance,
                maxX - sideClearance - coreWidth);
            var lobbyX0 = coreX0;
            var lobbyX1 = lobbyX0 + lobbyW;
            var elevX0 = lobbyX1;
            var elevX1 = elevX0 + elevatorW;

            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(minX, mopY0),
                End = new Point2D(maxX, mopY0)
            });
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(minX, mopY1),
                End = new Point2D(maxX, mopY1)
            });

            rooms.Add(MakeRoom($"mop_{variantIndex}_corr_l", "Коридор МОП",
                RoomType.CommonArea, minX, mopY0, lobbyX0, mopY1));
            rooms.Add(MakeRoom($"mop_{variantIndex}_lobby", "Лифтовый холл",
                RoomType.Lobby, lobbyX0, mopY0, lobbyX1, mopY1));
            rooms.Add(MakeRoom($"mop_{variantIndex}_elev", "Лифт",
                RoomType.Elevator, elevX0, mopY0, elevX1, mopY1));
            rooms.Add(MakeRoom($"mop_{variantIndex}_corr_r", "Коридор МОП",
                RoomType.CommonArea, elevX1, mopY0, maxX, mopY1));

            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(lobbyX0, mopY0),
                End = new Point2D(lobbyX0, mopY1)
            });
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(lobbyX1, mopY0),
                End = new Point2D(lobbyX1, mopY1)
            });
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(elevX1, mopY0),
                End = new Point2D(elevX1, mopY1)
            });

            var firstBandCount = requestedApartments.Count == 1
                ? 1
                : (int)Math.Ceiling(requestedApartments.Count / 2.0);
            var secondBandCount = requestedApartments.Count - firstBandCount;
            var topFirst = layoutStyle == "horizontal-upper-core";

            var bottomCount = topFirst ? secondBandCount : firstBandCount;
            var topCount = topFirst ? firstBandCount : secondBandCount;

            if (bottomCount > 0)
            {
                AddApartmentRow(rooms, partitions, requestedApartments, apartmentDistribution,
                    ref apartmentIndex, bottomCount, variantIndex, minX, maxX, minY, mopY0, rng);
            }

            if (topCount > 0)
            {
                AddApartmentRow(rooms, partitions, requestedApartments, apartmentDistribution,
                    ref apartmentIndex, topCount, variantIndex, minX, maxX, mopY1, maxY, rng);
            }
        }

        private static void GenerateVerticalCoreVariant(
            List<RoomLayout> rooms,
            List<ContourSegment> partitions,
            IReadOnlyList<string> requestedApartments,
            Dictionary<string, int> apartmentDistribution,
            ref int apartmentIndex,
            int variantIndex,
            double minX,
            double maxX,
            double minY,
            double maxY,
            GenerationParameters parms,
            Random rng,
            bool shiftedCore)
        {
            var width = maxX - minX;
            var height = maxY - minY;
            var mopWidth = CalculateMopStripSize(parms.MopAreaTarget, height, width, parms.MinCorridorWidth);
            var centerRatio = shiftedCore ? 0.56 : 0.5;
            var mopX0 = ClampStripStart(minX, maxX, mopWidth, centerRatio);
            var mopX1 = mopX0 + mopWidth;

            var lobbyH = Math.Min(4.0, height * 0.18);
            var elevatorH = Math.Min(3.0, height * 0.13);
            NormalizeCoreSizes(height, ref lobbyH, ref elevatorH);

            var coreRatio = shiftedCore ? 0.35 : 0.5;
            var coreHeight = lobbyH + elevatorH;
            var sideClearance = Math.Min(Math.Max(parms.MinCorridorWidth, 1.4),
                Math.Max(0.1, (height - coreHeight) / 2.0));
            var coreY0 = Clamp(minY + height * coreRatio - coreHeight / 2.0,
                minY + sideClearance,
                maxY - sideClearance - coreHeight);
            var lobbyY0 = coreY0;
            var lobbyY1 = lobbyY0 + lobbyH;
            var elevY0 = lobbyY1;
            var elevY1 = elevY0 + elevatorH;

            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(mopX0, minY),
                End = new Point2D(mopX0, maxY)
            });
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(mopX1, minY),
                End = new Point2D(mopX1, maxY)
            });

            rooms.Add(MakeRoom($"mop_{variantIndex}_corr_b", "Коридор МОП",
                RoomType.CommonArea, mopX0, minY, mopX1, lobbyY0));
            rooms.Add(MakeRoom($"mop_{variantIndex}_lobby", "Лифтовый холл",
                RoomType.Lobby, mopX0, lobbyY0, mopX1, lobbyY1));
            rooms.Add(MakeRoom($"mop_{variantIndex}_elev", "Лифт",
                RoomType.Elevator, mopX0, elevY0, mopX1, elevY1));
            rooms.Add(MakeRoom($"mop_{variantIndex}_corr_t", "Коридор МОП",
                RoomType.CommonArea, mopX0, elevY1, mopX1, maxY));

            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(mopX0, lobbyY0),
                End = new Point2D(mopX1, lobbyY0)
            });
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(mopX0, lobbyY1),
                End = new Point2D(mopX1, lobbyY1)
            });
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(mopX0, elevY1),
                End = new Point2D(mopX1, elevY1)
            });

            var firstColumnCount = requestedApartments.Count == 1
                ? 1
                : (int)Math.Ceiling(requestedApartments.Count / 2.0);
            var secondColumnCount = requestedApartments.Count - firstColumnCount;
            var rightFirst = shiftedCore;
            var leftCount = rightFirst ? secondColumnCount : firstColumnCount;
            var rightCount = rightFirst ? firstColumnCount : secondColumnCount;

            if (leftCount > 0)
            {
                AddApartmentColumn(rooms, partitions, requestedApartments, apartmentDistribution,
                    ref apartmentIndex, leftCount, variantIndex, minX, mopX0, minY, maxY, rng);
            }

            if (rightCount > 0)
            {
                AddApartmentColumn(rooms, partitions, requestedApartments, apartmentDistribution,
                    ref apartmentIndex, rightCount, variantIndex, mopX1, maxX, minY, maxY, rng);
            }
        }

        private static void AddApartmentRow(
            List<RoomLayout> rooms,
            List<ContourSegment> partitions,
            IReadOnlyList<string> requestedApartments,
            Dictionary<string, int> apartmentDistribution,
            ref int apartmentIndex,
            int rowCount,
            int variantIndex,
            double minX,
            double maxX,
            double y0,
            double y1,
            Random rng)
        {
            var width = maxX - minX;
            var cellW = width / rowCount;

            for (int i = 0; i < rowCount && apartmentIndex < requestedApartments.Count; i++)
            {
                double x0 = minX + i * cellW;
                double x1 = i == rowCount - 1 ? maxX : x0 + cellW;
                var aptType = requestedApartments[apartmentIndex];

                rooms.Add(MakeRoom($"apt_{variantIndex}_{apartmentIndex}",
                    $"{ApartmentTypeLabel(aptType)} {apartmentIndex + 1}",
                    RoomType.LivingRoom, x0, y0, x1, y1, aptType));

                apartmentDistribution.TryGetValue(aptType, out var count);
                apartmentDistribution[aptType] = count + 1;

                if (i < rowCount - 1)
                {
                    var sepX = x1 + (rng.NextDouble() - 0.5) * Math.Min(0.2, cellW * 0.02);
                    partitions.Add(new ContourSegment
                    {
                        Type = SegmentType.Line,
                        Start = new Point2D(sepX, y0),
                        End = new Point2D(sepX, y1)
                    });
                }

                apartmentIndex++;
            }
        }

        private static void AddApartmentColumn(
            List<RoomLayout> rooms,
            List<ContourSegment> partitions,
            IReadOnlyList<string> requestedApartments,
            Dictionary<string, int> apartmentDistribution,
            ref int apartmentIndex,
            int columnCount,
            int variantIndex,
            double x0,
            double x1,
            double minY,
            double maxY,
            Random rng)
        {
            var height = maxY - minY;
            var cellH = height / columnCount;

            for (int i = 0; i < columnCount && apartmentIndex < requestedApartments.Count; i++)
            {
                double y0 = minY + i * cellH;
                double y1 = i == columnCount - 1 ? maxY : y0 + cellH;
                var aptType = requestedApartments[apartmentIndex];

                rooms.Add(MakeRoom($"apt_{variantIndex}_{apartmentIndex}",
                    $"{ApartmentTypeLabel(aptType)} {apartmentIndex + 1}",
                    RoomType.LivingRoom, x0, y0, x1, y1, aptType));

                apartmentDistribution.TryGetValue(aptType, out var count);
                apartmentDistribution[aptType] = count + 1;

                if (i < columnCount - 1)
                {
                    var sepY = y1 + (rng.NextDouble() - 0.5) * Math.Min(0.2, cellH * 0.02);
                    partitions.Add(new ContourSegment
                    {
                        Type = SegmentType.Line,
                        Start = new Point2D(x0, sepY),
                        End = new Point2D(x1, sepY)
                    });
                }

                apartmentIndex++;
            }
        }

        private static double CalculateMopStripSize(
            double targetArea,
            double lengthAlongStrip,
            double availableAcrossStrip,
            double minCorridorWidth)
        {
            var stripSize = targetArea > 0 && lengthAlongStrip > 0
                ? targetArea / lengthAlongStrip
                : availableAcrossStrip * 0.15;

            stripSize = Math.Max(Math.Max(1.8, minCorridorWidth), stripSize);
            stripSize = Math.Min(availableAcrossStrip * 0.35, stripSize);
            return Math.Max(0.1, stripSize);
        }

        private static double ClampStripStart(double min, double max, double stripSize, double centerRatio)
        {
            var span = max - min;
            var slack = span - stripSize;
            if (slack <= 0)
                return min;

            var sideBand = Math.Min(Math.Max(1.0, span * 0.08), slack / 2.0);
            return Clamp(min + span * centerRatio - stripSize / 2.0,
                min + sideBand,
                max - stripSize - sideBand);
        }

        private static void NormalizeCoreSizes(double span, ref double firstCoreSize, ref double secondCoreSize)
        {
            var maxCoreSize = Math.Max(0.4, span * 0.45);
            if (firstCoreSize + secondCoreSize <= maxCoreSize)
                return;

            firstCoreSize = maxCoreSize * 0.58;
            secondCoreSize = maxCoreSize * 0.42;
        }

        private static double GetCoreRatio(int variantIndex)
        {
            switch (variantIndex % 5)
            {
                case 2:
                    return 0.34;
                case 3:
                    return 0.66;
                case 4:
                    return 0.42;
                default:
                    return 0.5;
            }
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
                return min;
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static string ApartmentTypeLabel(string type)
        {
            return type switch
            {
                "Studio" => "Студия",
                "OneRoom" => "1К кв.",
                "TwoRoom" => "2К кв.",
                "ThreeRoom" => "3К кв.",
                "FourRoom" => "4К кв.",
                _ => type
            };
        }

        private sealed class DemoBounds
        {
            public double MinX { get; set; }
            public double MaxX { get; set; }
            public double MinY { get; set; }
            public double MaxY { get; set; }
            public double Area => Math.Max(0, MaxX - MinX) * Math.Max(0, MaxY - MinY);
        }

        private static DemoBounds FindSafeDemoBounds(BuildingContour contour)
        {
            var outer = contour.GetOuterVertices();
            if (outer.Count < 3)
                return new DemoBounds { MinX = 0, MaxX = 30, MinY = 0, MaxY = 20 };

            var yStops = outer.Select(p => p.Y)
                .Concat(contour.GetInnerVertices().SelectMany(loop => loop.Select(p => p.Y)))
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            var fallback = new DemoBounds
            {
                MinX = outer.Min(p => p.X),
                MaxX = outer.Max(p => p.X),
                MinY = outer.Min(p => p.Y),
                MaxY = outer.Max(p => p.Y)
            };
            var best = new DemoBounds();

            if (yStops.Count < 2)
                return fallback;

            for (int i = 0; i < yStops.Count - 1; i++)
            {
                var y0 = yStops[i];
                var y1 = yStops[i + 1];
                if (y1 - y0 < 2.2) continue;

                var sampleYs = new[]
                {
                    y0 + (y1 - y0) * 0.05,
                    (y0 + y1) / 2.0,
                    y1 - (y1 - y0) * 0.05
                };

                var intervals = sampleYs
                    .Select(y => GetAllowedIntervals(contour, y))
                    .ToList();

                foreach (var baseInterval in intervals[0])
                {
                    var minX = baseInterval.start;
                    var maxX = baseInterval.end;

                    foreach (var rowIntervals in intervals.Skip(1))
                    {
                        var overlap = rowIntervals
                            .Select(interval => (
                                start: Math.Max(minX, interval.start),
                                end: Math.Min(maxX, interval.end)))
                            .Where(interval => interval.end - interval.start > 2.2)
                            .OrderByDescending(interval => interval.end - interval.start)
                            .FirstOrDefault();

                        if (overlap.end <= overlap.start)
                        {
                            minX = maxX = 0;
                            break;
                        }

                        minX = overlap.start;
                        maxX = overlap.end;
                    }

                    var candidate = new DemoBounds
                    {
                        MinX = minX,
                        MaxX = maxX,
                        MinY = y0,
                        MaxY = y1
                    };

                    if (candidate.Area > best.Area)
                        best = candidate;
                }
            }

            if (!BoundsAreUsable(best))
                return fallback;

            const double margin = 0.05;
            if (best.MaxX - best.MinX > margin * 2 && best.MaxY - best.MinY > margin * 2)
            {
                best.MinX += margin;
                best.MaxX -= margin;
                best.MinY += margin;
                best.MaxY -= margin;
            }

            return best;
        }

        private static bool BoundsAreUsable(DemoBounds bounds)
        {
            return bounds.MaxX - bounds.MinX > 2.2
                   && bounds.MaxY - bounds.MinY > 2.2;
        }

        private static List<(double start, double end)> GetAllowedIntervals(BuildingContour contour, double y)
        {
            var outerIntervals = GetLoopIntervals(contour.GetOuterVertices(), y);
            var innerIntervals = contour.GetInnerVertices()
                .SelectMany(loop => GetLoopIntervals(loop, y))
                .ToList();

            var allowed = new List<(double start, double end)>();
            foreach (var interval in outerIntervals)
            {
                var pieces = new List<(double start, double end)> { interval };
                foreach (var hole in innerIntervals)
                {
                    pieces = pieces.SelectMany(piece => SubtractInterval(piece, hole)).ToList();
                }

                allowed.AddRange(pieces.Where(piece => piece.end - piece.start > 2.2));
            }

            return allowed.OrderBy(interval => interval.start).ToList();
        }

        private static List<(double start, double end)> GetLoopIntervals(List<Point2D> polygon, double y)
        {
            var intersections = new List<double>();
            for (int i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                if ((a.Y > y) == (b.Y > y)) continue;

                var t = (y - a.Y) / (b.Y - a.Y);
                intersections.Add(a.X + t * (b.X - a.X));
            }

            intersections.Sort();
            var intervals = new List<(double start, double end)>();
            for (int i = 0; i + 1 < intersections.Count; i += 2)
            {
                var start = intersections[i];
                var end = intersections[i + 1];
                if (end - start > 0.001)
                    intervals.Add((start, end));
            }

            return intervals;
        }

        private static IEnumerable<(double start, double end)> SubtractInterval(
            (double start, double end) source,
            (double start, double end) cut)
        {
            if (cut.end <= source.start || cut.start >= source.end)
            {
                yield return source;
                yield break;
            }

            if (cut.start > source.start)
                yield return (source.start, Math.Min(cut.start, source.end));

            if (cut.end < source.end)
                yield return (Math.Max(cut.end, source.start), source.end);
        }

        private static RoomLayout MakeRoom(string id, string name, RoomType type,
            double x0, double y0, double x1, double y1, string? aptType = null)
        {
            double area = Math.Abs((x1 - x0) * (y1 - y0));
            var room = new RoomLayout
            {
                Id = id,
                Name = name,
                Type = type,
                Area = area,
                LabelPoint = new Point2D((x0 + x1) / 2, (y0 + y1) / 2),
                Boundary = new List<ContourSegment>
                {
                    new() { Type = SegmentType.Line, Start = new Point2D(x0, y0), End = new Point2D(x1, y0) },
                    new() { Type = SegmentType.Line, Start = new Point2D(x1, y0), End = new Point2D(x1, y1) },
                    new() { Type = SegmentType.Line, Start = new Point2D(x1, y1), End = new Point2D(x0, y1) },
                    new() { Type = SegmentType.Line, Start = new Point2D(x0, y1), End = new Point2D(x0, y0) }
                }
            };
            if (aptType != null)
                room.Properties["apartment_type"] = aptType;
            return room;
        }

        private static List<ContourSegment> MakeRectangleSegments(double x0, double y0, double x1, double y1)
        {
            return LoopFromPoints(new[]
            {
                new Point2D(x0, y0),
                new Point2D(x1, y0),
                new Point2D(x1, y1),
                new Point2D(x0, y1)
            });
        }

        private static BuildingContour ContourFromPoints(string id, string name, Point2D[] pts)
        {
            return new BuildingContour
            {
                Id = id, Name = name, SourceUnit = "m",
                OuterLoop = LoopFromPoints(pts)
            };
        }

        private static List<ContourSegment> LoopFromPoints(Point2D[] pts)
        {
            var segments = new List<ContourSegment>();
            for (int i = 0; i < pts.Length; i++)
            {
                var next = pts[(i + 1) % pts.Length];
                segments.Add(new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = pts[i],
                    End = next
                });
            }
            return segments;
        }
    }
}
