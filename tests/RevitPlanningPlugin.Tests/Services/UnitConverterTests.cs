using System;
using System.Collections.Generic;
using Xunit;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Geometry;

namespace RevitPlanningPlugin.Tests.Services
{
    public class UnitConverterTests
    {
        private const double Tol = 1e-9;

        // ——— MetersToRevit ———

        [Fact]
        public void MetersToRevit_Point_ConvertsToFeet()
        {
            var pt = new Point2D(1.0, 2.0);
            var result = UnitConverter.MetersToRevit(pt);
            Assert.Equal(1.0 / 0.3048, result.X, precision: 8);
            Assert.Equal(2.0 / 0.3048, result.Y, precision: 8);
        }

        [Fact]
        public void MetersToRevit_Scalar_ConvertsToFeet()
        {
            Assert.Equal(1.0 / 0.3048, UnitConverter.MetersToRevit(1.0), precision: 8);
        }

        // ——— ConvertToMeters ———

        [Theory]
        [InlineData("m",    1.0,    1.0)]
        [InlineData("mm",   1000.0, 1.0)]
        [InlineData("cm",   100.0,  1.0)]
        [InlineData("ft",   1.0,    0.3048)]
        [InlineData("in",   1.0,    0.0254)]
        public void ConvertToMeters_KnownUnits_ScalesCorrectly(string unit, double inputX, double expectedX)
        {
            var contour = new BuildingContour
            {
                SourceUnit = unit,
                OuterLoop = new List<ContourSegment>
                {
                    new()
                    {
                        Type = SegmentType.Line,
                        Start = new Point2D(0, 0),
                        End = new Point2D(inputX, 0)
                    }
                }
            };

            UnitConverter.ConvertToMeters(contour);

            Assert.Equal(expectedX, contour.OuterLoop[0].End.X, precision: 8);
            Assert.Equal("m", contour.SourceUnit);
        }

        [Fact]
        public void ConvertToMeters_AlreadyMeters_ReturnsSameObject()
        {
            var contour = new BuildingContour { SourceUnit = "m" };
            var result = UnitConverter.ConvertToMeters(contour);
            Assert.Same(contour, result);
        }

        [Fact]
        public void ConvertToMeters_UnknownUnit_ThrowsInvalidOperation()
        {
            var contour = new BuildingContour { SourceUnit = "parsec" };
            Assert.Throws<InvalidOperationException>(() => UnitConverter.ConvertToMeters(contour));
        }

        [Fact]
        public void ConvertToMeters_CaseInsensitive_Works()
        {
            var contour = new BuildingContour
            {
                SourceUnit = "MM",  // верхний регистр
                OuterLoop = new List<ContourSegment>
                {
                    new()
                    {
                        Type = SegmentType.Line,
                        Start = new Point2D(0, 0),
                        End = new Point2D(1000, 0)
                    }
                }
            };
            UnitConverter.ConvertToMeters(contour);
            Assert.Equal(1.0, contour.OuterLoop[0].End.X, precision: 8);
        }

        // ——— ScaleSegments — Ellipse (Bug #3 fix) ———

        [Fact]
        public void ConvertToMeters_EllipseSegment_ScalesCenter()
        {
            var contour = new BuildingContour
            {
                SourceUnit = "mm",
                OuterLoop = new List<ContourSegment>
                {
                    new()
                    {
                        Type = SegmentType.Ellipse,
                        Start = new Point2D(0, 0),
                        End = new Point2D(1000, 0),
                        EllipseCenter = new Point2D(500, 0),
                        EllipseRadiusX = 500.0,
                        EllipseRadiusY = 250.0
                    }
                }
            };

            UnitConverter.ConvertToMeters(contour);

            var seg = contour.OuterLoop[0];
            Assert.Equal(0.5, seg.EllipseCenter!.X, precision: 8);
            Assert.Equal(0.5, seg.EllipseRadiusX!.Value, precision: 8);
            Assert.Equal(0.25, seg.EllipseRadiusY!.Value, precision: 8);
        }

        // ——— SegmentsToRevitUnits — Ellipse (Bug #2 fix) ———

        [Fact]
        public void SegmentsToRevitUnits_EllipseSegment_PreservesAllFields()
        {
            var seg = new ContourSegment
            {
                Type = SegmentType.Ellipse,
                Start = new Point2D(1, 0),
                End = new Point2D(-1, 0),
                EllipseCenter = new Point2D(0, 0),
                EllipseRadiusX = 1.0,
                EllipseRadiusY = 0.5,
                EllipseRotation = Math.PI / 4,
                EllipseStartAngle = 0,
                EllipseEndAngle = Math.PI
            };

            var result = UnitConverter.SegmentsToRevitUnits(new List<ContourSegment> { seg });
            var r = result[0];

            double toFt = 1.0 / 0.3048;
            Assert.NotNull(r.EllipseCenter);
            Assert.Equal(0.0, r.EllipseCenter!.X, precision: 8);
            Assert.Equal(1.0 * toFt, r.EllipseRadiusX!.Value, precision: 8);
            Assert.Equal(0.5 * toFt, r.EllipseRadiusY!.Value, precision: 8);
            Assert.Equal(Math.PI / 4, r.EllipseRotation, precision: 10);   // углы не меняются
            Assert.Equal(0.0, r.EllipseStartAngle!.Value, precision: 10);
            Assert.Equal(Math.PI, r.EllipseEndAngle!.Value, precision: 10);
        }

        [Fact]
        public void SegmentsToRevitUnits_NurbsSegment_PreservesScalarParams()
        {
            var seg = new ContourSegment
            {
                Type = SegmentType.NurbsSpline,
                Start = new Point2D(0, 0),
                End = new Point2D(1, 0),
                NurbsDegree = 3,
                NurbsWeights = new List<double> { 1.0, 0.7071, 1.0 },
                NurbsKnots = new List<double> { 0, 0, 0, 1, 1, 1 }
            };

            var result = UnitConverter.SegmentsToRevitUnits(new List<ContourSegment> { seg });
            var r = result[0];

            Assert.Equal(3, r.NurbsDegree);
            Assert.Equal(seg.NurbsWeights, r.NurbsWeights);
            Assert.Equal(seg.NurbsKnots, r.NurbsKnots);
        }

        // ——— SegmentsToRevitUnits — Arc ———

        [Fact]
        public void SegmentsToRevitUnits_ArcSegment_ConvertsCenterAndRadius()
        {
            double toFt = 1.0 / 0.3048;
            var seg = new ContourSegment
            {
                Type = SegmentType.Arc,
                Start = new Point2D(1, 0),
                End = new Point2D(0, 1),
                ArcCenter = new Point2D(0, 0),
                ArcRadius = 1.0,
                ArcClockwise = false
            };

            var result = UnitConverter.SegmentsToRevitUnits(new List<ContourSegment> { seg });
            var r = result[0];

            Assert.Equal(toFt, r.ArcRadius!.Value, precision: 8);
            Assert.Equal(0.0, r.ArcCenter!.X, precision: 8);
            Assert.Equal(0.0, r.ArcCenter!.Y, precision: 8);
            Assert.False(r.ArcClockwise);
        }

        // ——— SegmentsToRevitUnits — Spline ———

        [Fact]
        public void SegmentsToRevitUnits_SplineSegment_ConvertsControlPoints()
        {
            double toFt = 1.0 / 0.3048;
            var seg = new ContourSegment
            {
                Type = SegmentType.Spline,
                Start = new Point2D(0, 0),
                End = new Point2D(1, 0),
                SplineControlPoints = new List<Point2D>
                {
                    new(0.5, 0.5), new(0.75, 0.25)
                }
            };

            var result = UnitConverter.SegmentsToRevitUnits(new List<ContourSegment> { seg });
            var r = result[0];

            Assert.Equal(0.5 * toFt, r.SplineControlPoints![0].X, precision: 8);
            Assert.Equal(0.25 * toFt, r.SplineControlPoints![1].Y, precision: 8);
        }

        // ——— Константы ———

        [Fact]
        public void Constants_MetersToFeet_FeetToMeters_Reciprocal()
        {
            Assert.Equal(1.0, UnitConverter.MetersToFeet * UnitConverter.FeetToMeters, precision: 10);
        }
    }
}
