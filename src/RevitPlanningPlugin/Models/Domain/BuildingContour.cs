using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Контур здания (внешний периметр + опциональные внутренние отверстия).
    /// Все координаты хранятся в метрах.
    /// </summary>
    public class BuildingContour
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        /// <summary>Внешний замкнутый контур.</summary>
        public List<ContourSegment> OuterLoop { get; set; } = new();

        /// <summary>Внутренние вырезы (дворы, шахты и т.п.).</summary>
        public List<List<ContourSegment>> InnerLoops { get; set; } = new();

        /// <summary>Метаданные из API.</summary>
        public Dictionary<string, string> Metadata { get; set; } = new();

        /// <summary>Единица измерения координат, полученная из API.</summary>
        public string SourceUnit { get; set; } = "m";

        /// <summary>Грубая оценка площади по формуле Шёлейса (Shoelace) для полигона.</summary>
        public double ApproximateArea
        {
            get
            {
                var outerArea = ComputePolygonArea(GetOuterVertices());
                var innerArea = InnerLoops.Sum(loop => ComputePolygonArea(loop.Select(s => s.Start).ToList()));
                return Math.Max(0, outerArea - innerArea);
            }
        }

        public List<Point2D> GetOuterVertices()
        {
            var vertices = new List<Point2D>();
            foreach (var seg in OuterLoop)
            {
                vertices.Add(seg.Start);
            }
            return vertices;
        }

        public List<List<Point2D>> GetInnerVertices()
        {
            return InnerLoops
                .Select(loop => loop.Select(seg => seg.Start).ToList())
                .ToList();
        }

        private static double ComputePolygonArea(List<Point2D> pts)
        {
            if (pts.Count < 3) return 0;

            double area = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var j = (i + 1) % pts.Count;
                area += pts[i].X * pts[j].Y;
                area -= pts[j].X * pts[i].Y;
            }
            return Math.Abs(area) / 2.0;
        }

        /// <summary>Проверка замкнутости внешнего контура.</summary>
        public bool IsClosed
        {
            get
            {
                if (OuterLoop.Count == 0) return false;
                var first = OuterLoop.First().Start;
                var last = OuterLoop.Last().End;
                return first.Equals(last);
            }
        }

        /// <summary>Содержит ли контур криволинейные сегменты (неортогональный / органичный).</summary>
        public bool HasCurvedGeometry => OuterLoop.Any(s => s.IsCurved) 
            || InnerLoops.Any(loop => loop.Any(s => s.IsCurved));

        /// <summary>Типы кривых, используемые в контуре.</summary>
        public string GeometryDescription
        {
            get
            {
                var types = OuterLoop.Select(s => s.Type).Distinct().OrderBy(t => t);
                var desc = string.Join(", ", types);
                return HasCurvedGeometry ? $"Неортогональный ({desc})" : $"Ортогональный ({desc})";
            }
        }

        /// <summary>
        /// История генераций для этого контура.
        /// Ключ — timestamp, значение — список вариантов.
        /// Позволяет получить все сгенерированные планы по одному контуру.
        /// </summary>
        [JsonIgnore]
        public Dictionary<DateTime, List<LayoutVariant>> GenerationHistory { get; } = new();

        /// <summary>Общее количество сгенерированных вариантов по этому контуру.</summary>
        public int TotalGeneratedVariants => GenerationHistory.Values.Sum(v => v.Count);

        /// <summary>Добавить результат генерации в историю.</summary>
        public void AddGenerationResult(List<LayoutVariant> variants)
        {
            GenerationHistory[DateTime.Now] = variants;
        }

        /// <summary>Получить все варианты, когда-либо сгенерированные для этого контура.</summary>
        public List<LayoutVariant> GetAllVariants()
        {
            return GenerationHistory.Values.SelectMany(v => v).ToList();
        }
    }
}
