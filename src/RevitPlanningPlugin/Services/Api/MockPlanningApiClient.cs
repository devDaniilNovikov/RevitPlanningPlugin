using System;
using System.Collections.Generic;
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
    /// Демонстрирует: ортогональные, неортогональные и органичные контуры,
    /// пакетную генерацию, разнообразные планировки.
    /// </summary>
    public class MockPlanningApiClient : IPlanningApiClient
    {
        public Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            PluginLogger.Info("[Mock] TestConnection → OK");
            return Task.FromResult(true);
        }

        public Task<List<ApiContourSummaryDto>> GetContourListAsync(CancellationToken ct = default)
        {
            var list = new List<ApiContourSummaryDto>
            {
                new() { Id = "demo-rect", Name = "Прямоугольный 30×20м", Area = 600,
                    Description = "Ортогональный типовой этаж" },
                new() { Id = "demo-lshape", Name = "Г-образный контур", Area = 450,
                    Description = "Неортогональный L-shape этаж" },
                new() { Id = "demo-polygon", Name = "Пятиугольник", Area = 520,
                    Description = "Неортогональная форма с наклонными гранями" },
                new() { Id = "demo-organic", Name = "Органичная форма (дуги)", Area = 480,
                    Description = "Криволинейный контур с дугами и скруглениями" },
                new() { Id = "demo-spline", Name = "Свободная форма (сплайн)", Area = 550,
                    Description = "Органичный фасад со сплайновыми кривыми" },
                new() { Id = "demo-courtyard", Name = "С внутренним двором", Area = 700,
                    Description = "Прямоугольник с внутренним вырезом" }
            };
            PluginLogger.Info($"[Mock] GetContourList → {list.Count} контуров");
            return Task.FromResult(list);
        }

        public async Task<BuildingContour> GetContourAsync(string contourId, CancellationToken ct = default)
        {
            await Task.Delay(200, ct); // быстрая имитация

            var contour = contourId switch
            {
                "demo-rect" => CreateRectContour(),
                "demo-lshape" => CreateLShapeContour(),
                "demo-polygon" => CreatePolygonContour(),
                "demo-organic" => CreateOrganicContour(),
                "demo-spline" => CreateSplineContour(),
                "demo-courtyard" => CreateCourtyardContour(),
                _ => CreateRectContour()
            };

            PluginLogger.Info($"[Mock] GetContour '{contourId}' → {contour.OuterLoop.Count} сегментов, " +
                $"тип: {contour.GeometryDescription}");
            return contour;
        }

        public async Task<List<LayoutVariant>> GenerateLayoutsAsync(
            string contourId, GenerationParameters parameters, CancellationToken ct = default)
        {
            // Пакетная генерация: все варианты за один запрос
            // Имитируем быструю генерацию: ~100ms на вариант
            var delay = Math.Min(parameters.VariantCount * 100, 2000);
            await Task.Delay(delay, ct);

            var contour = await GetContourAsync(contourId, ct);
            var totalArea = contour.ApproximateArea;
            var variants = new List<LayoutVariant>();

            for (int i = 0; i < parameters.VariantCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                variants.Add(GenerateDemoVariant(contour, i, totalArea, parameters));
            }

            PluginLogger.Info($"[Mock] Пакетная генерация → {variants.Count} вариантов " +
                $"для '{contourId}' за {delay}ms");
            return variants;
        }

        // ═══════════════════════════════════════════
        //  Демо-контуры: ортогональные
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

        // ═══════════════════════════════════════════
        //  Демо-контуры: неортогональные
        // ═══════════════════════════════════════════

        private static BuildingContour CreatePolygonContour()
        {
            var pts = new[] {
                new Point2D(5, 0), new Point2D(25, 0),
                new Point2D(32, 8), new Point2D(28, 22),
                new Point2D(15, 25), new Point2D(0, 18), new Point2D(0, 6)
            };
            return ContourFromPoints("demo-polygon", "Пятиугольник (неортогональный)", pts);
        }

        // ═══════════════════════════════════════════
        //  Демо-контуры: органичные (дуги, сплайны)
        // ═══════════════════════════════════════════

        private static BuildingContour CreateOrganicContour()
        {
            // Контур со скруглёнными углами (линии + дуги)
            var segments = new List<ContourSegment>
            {
                // Нижняя сторона
                new() { Type = SegmentType.Line, Start = new Point2D(5, 0), End = new Point2D(25, 0) },
                // Скругление правого нижнего угла
                new() {
                    Type = SegmentType.Arc,
                    Start = new Point2D(25, 0), End = new Point2D(30, 5),
                    ArcCenter = new Point2D(25, 5), ArcRadius = 5, ArcClockwise = false
                },
                // Правая сторона
                new() { Type = SegmentType.Line, Start = new Point2D(30, 5), End = new Point2D(30, 15) },
                // Скругление правого верхнего угла
                new() {
                    Type = SegmentType.Arc,
                    Start = new Point2D(30, 15), End = new Point2D(25, 20),
                    ArcCenter = new Point2D(25, 15), ArcRadius = 5, ArcClockwise = false
                },
                // Верхняя сторона
                new() { Type = SegmentType.Line, Start = new Point2D(25, 20), End = new Point2D(5, 20) },
                // Скругление левого верхнего угла
                new() {
                    Type = SegmentType.Arc,
                    Start = new Point2D(5, 20), End = new Point2D(0, 15),
                    ArcCenter = new Point2D(5, 15), ArcRadius = 5, ArcClockwise = false
                },
                // Левая сторона
                new() { Type = SegmentType.Line, Start = new Point2D(0, 15), End = new Point2D(0, 5) },
                // Скругление левого нижнего угла
                new() {
                    Type = SegmentType.Arc,
                    Start = new Point2D(0, 5), End = new Point2D(5, 0),
                    ArcCenter = new Point2D(5, 5), ArcRadius = 5, ArcClockwise = false
                }
            };

            return new BuildingContour
            {
                Id = "demo-organic",
                Name = "Органичная форма (дуги)",
                SourceUnit = "m",
                OuterLoop = segments
            };
        }

        private static BuildingContour CreateSplineContour()
        {
            // Контур с криволинейным фасадом (сплайн)
            var segments = new List<ContourSegment>
            {
                // Нижняя прямая сторона
                new() { Type = SegmentType.Line, Start = new Point2D(0, 0), End = new Point2D(30, 0) },
                // Правая сторона — сплайн (органичный фасад)
                new() {
                    Type = SegmentType.Spline,
                    Start = new Point2D(30, 0), End = new Point2D(28, 22),
                    SplineControlPoints = new List<Point2D> {
                        new(32, 5), new(34, 10), new(33, 15), new(30, 20)
                    }
                },
                // Верхняя сторона — слегка волнистый сплайн
                new() {
                    Type = SegmentType.Spline,
                    Start = new Point2D(28, 22), End = new Point2D(0, 20),
                    SplineControlPoints = new List<Point2D> {
                        new(20, 24), new(10, 19)
                    }
                },
                // Левая прямая сторона
                new() { Type = SegmentType.Line, Start = new Point2D(0, 20), End = new Point2D(0, 0) }
            };

            return new BuildingContour
            {
                Id = "demo-spline",
                Name = "Свободная форма (сплайн)",
                SourceUnit = "m",
                OuterLoop = segments
            };
        }

        // ═══════════════════════════════════════════
        //  Демо-контуры: с внутренним вырезом
        // ═══════════════════════════════════════════

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
        //  Утилиты
        // ═══════════════════════════════════════════

        private static BuildingContour ContourFromPoints(string id, string name, Point2D[] pts)
        {
            return new BuildingContour
            {
                Id = id,
                Name = name,
                SourceUnit = "m",
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

        // ═══════════════════════════════════════════
        //  Генерация демо-вариантов планировки
        // ═══════════════════════════════════════════

        private static LayoutVariant GenerateDemoVariant(
            BuildingContour contour, int index, double totalArea, GenerationParameters parms)
        {
            var rng = new Random(42 + index);
            var vertices = contour.GetOuterVertices();

            double minX = vertices.Min(p => p.X);
            double maxX = vertices.Max(p => p.X);
            double minY = vertices.Min(p => p.Y);
            double maxY = vertices.Max(p => p.Y);

            double width = maxX - minX;
            double height = maxY - minY;

            // Разные стратегии деления для разных вариантов
            int nx = 2 + index % 4;   // 2..5
            int ny = 2 + (index + 1) % 3; // 2..4

            var rooms = new List<RoomLayout>();
            var partitions = new List<ContourSegment>();

            var roomTypes = new[] {
                RoomType.LivingRoom, RoomType.Bedroom, RoomType.Kitchen,
                RoomType.Bathroom, RoomType.Corridor, RoomType.Office,
                RoomType.MeetingRoom, RoomType.Storage, RoomType.OpenSpace
            };

            var roomNames = new[] {
                "Гостиная", "Спальня", "Кухня", "Санузел", "Коридор",
                "Кабинет", "Переговорная", "Кладовая", "Open Space"
            };

            double cellW = width / nx;
            double cellH = height / ny;
            int roomIdx = 0;

            // Генерируем перегородки с лёгким рандомным смещением (разные планировки)
            for (int j = 1; j < ny; j++)
            {
                double y = minY + j * cellH + (rng.NextDouble() - 0.5) * cellH * 0.15;
                y = Math.Max(minY + 1, Math.Min(maxY - 1, y));
                partitions.Add(new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = new Point2D(minX, y),
                    End = new Point2D(maxX, y)
                });
            }

            for (int i = 1; i < nx; i++)
            {
                double x = minX + i * cellW + (rng.NextDouble() - 0.5) * cellW * 0.15;
                x = Math.Max(minX + 1, Math.Min(maxX - 1, x));
                partitions.Add(new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = new Point2D(x, minY),
                    End = new Point2D(x, maxY)
                });
            }

            // Помещения — ячейки сетки
            for (int j = 0; j < ny; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    double x0 = minX + i * cellW;
                    double y0 = minY + j * cellH;
                    double x1 = x0 + cellW;
                    double y1 = y0 + cellH;

                    var typeIdx = (roomIdx + index) % roomTypes.Length;
                    var area = cellW * cellH;

                    rooms.Add(new RoomLayout
                    {
                        Id = $"room_{index}_{roomIdx}",
                        Name = $"{roomNames[typeIdx]} {roomIdx + 1}",
                        Type = roomTypes[typeIdx],
                        Area = area,
                        Boundary = new List<ContourSegment>
                        {
                            new() { Type = SegmentType.Line, Start = new Point2D(x0, y0), End = new Point2D(x1, y0) },
                            new() { Type = SegmentType.Line, Start = new Point2D(x1, y0), End = new Point2D(x1, y1) },
                            new() { Type = SegmentType.Line, Start = new Point2D(x1, y1), End = new Point2D(x0, y1) },
                            new() { Type = SegmentType.Line, Start = new Point2D(x0, y1), End = new Point2D(x0, y0) }
                        },
                        LabelPoint = new Point2D(x0 + cellW / 2, y0 + cellH / 2)
                    });

                    roomIdx++;
                }
            }

            double usableArea = rooms.Where(r => r.Type != RoomType.Corridor).Sum(r => r.Area);
            double corridorArea = rooms.Where(r => r.Type == RoomType.Corridor).Sum(r => r.Area);
            double score = 55 + rng.Next(0, 40);

            return new LayoutVariant
            {
                Id = $"variant_{index}",
                Name = $"Вариант {index + 1} ({nx}×{ny})",
                VariantIndex = index + 1,
                Rooms = rooms,
                Partitions = partitions,
                TotalArea = totalArea,
                UsableArea = usableArea,
                RoomCount = rooms.Count,
                CorridorArea = corridorArea,
                EfficiencyScore = score
            };
        }
    }
}
