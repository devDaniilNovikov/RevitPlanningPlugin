using System;
using System.Collections.Generic;
using System.Linq;

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
                var pts = GetOuterVertices();
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
    }
}
