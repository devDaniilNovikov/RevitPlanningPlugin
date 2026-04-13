using System;
using System.Collections.Generic;
using Xunit;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Geometry;

namespace RevitPlanningPlugin.Tests.Services
{
    public class ContourValidatorTests
    {
        private readonly ContourValidator _validator = new();

        // ——— Вспомогательные методы ———

        /// <summary>Строит замкнутый контур по массиву точек (CCW по умолчанию).</summary>
        private static BuildingContour MakeContour(Point2D[] pts, bool closeLoop = true)
        {
            var segs = new List<ContourSegment>();
            for (int i = 0; i < pts.Length; i++)
            {
                var next = closeLoop ? pts[(i + 1) % pts.Length] : pts[Math.Min(i + 1, pts.Length - 1)];
                segs.Add(new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = pts[i],
                    End = next
                });
            }
            return new BuildingContour { OuterLoop = segs };
        }

        /// <summary>Прямоугольник 20×10 м, CCW.</summary>
        private static BuildingContour ValidRect()
            => MakeContour(new[] {
                new Point2D(0, 0), new Point2D(20, 0),
                new Point2D(20, 10), new Point2D(0, 10)
            });

        // ——— Корректный контур ———

        [Fact]
        public void Validate_ValidRectangle_IsValid_NoErrors()
        {
            var result = _validator.Validate(ValidRect());
            Assert.True(result.IsValid);
            Assert.DoesNotContain(result.Issues, i => i.Severity == ValidationSeverity.Error);
        }

        // ——— Минимальное количество сегментов ———

        [Fact]
        public void Validate_TwoSegments_ReturnsError_MinSegments()
        {
            var contour = new BuildingContour
            {
                OuterLoop = new List<ContourSegment>
                {
                    new() { Start = new Point2D(0,0), End = new Point2D(1,0) },
                    new() { Start = new Point2D(1,0), End = new Point2D(0,0) }
                }
            };
            var result = _validator.Validate(contour);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "MIN_SEGMENTS");
        }

        [Fact]
        public void Validate_NullOuterLoop_ReturnsError()
        {
            var contour = new BuildingContour { OuterLoop = null! };
            var result = _validator.Validate(contour);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "MIN_SEGMENTS");
        }

        // ——— Замкнутость ———

        [Fact]
        public void Validate_OpenContour_ReturnsNotClosedError()
        {
            var contour = MakeContour(new[] {
                new Point2D(0, 0), new Point2D(20, 0),
                new Point2D(20, 10), new Point2D(0, 10)
            });
            // Смещаем конец последнего сегмента, чтобы создать разрыв
            contour.OuterLoop[^1].End = new Point2D(5, 5);

            var result = _validator.Validate(contour);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "NOT_CLOSED");
        }

        [Fact]
        public void Validate_DiscontinuousSegments_ReturnsDiscontinuityError()
        {
            // Вставляем разрыв между seg[0].End и seg[1].Start
            var contour = ValidRect();
            contour.OuterLoop[1].Start = new Point2D(999, 999); // разрыв

            var result = _validator.Validate(contour);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "DISCONTINUITY");
        }

        // ——— Площадь ———

        [Fact]
        public void Validate_AreaTooSmall_ReturnsAreaError()
        {
            // Прямоугольник 0.5×0.5 = 0.25 м² < 1 м²
            var contour = MakeContour(new[] {
                new Point2D(0, 0), new Point2D(0.5, 0),
                new Point2D(0.5, 0.5), new Point2D(0, 0.5)
            });
            var result = _validator.Validate(contour);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "AREA_TOO_SMALL");
        }

        [Fact]
        public void Validate_AreaTooLarge_ReturnsWarning()
        {
            // 1500×1500 = 2 250 000 м² > 1 000 000
            double s = 1500;
            var contour = MakeContour(new[] {
                new Point2D(0, 0), new Point2D(s, 0),
                new Point2D(s, s), new Point2D(0, s)
            });
            var result = _validator.Validate(contour);
            Assert.True(result.IsValid);  // только предупреждение, не ошибка
            Assert.Contains(result.Issues, i => i.Code == "AREA_TOO_LARGE");
        }

        // ——— Ориентация (BUG #1 проверка) ———

        [Fact]
        public void Validate_CCWOrientation_NoOrientationWarning()
        {
            // CCW: (0,0) → (20,0) → (20,10) → (0,10) — корректная ориентация
            var result = _validator.Validate(ValidRect());
            Assert.DoesNotContain(result.Issues, i => i.Code == "CW_ORIENTATION");
        }

        [Fact]
        public void Validate_CWOrientation_ReturnsWarning()
        {
            // CW: обратный обход прямоугольника
            var contour = MakeContour(new[] {
                new Point2D(0, 10), new Point2D(20, 10),
                new Point2D(20, 0), new Point2D(0, 0)
            });
            var result = _validator.Validate(contour);
            Assert.True(result.IsValid);  // только предупреждение
            Assert.Contains(result.Issues, i => i.Code == "CW_ORIENTATION");
        }

        // ——— Самопересечения ———

        [Fact]
        public void Validate_SelfIntersecting_ReturnsError()
        {
            // Бабочка / цифра 8
            var contour = MakeContour(new[] {
                new Point2D(0, 0), new Point2D(10, 10),
                new Point2D(10, 0), new Point2D(0, 10)
            });
            var result = _validator.Validate(contour);
            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "SELF_INTERSECTION");
        }

        [Fact]
        public void Validate_NonSelfIntersecting_NoIntersectionError()
        {
            var result = _validator.Validate(ValidRect());
            Assert.DoesNotContain(result.Issues, i => i.Code == "SELF_INTERSECTION");
        }

        // ——— Внутренние контуры ———

        [Fact]
        public void Validate_InnerLoopTwoSegments_ReturnsInnerWarning()
        {
            var contour = ValidRect();
            contour.InnerLoops.Add(new List<ContourSegment>
            {
                new() { Start = new Point2D(1,1), End = new Point2D(2,2) },
                new() { Start = new Point2D(2,2), End = new Point2D(1,1) }
            });
            var result = _validator.Validate(contour);
            Assert.Contains(result.Issues, i => i.Code == "INNER_MIN_SEGMENTS");
        }

        [Fact]
        public void Validate_ValidInnerLoop_NoInnerWarning()
        {
            var contour = ValidRect();
            contour.InnerLoops.Add(new List<ContourSegment>
            {
                new() { Start = new Point2D(5,3), End = new Point2D(10,3) },
                new() { Start = new Point2D(10,3), End = new Point2D(10,7) },
                new() { Start = new Point2D(10,7), End = new Point2D(5,3) }
            });
            var result = _validator.Validate(contour);
            Assert.DoesNotContain(result.Issues, i => i.Code == "INNER_MIN_SEGMENTS");
        }
    }
}
