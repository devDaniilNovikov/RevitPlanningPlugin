using System;
using System.Collections.Generic;
using Xunit;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Tests.Domain
{
    public class ContourSegmentTests
    {
        // ——— IsCurved ———

        [Fact]
        public void IsCurved_Line_ReturnsFalse()
        {
            var seg = new ContourSegment { Type = SegmentType.Line };
            Assert.False(seg.IsCurved);
        }

        [Theory]
        [InlineData(SegmentType.Arc)]
        [InlineData(SegmentType.Spline)]
        [InlineData(SegmentType.Ellipse)]
        [InlineData(SegmentType.NurbsSpline)]
        public void IsCurved_CurvedTypes_ReturnsTrue(SegmentType type)
        {
            var seg = new ContourSegment { Type = type };
            Assert.True(seg.IsCurved);
        }

        // ——— Length: Line ———

        [Fact]
        public void Length_Line_ReturnsEuclideanDistance()
        {
            var seg = new ContourSegment
            {
                Type = SegmentType.Line,
                Start = new Point2D(0, 0),
                End = new Point2D(3, 4)
            };
            Assert.Equal(5.0, seg.Length, precision: 10);
        }

        // ——— Length: Arc (четверть окружности, CCW) ———

        [Fact]
        public void Length_Arc_QuarterCircle_CCW_Correct()
        {
            // Четверть круга радиуса 1 (CCW): от 0° до 90°
            var seg = new ContourSegment
            {
                Type = SegmentType.Arc,
                Start = new Point2D(1, 0),
                End = new Point2D(0, 1),
                ArcCenter = new Point2D(0, 0),
                ArcRadius = 1.0,
                ArcClockwise = false
            };
            // Длина четверти окружности R=1: π/2 ≈ 1.5708
            Assert.Equal(Math.PI / 2, seg.Length, precision: 6);
        }

        [Fact]
        public void Length_Arc_QuarterCircle_CW_Correct()
        {
            // Четверть круга радиуса 1 (CW): от 90° до 0° (по часовой)
            var seg = new ContourSegment
            {
                Type = SegmentType.Arc,
                Start = new Point2D(0, 1),
                End = new Point2D(1, 0),
                ArcCenter = new Point2D(0, 0),
                ArcRadius = 1.0,
                ArcClockwise = true
            };
            Assert.Equal(Math.PI / 2, seg.Length, precision: 6);
        }

        [Fact]
        public void Length_Arc_ThreeQuarterCircle_CCW_Correct()
        {
            // 270° дуга (CCW): от 0° до 270° = 3π/2 ≈ 4.7124
            // Start = (1,0), End = (0,-1), Center = (0,0)
            var seg = new ContourSegment
            {
                Type = SegmentType.Arc,
                Start = new Point2D(1, 0),
                End = new Point2D(0, -1),
                ArcCenter = new Point2D(0, 0),
                ArcRadius = 1.0,
                ArcClockwise = false   // CCW → 270°
            };
            Assert.Equal(3 * Math.PI / 2, seg.Length, precision: 6);
        }

        [Fact]
        public void Length_Arc_NoCenter_FallsBackToChordLength()
        {
            var seg = new ContourSegment
            {
                Type = SegmentType.Arc,
                Start = new Point2D(0, 0),
                End = new Point2D(3, 4),
                ArcCenter = null
            };
            Assert.Equal(5.0, seg.Length, precision: 10);
        }

        // ——— Length: Spline ———

        [Fact]
        public void Length_Spline_ReturnsPolylineApproximation()
        {
            var pts = new List<Point2D>
            {
                new(0, 0), new(1, 0), new(2, 0), new(3, 0)
            };
            var seg = new ContourSegment
            {
                Type = SegmentType.Spline,
                Start = new Point2D(0, 0),
                End = new Point2D(3, 0),
                SplineControlPoints = pts
            };
            // 3 отрезка по 1 единице = длина 3
            Assert.Equal(3.0, seg.Length, precision: 10);
        }

        [Fact]
        public void Length_Spline_NoControlPoints_FallsBackToChord()
        {
            var seg = new ContourSegment
            {
                Type = SegmentType.Spline,
                Start = new Point2D(0, 0),
                End = new Point2D(3, 4)
            };
            Assert.Equal(5.0, seg.Length, precision: 10);
        }

        // ——— Length: Ellipse ———

        [Fact]
        public void Length_Ellipse_FullCircle_ReturnsCorrectApproximation()
        {
            // Окружность: a == b == R, Рамануджан ≈ 2πR
            double R = 5.0;
            var seg = new ContourSegment
            {
                Type = SegmentType.Ellipse,
                Start = new Point2D(R, 0),
                End = new Point2D(R, 0),
                EllipseRadiusX = R,
                EllipseRadiusY = R,
                EllipseStartAngle = 0,
                EllipseEndAngle = 2 * Math.PI
            };
            // Для круга a==b: Рамануджан → π*(3*(a+b) - sqrt((3a+b)(a+3b))) = π*(6R - sqrt(16R²)) = π*(6R-4R) = 2πR
            Assert.Equal(2 * Math.PI * R, seg.Length, precision: 6);
        }

        // ——— Прочее ———

        [Fact]
        public void Length_NurbsSpline_NoControlPoints_FallsBackToChord()
        {
            var seg = new ContourSegment
            {
                Type = SegmentType.NurbsSpline,
                Start = new Point2D(0, 0),
                End = new Point2D(3, 4)
            };
            Assert.Equal(5.0, seg.Length, precision: 10);
        }
    }
}
