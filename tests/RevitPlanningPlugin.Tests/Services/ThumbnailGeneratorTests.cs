using System.Collections.Generic;
using Xunit;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Geometry;

namespace RevitPlanningPlugin.Tests.Services
{
    public class ThumbnailGeneratorTests
    {
        private static LayoutVariant MakeVariant(double score = 75)
        {
            return new LayoutVariant
            {
                Id = "v1",
                EfficiencyScore = score,
                Rooms = new List<RoomLayout>
                {
                    new()
                    {
                        Id = "r1", Name = "Студия", Type = RoomType.LivingRoom, Area = 30,
                        LabelPoint = new Point2D(5, 5),
                        Boundary = new List<ContourSegment>
                        {
                            new() { Type = SegmentType.Line, Start = new Point2D(0,0), End = new Point2D(10,0) },
                            new() { Type = SegmentType.Line, Start = new Point2D(10,0), End = new Point2D(10,6) },
                            new() { Type = SegmentType.Line, Start = new Point2D(10,6), End = new Point2D(0,6) },
                            new() { Type = SegmentType.Line, Start = new Point2D(0,6), End = new Point2D(0,0) }
                        }
                    }
                },
                Partitions = new List<ContourSegment>
                {
                    new() { Type = SegmentType.Line, Start = new Point2D(0,0), End = new Point2D(10,0) }
                }
            };
        }

        // ——— GenerateSvg ———

        [Fact]
        public void GenerateSvg_NonEmptyVariant_ReturnsSvgString()
        {
            var svg = ThumbnailGenerator.GenerateSvg(MakeVariant());
            Assert.False(string.IsNullOrWhiteSpace(svg));
            Assert.StartsWith("<svg", svg.TrimStart());
        }

        [Fact]
        public void GenerateSvg_ContainsScoreBadge()
        {
            var svg = ThumbnailGenerator.GenerateSvg(MakeVariant(score: 88));
            Assert.Contains("88", svg);
        }

        [Fact]
        public void GenerateSvg_EmptyRoomsAndPartitions_ReturnsEmpty()
        {
            var v = new LayoutVariant { Rooms = new(), Partitions = new() };
            var svg = ThumbnailGenerator.GenerateSvg(v);
            Assert.Equal(string.Empty, svg);
        }

        [Fact]
        public void GenerateSvg_WithContour_ContainsContourPath()
        {
            var variant = MakeVariant();
            var contour = new BuildingContour
            {
                OuterLoop = new List<ContourSegment>
                {
                    new() { Type = SegmentType.Line, Start = new Point2D(0,0), End = new Point2D(30,0) },
                    new() { Type = SegmentType.Line, Start = new Point2D(30,0), End = new Point2D(30,20) },
                    new() { Type = SegmentType.Line, Start = new Point2D(30,20), End = new Point2D(0,20) },
                    new() { Type = SegmentType.Line, Start = new Point2D(0,20), End = new Point2D(0,0) }
                }
            };

            var svg = ThumbnailGenerator.GenerateSvg(variant, contour);
            // Контур рисуется как <path fill='none' stroke='#333'...>
            Assert.Contains("#333", svg);
        }

        [Fact]
        public void GenerateSvg_RoomWithUnsafeCharsInName_HtmlEncoded()
        {
            // Bug #5: имена с спецсимволами XML/HTML должны быть экранированы
            var variant = new LayoutVariant
            {
                Id = "v1",
                EfficiencyScore = 70,
                Rooms = new List<RoomLayout>
                {
                    new()
                    {
                        Id = "r1",
                        Name = "<script>",   // XSS-вектор
                        Type = RoomType.LivingRoom,
                        LabelPoint = new Point2D(5, 5),
                        Boundary = new List<ContourSegment>
                        {
                            new() { Type = SegmentType.Line, Start = new Point2D(0,0), End = new Point2D(10,0) },
                            new() { Type = SegmentType.Line, Start = new Point2D(10,0), End = new Point2D(10,6) },
                            new() { Type = SegmentType.Line, Start = new Point2D(10,6), End = new Point2D(0,0) }
                        }
                    }
                }
            };

            var svg = ThumbnailGenerator.GenerateSvg(variant);
            // Сырой <script> не должен присутствовать в SVG
            Assert.DoesNotContain("<script>", svg);
            // Должно быть экранировано
            Assert.Contains("&lt;script&gt;", svg);
        }

        [Fact]
        public void GenerateSvg_RoomNameWithAmpersand_HtmlEncoded()
        {
            var variant = new LayoutVariant
            {
                Id = "v1",
                EfficiencyScore = 70,
                Rooms = new List<RoomLayout>
                {
                    new()
                    {
                        Id = "r1",
                        Name = "A&B",
                        Type = RoomType.LivingRoom,
                        LabelPoint = new Point2D(5, 5),
                        Boundary = new List<ContourSegment>
                        {
                            new() { Type = SegmentType.Line, Start = new Point2D(0,0), End = new Point2D(10,0) },
                            new() { Type = SegmentType.Line, Start = new Point2D(10,0), End = new Point2D(10,6) },
                            new() { Type = SegmentType.Line, Start = new Point2D(10,6), End = new Point2D(0,0) }
                        }
                    }
                }
            };

            var svg = ThumbnailGenerator.GenerateSvg(variant);
            Assert.Contains("&amp;", svg);
            Assert.DoesNotContain(">A&B<", svg);
        }

        // ——— GenerateThumbnails ———

        [Fact]
        public void GenerateThumbnails_SetsThumbnailSvgOnAllVariants()
        {
            var variants = new List<LayoutVariant> { MakeVariant(60), MakeVariant(70), MakeVariant(80) };
            ThumbnailGenerator.GenerateThumbnails(variants);
            Assert.All(variants, v => Assert.False(string.IsNullOrWhiteSpace(v.ThumbnailSvg)));
        }

        [Fact]
        public void GenerateThumbnails_EmptyList_NoException()
        {
            ThumbnailGenerator.GenerateThumbnails(new List<LayoutVariant>());
        }
    }
}
