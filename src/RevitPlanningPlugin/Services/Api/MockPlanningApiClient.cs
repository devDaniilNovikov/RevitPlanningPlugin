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
    /// Генерирует демонстрационные контуры и планировки.
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
                new() { Id = "demo-rect", Name = "Прямоугольный контур 30×20м", Area = 600, Description = "Типовой прямоугольный этаж" },
                new() { Id = "demo-lshape", Name = "Г-образный контур", Area = 450, Description = "L-shape этаж" },
                new() { Id = "demo-complex", Name = "Сложный контур с вырезом", Area = 520, Description = "Неортогональная форма" }
            };
            PluginLogger.Info($"[Mock] GetContourList → {list.Count} контуров");
            return Task.FromResult(list);
        }

        public async Task<BuildingContour> GetContourAsync(string contourId, CancellationToken ct = default)
        {
            await Task.Delay(300, ct); // имитация задержки

            var contour = contourId switch
            {
                "demo-rect" => CreateRectContour(),
                "demo-lshape" => CreateLShapeContour(),
                "demo-complex" => CreateComplexContour(),
                _ => CreateRectContour()
            };

            PluginLogger.Info($"[Mock] GetContour '{contourId}' → {contour.OuterLoop.Count} сегментов");
            return contour;
        }

        public async Task<List<LayoutVariant>> GenerateLayoutsAsync(
            string contourId, GenerationParameters parameters, CancellationToken ct = default)
        {
            await Task.Delay(1500, ct); // имитация генерации

            var variants = new List<LayoutVariant>();
            var contour = await GetContourAsync(contourId, ct);
            var totalArea = contour.ApproximateArea;

            for (int i = 0; i < parameters.VariantCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                variants.Add(GenerateDemoVariant(contour, i, totalArea, parameters));
            }

            PluginLogger.Info($"[Mock] Generate → {variants.Count} вариантов для '{contourId}'");
            return variants;
        }

        // ——— Генерация демо-контуров ———

        private static BuildingContour CreateRectContour()
        {
            // Прямоугольник 30×20 м
            var pts = new[] {
                new Point2D(0, 0), new Point2D(30, 0),
                new Point2D(30, 20), new Point2D(0, 20)
            };
            return ContourFromPoints("demo-rect", "Прямоугольный контур 30×20м", pts);
        }

        private static BuildingContour CreateLShapeContour()
        {
            // Г-образный контур
            var pts = new[] {
                new Point2D(0, 0), new Point2D(30, 0),
                new Point2D(30, 12), new Point2D(18, 12),
                new Point2D(18, 20), new Point2D(0, 20)
            };
            return ContourFromPoints("demo-lshape", "Г-образный контур", pts);
        }

        private static BuildingContour CreateComplexContour()
        {
            // Пятиугольник
            var pts = new[] {
                new Point2D(0, 0), new Point2D(28, 0),
                new Point2D(32, 10), new Point2D(20, 22),
                new Point2D(0, 18)
            };
            return ContourFromPoints("demo-complex", "Сложный контур", pts);
        }

        private static BuildingContour ContourFromPoints(string id, string name, Point2D[] pts)
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

            return new BuildingContour
            {
                Id = id,
                Name = name,
                SourceUnit = "m",
                OuterLoop = segments
            };
        }

        // ——— Генерация демо-вариантов планировки ———

        private static LayoutVariant GenerateDemoVariant(
            BuildingContour contour, int index, double totalArea, GenerationParameters parms)
        {
            var rng = new Random(42 + index);
            var vertices = contour.GetOuterVertices();

            // Вычислим bounding box
            double minX = vertices.Min(p => p.X);
            double maxX = vertices.Max(p => p.X);
            double minY = vertices.Min(p => p.Y);
            double maxY = vertices.Max(p => p.Y);

            double width = maxX - minX;
            double height = maxY - minY;

            // Количество делений по X и Y
            int nx = 2 + index % 3;  // 2, 3, 4
            int ny = 2 + (index + 1) % 2;  // 2, 3

            var rooms = new List<RoomLayout>();
            var partitions = new List<ContourSegment>();

            var roomTypes = new[] {
                RoomType.LivingRoom, RoomType.Bedroom, RoomType.Kitchen,
                RoomType.Bathroom, RoomType.Corridor, RoomType.Office,
                RoomType.MeetingRoom, RoomType.Storage
            };

            var roomNames = new[] {
                "Гостиная", "Спальня", "Кухня", "Санузел", "Коридор",
                "Кабинет", "Переговорная", "Кладовая"
            };

            double cellW = width / nx;
            double cellH = height / ny;
            int roomIdx = 0;

            // Горизонтальные перегородки
            for (int j = 1; j < ny; j++)
            {
                double y = minY + j * cellH;
                partitions.Add(new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = new Point2D(minX, y),
                    End = new Point2D(maxX, y)
                });
            }

            // Вертикальные перегородки
            for (int i = 1; i < nx; i++)
            {
                double x = minX + i * cellW;
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

                    var typeIdx = roomIdx % roomTypes.Length;
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
            double score = 60 + rng.Next(0, 35);

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
