using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Tests.Domain
{
    public class BuildingContourTests
    {
        // ——— Вспомогательный метод: прямоугольник 10×5 (CCW) ———
        private static BuildingContour MakeRect(double w = 10, double h = 5)
        {
            var pts = new[] {
                new Point2D(0, 0), new Point2D(w, 0),
                new Point2D(w, h), new Point2D(0, h)
            };
            return new BuildingContour
            {
                Id = "test",
                SourceUnit = "m",
                OuterLoop = BuildLoop(pts)
            };
        }

        private static List<ContourSegment> BuildLoop(Point2D[] pts)
        {
            var segs = new List<ContourSegment>();
            for (int i = 0; i < pts.Length; i++)
                segs.Add(new ContourSegment
                {
                    Type = SegmentType.Line,
                    Start = pts[i],
                    End = pts[(i + 1) % pts.Length]
                });
            return segs;
        }

        // ——— ApproximateArea ———

        [Fact]
        public void ApproximateArea_Rectangle_Correct()
        {
            var contour = MakeRect(10, 5);
            Assert.Equal(50.0, contour.ApproximateArea, precision: 6);
        }

        [Fact]
        public void ApproximateArea_EmptyOuterLoop_ReturnsZero()
        {
            var contour = new BuildingContour();
            Assert.Equal(0.0, contour.ApproximateArea);
        }

        [Fact]
        public void ApproximateArea_TwoPoints_ReturnsZero()
        {
            var contour = new BuildingContour
            {
                OuterLoop = new List<ContourSegment>
                {
                    new() { Start = new Point2D(0,0), End = new Point2D(1,0) },
                    new() { Start = new Point2D(1,0), End = new Point2D(0,0) }
                }
            };
            Assert.Equal(0.0, contour.ApproximateArea, precision: 10);
        }

        [Fact]
        public void ApproximateArea_InnerLoop_SubtractsVoidArea()
        {
            var contour = MakeRect(10, 10);
            contour.InnerLoops.Add(BuildLoop(new[] {
                new Point2D(3, 3), new Point2D(7, 3),
                new Point2D(7, 7), new Point2D(3, 7)
            }));

            Assert.Equal(84.0, contour.ApproximateArea, precision: 6);
        }

        // ——— IsClosed ———

        [Fact]
        public void IsClosed_ClosedLoop_ReturnsTrue()
        {
            var contour = MakeRect();
            Assert.True(contour.IsClosed);
        }

        [Fact]
        public void IsClosed_OpenLoop_ReturnsFalse()
        {
            var contour = new BuildingContour
            {
                OuterLoop = new List<ContourSegment>
                {
                    new() { Start = new Point2D(0,0), End = new Point2D(1,0) },
                    new() { Start = new Point2D(1,0), End = new Point2D(1,1) }
                    // End (1,1) ≠ Start (0,0)
                }
            };
            Assert.False(contour.IsClosed);
        }

        [Fact]
        public void IsClosed_EmptyLoop_ReturnsFalse()
        {
            var contour = new BuildingContour();
            Assert.False(contour.IsClosed);
        }

        // ——— HasCurvedGeometry ———

        [Fact]
        public void HasCurvedGeometry_AllLines_ReturnsFalse()
        {
            var contour = MakeRect();
            Assert.False(contour.HasCurvedGeometry);
        }

        [Fact]
        public void HasCurvedGeometry_ContainsArc_ReturnsTrue()
        {
            var contour = MakeRect();
            contour.OuterLoop[0].Type = SegmentType.Arc;
            Assert.True(contour.HasCurvedGeometry);
        }

        [Fact]
        public void HasCurvedGeometry_InnerLoopHasArc_ReturnsTrue()
        {
            var contour = MakeRect();
            var inner = BuildLoop(new[] {
                new Point2D(2,1), new Point2D(4,1),
                new Point2D(4,3), new Point2D(2,3)
            });
            inner[0].Type = SegmentType.Arc;
            contour.InnerLoops.Add(inner);
            Assert.True(contour.HasCurvedGeometry);
        }

        // ——— GetOuterVertices ———

        [Fact]
        public void GetOuterVertices_ReturnsStartOfEachSegment()
        {
            var contour = MakeRect(10, 5);
            var verts = contour.GetOuterVertices();
            Assert.Equal(4, verts.Count);
            Assert.Equal(new Point2D(0, 0), verts[0]);
            Assert.Equal(new Point2D(10, 0), verts[1]);
            Assert.Equal(new Point2D(10, 5), verts[2]);
            Assert.Equal(new Point2D(0, 5), verts[3]);
        }

        // ——— GenerationHistory / AddGenerationResult ———

        [Fact]
        public void AddGenerationResult_StoresVariants()
        {
            var contour = MakeRect();
            var variants = new List<LayoutVariant>
            {
                new() { Id = "v1" }, new() { Id = "v2" }
            };
            contour.AddGenerationResult(variants);
            Assert.Equal(2, contour.TotalGeneratedVariants);
        }

        [Fact]
        public void AddGenerationResult_MultipleTimes_HistoryGrows()
        {
            var contour = MakeRect();
            contour.AddGenerationResult(new List<LayoutVariant> { new() { Id = "v1" } });
            System.Threading.Thread.Sleep(10); // разные ключи DateTime
            contour.AddGenerationResult(new List<LayoutVariant> { new() { Id = "v2" }, new() { Id = "v3" } });
            Assert.Equal(3, contour.TotalGeneratedVariants);
        }

        [Fact]
        public void GetAllVariants_ReturnsAllAcrossRuns()
        {
            var contour = MakeRect();
            contour.AddGenerationResult(new List<LayoutVariant> { new() { Id = "a" } });
            System.Threading.Thread.Sleep(10);
            contour.AddGenerationResult(new List<LayoutVariant> { new() { Id = "b" }, new() { Id = "c" } });
            var all = contour.GetAllVariants();
            Assert.Equal(3, all.Count);
            Assert.Contains(all, v => v.Id == "a");
            Assert.Contains(all, v => v.Id == "c");
        }
    }
}
