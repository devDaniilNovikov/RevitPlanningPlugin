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
    /// Генерирует демо-контуры и варианты планировок с квартирами и МОПами.
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

            PluginLogger.Info($"[Mock] Генерация → {variants.Count} вариантов для '{contourId}' за {delay}ms");
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

            // ——— МОП: полоса по центру (коридор + лифтовый холл) ———
            // МОП занимает горизонтальную полосу высотой ~15% здания по центру
            double mopHeight = Math.Max(1.8, height * 0.15);
            double mopY0 = minY + (height - mopHeight) / 2;
            double mopY1 = mopY0 + mopHeight;

            // Лифтовый холл — квадрат слева в полосе МОП
            double lobbyW = Math.Min(4.0, width * 0.15);
            double elevatorW = Math.Min(3.0, width * 0.10);

            var rooms = new List<RoomLayout>();
            var partitions = new List<ContourSegment>();

            // — Горизонтальные перегородки МОП —
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(minX, mopY0), End = new Point2D(maxX, mopY0)
            });
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(minX, mopY1), End = new Point2D(maxX, mopY1)
            });

            // — МОП: лифтовый холл —
            double lobbyX0 = minX + width * 0.5 - lobbyW / 2;
            double lobbyX1 = lobbyX0 + lobbyW;
            rooms.Add(MakeRoom($"mop_{index}_lobby", "Лифтовый холл",
                RoomType.Lobby, lobbyX0, mopY0, lobbyX1, mopY1));

            // — МОП: шахта лифта —
            double elevX0 = lobbyX1;
            double elevX1 = elevX0 + elevatorW;
            rooms.Add(MakeRoom($"mop_{index}_elev", "Лифт",
                RoomType.Elevator, elevX0, mopY0, elevX1, mopY1));

            // — МОП: коридор (левая часть) —
            rooms.Add(MakeRoom($"mop_{index}_corr_l", "Коридор МОП",
                RoomType.CommonArea, minX, mopY0, lobbyX0, mopY1));

            // — МОП: коридор (правая часть) —
            rooms.Add(MakeRoom($"mop_{index}_corr_r", "Коридор МОП",
                RoomType.CommonArea, elevX1, mopY0, maxX, mopY1));

            // Вертикальные перегородки для МОП
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(lobbyX0, mopY0), End = new Point2D(lobbyX0, mopY1)
            });
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(lobbyX1, mopY0), End = new Point2D(lobbyX1, mopY1)
            });
            partitions.Add(new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(elevX1, mopY0), End = new Point2D(elevX1, mopY1)
            });

            // ——— Квартиры: расположены выше и ниже полосы МОП ———
            // Нижняя зона: minY..mopY0
            // Верхняя зона: mopY1..maxY

            int nx = 2 + index % 3;  // 2, 3 или 4 секции по ширине
            double cellW = width / nx;

            // Варианты состава квартир
            var aptTypes = new[] { "Studio", "OneRoom", "TwoRoom", "ThreeRoom" };
            var aptNames = new[] { "Студия", "1К кв.", "2К кв.", "3К кв." };
            var aptDist = new Dictionary<string, int>();

            int aptIdx = 0;

            // Нижняя зона квартир
            double bottomH = mopY0 - minY;
            if (bottomH > 3.0)
            {
                for (int i = 0; i < nx; i++)
                {
                    double x0 = minX + i * cellW;
                    double x1 = x0 + cellW;
                    var typeIdx = (aptIdx + index) % aptTypes.Length;
                    var aptType = aptTypes[typeIdx];
                    var aptName = aptNames[typeIdx];

                    rooms.Add(MakeRoom($"apt_{index}_{aptIdx}", $"{aptName} {aptIdx + 1}",
                        RoomType.LivingRoom, x0, minY, x1, mopY0,
                        aptType));

                    aptDist.TryGetValue(aptType, out int cnt);
                    aptDist[aptType] = cnt + 1;
                    aptIdx++;

                    // Вертикальная перегородка между квартирами
                    if (i < nx - 1)
                    {
                        double sepX = x1 + (rng.NextDouble() - 0.5) * cellW * 0.1;
                        sepX = Math.Max(x1 - 0.5, Math.Min(x1 + 0.5, sepX));
                        partitions.Add(new ContourSegment
                        {
                            Type = SegmentType.Line,
                            Start = new Point2D(sepX, minY), End = new Point2D(sepX, mopY0)
                        });
                    }
                }
            }

            // Верхняя зона квартир
            double topH = maxY - mopY1;
            if (topH > 3.0)
            {
                for (int i = 0; i < nx; i++)
                {
                    double x0 = minX + i * cellW;
                    double x1 = x0 + cellW;
                    var typeIdx = (aptIdx + index + 1) % aptTypes.Length;
                    var aptType = aptTypes[typeIdx];
                    var aptName = aptNames[typeIdx];

                    rooms.Add(MakeRoom($"apt_{index}_{aptIdx}", $"{aptName} {aptIdx + 1}",
                        RoomType.LivingRoom, x0, mopY1, x1, maxY,
                        aptType));

                    aptDist.TryGetValue(aptType, out int cnt);
                    aptDist[aptType] = cnt + 1;
                    aptIdx++;

                    if (i < nx - 1)
                    {
                        double sepX = x1;
                        partitions.Add(new ContourSegment
                        {
                            Type = SegmentType.Line,
                            Start = new Point2D(sepX, mopY1), End = new Point2D(sepX, maxY)
                        });
                    }
                }
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

            int apartmentCount = rooms.Count(r => r.Type == RoomType.LivingRoom);

            double score = 55 + rng.Next(0, 40);

            return new LayoutVariant
            {
                Id = $"variant_{index}",
                Name = $"Вариант {index + 1} ({nx} секции, МОП {mopArea:F0}м²)",
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
        }

        // ═══════════════════════════════════════════
        //  Вспомогательные методы
        // ═══════════════════════════════════════════

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
