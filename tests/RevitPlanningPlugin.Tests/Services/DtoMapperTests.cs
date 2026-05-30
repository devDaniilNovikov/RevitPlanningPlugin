using System.Collections.Generic;
using Xunit;
using RevitPlanningPlugin.Models.Api;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Api;

namespace RevitPlanningPlugin.Tests.Services
{
    public class DtoMapperTests
    {
        // ——— ToDomain(ApiContourDto) ———

        [Fact]
        public void ToDomain_Contour_MapsAllFields()
        {
            var dto = new ApiContourDto
            {
                Id = "c1",
                Name = "Контур 1",
                Description = "Описание",
                Unit = "mm",
                OuterLoop = new List<ApiSegmentDto>
                {
                    new() { Type = "line", Start = new ApiPointDto { X = 0, Y = 0 }, End = new ApiPointDto { X = 1, Y = 0 } },
                    new() { Type = "line", Start = new ApiPointDto { X = 1, Y = 0 }, End = new ApiPointDto { X = 1, Y = 1 } },
                    new() { Type = "line", Start = new ApiPointDto { X = 1, Y = 1 }, End = new ApiPointDto { X = 0, Y = 0 } }
                },
                Metadata = new Dictionary<string, string> { ["key"] = "value" }
            };

            var contour = DtoMapper.ToDomain(dto);

            Assert.Equal("c1", contour.Id);
            Assert.Equal("Контур 1", contour.Name);
            Assert.Equal("mm", contour.SourceUnit);
            Assert.Equal(3, contour.OuterLoop.Count);
            Assert.Equal("value", contour.Metadata["key"]);
        }

        [Fact]
        public void ToDomain_Contour_NullInnerLoops_ReturnsEmptyList()
        {
            var dto = new ApiContourDto { InnerLoops = null };
            var contour = DtoMapper.ToDomain(dto);
            Assert.NotNull(contour.InnerLoops);
            Assert.Empty(contour.InnerLoops);
        }

        [Fact]
        public void ToDomain_Contour_NullUnit_DefaultsToMeters()
        {
            var dto = new ApiContourDto { Unit = null! };
            var contour = DtoMapper.ToDomain(dto);
            Assert.Equal("m", contour.SourceUnit);
        }

        // ——— MapSegment: типы сегментов ———

        [Theory]
        [InlineData("line", SegmentType.Line)]
        [InlineData("LINE", SegmentType.Line)]
        [InlineData("arc", SegmentType.Arc)]
        [InlineData("ARC", SegmentType.Arc)]
        [InlineData("spline", SegmentType.Spline)]
        [InlineData("ellipse", SegmentType.Ellipse)]
        [InlineData("nurbs", SegmentType.NurbsSpline)]
        [InlineData("nurbsspline", SegmentType.NurbsSpline)]
        [InlineData("nurbs_spline", SegmentType.NurbsSpline)]
        [InlineData("unknown_type", SegmentType.Line)]  // неизвестный → Line
        [InlineData(null, SegmentType.Line)]             // null → Line
        public void MapSegment_TypeString_MapsCorrectly(string? typeStr, SegmentType expected)
        {
            // Используем ToDto для косвенной проверки через полный маппинг контура
            var dto = new ApiContourDto
            {
                OuterLoop = new List<ApiSegmentDto>
                {
                    new()
                    {
                        Type = typeStr!,
                        Start = new ApiPointDto { X = 0, Y = 0 },
                        End = new ApiPointDto { X = 1, Y = 0 }
                    }
                }
            };
            var contour = DtoMapper.ToDomain(dto);
            Assert.Equal(expected, contour.OuterLoop[0].Type);
        }

        [Fact]
        public void MapSegment_Arc_MapsAllArcFields()
        {
            var dto = new ApiContourDto
            {
                OuterLoop = new List<ApiSegmentDto>
                {
                    new()
                    {
                        Type = "arc",
                        Start = new ApiPointDto { X = 1, Y = 0 },
                        End = new ApiPointDto { X = 0, Y = 1 },
                        Center = new ApiPointDto { X = 0, Y = 0 },
                        Radius = 1.0,
                        Clockwise = false
                    }
                }
            };

            var contour = DtoMapper.ToDomain(dto);
            var seg = contour.OuterLoop[0];

            Assert.Equal(SegmentType.Arc, seg.Type);
            Assert.NotNull(seg.ArcCenter);
            Assert.Equal(0.0, seg.ArcCenter!.X);
            Assert.Equal(1.0, seg.ArcRadius);
            Assert.False(seg.ArcClockwise);
        }

        [Fact]
        public void MapSegment_Spline_MapsControlPoints()
        {
            var dto = new ApiContourDto
            {
                OuterLoop = new List<ApiSegmentDto>
                {
                    new()
                    {
                        Type = "spline",
                        Start = new ApiPointDto { X = 0, Y = 0 },
                        End = new ApiPointDto { X = 3, Y = 0 },
                        ControlPoints = new List<ApiPointDto>
                        {
                            new() { X = 1, Y = 1 }, new() { X = 2, Y = 1 }
                        }
                    }
                }
            };

            var contour = DtoMapper.ToDomain(dto);
            var seg = contour.OuterLoop[0];

            Assert.Equal(2, seg.SplineControlPoints!.Count);
            Assert.Equal(1.0, seg.SplineControlPoints[0].X);
        }

        // ——— ParseRoomType ———

        [Theory]
        [InlineData("commonarea",  RoomType.CommonArea)]
        [InlineData("common_area", RoomType.CommonArea)]
        [InlineData("mop",         RoomType.CommonArea)]
        [InlineData("lobby",       RoomType.Lobby)]
        [InlineData("elevator",    RoomType.Elevator)]
        [InlineData("corridor",    RoomType.Corridor)]
        [InlineData("staircase",   RoomType.Staircase)]
        [InlineData("livingroom",  RoomType.LivingRoom)]
        [InlineData("living_room", RoomType.LivingRoom)]
        [InlineData("bedroom",     RoomType.Bedroom)]
        [InlineData("kitchen",     RoomType.Kitchen)]
        [InlineData("bathroom",    RoomType.Bathroom)]
        [InlineData("storage",     RoomType.Storage)]
        [InlineData("office",      RoomType.Office)]
        [InlineData("balcony",     RoomType.Balcony)]
        [InlineData("unknown",     RoomType.Other)]
        [InlineData("",            RoomType.Other)]
        [InlineData(null,          RoomType.Other)]
        public void ParseRoomType_VariousStrings_Correct(string? typeStr, RoomType expected)
        {
            // Маппим через ApiRoomDto, т.к. ParseRoomType — private через MapRoom
            var dto = new ApiLayoutVariantDto
            {
                Rooms = new List<ApiRoomDto>
                {
                    new()
                    {
                        Id = "r1", Name = "R", Type = typeStr!, Area = 10,
                        Boundary = new List<ApiSegmentDto>()
                    }
                }
            };

            var variant = DtoMapper.ToDomain(dto);
            Assert.Equal(expected, variant.Rooms[0].Type);
        }

        // ——— ToDomain(ApiLayoutVariantDto) ———

        [Fact]
        public void ToDomain_Variant_MapsMetrics()
        {
            var dto = new ApiLayoutVariantDto
            {
                Id = "v1",
                Name = "Вариант 1",
                VariantIndex = 1,
                TotalArea = 500,
                UsableArea = 400,
                MopArea = 60,
                CorridorArea = 30,
                RoomCount = 12,
                ApartmentCount = 10,
                EfficiencyScore = 78.5,
                ApartmentTypeDistribution = new Dictionary<string, int> { ["OneRoom"] = 6, ["TwoRoom"] = 4 }
            };

            var variant = DtoMapper.ToDomain(dto);

            Assert.Equal("v1", variant.Id);
            Assert.Equal(500, variant.TotalArea);
            Assert.Equal(400, variant.UsableArea);
            Assert.Equal(60, variant.MopArea);
            Assert.Equal(78.5, variant.EfficiencyScore);
            Assert.Equal(6, variant.ApartmentTypeDistribution["OneRoom"]);
        }

        [Fact]
        public void ToDomain_Variant_NullApartmentDistribution_ReturnsEmptyDict()
        {
            var dto = new ApiLayoutVariantDto { ApartmentTypeDistribution = null };
            var variant = DtoMapper.ToDomain(dto);
            Assert.NotNull(variant.ApartmentTypeDistribution);
            Assert.Empty(variant.ApartmentTypeDistribution);
        }

        // ——— ToDto(GenerationParameters) ———

        [Fact]
        public void ToDto_MapsAllFields()
        {
            var p = new GenerationParameters
            {
                VariantCount = 5,
                GenerationType = GenerationType.MixedUse,
                ValidationMode = ValidationMode.Strict,
                TextPrompt = "Сохранить компактные МОП.",
                StudioCount = 2,
                OneRoomCount = 4,
                MinApartmentArea = 30,
                MaxApartmentArea = 100,
                MopAreaTarget = 50,
                MinCorridorWidth = 1.8,
                OptimizationPriority = "area"
            };

            var dto = DtoMapper.ToDto("contour-1", p);

            Assert.Equal("contour-1", dto.ContourId);
            Assert.Equal(5, dto.VariantCount);
            Assert.Equal("MixedUse", dto.GenerationType);
            Assert.Equal("Strict", dto.ValidationMode);
            Assert.Equal("Сохранить компактные МОП.", dto.TextPrompt);
            Assert.Equal(2, dto.ApartmentTypes!["Studio"]);
            Assert.Equal(30.0, dto.MinApartmentArea);
            Assert.Equal(100.0, dto.MaxApartmentArea);
            Assert.Equal(50.0, dto.MopAreaTarget);
            Assert.Equal(1.8, dto.MinCorridorWidth);
            Assert.Equal("area", dto.OptimizationPriority);
        }

        [Fact]
        public void ToDto_AllZeroApartmentCounts_NullApartmentTypes()
        {
            var p = new GenerationParameters
            {
                StudioCount = 0, OneRoomCount = 0, TwoRoomCount = 0,
                ThreeRoomCount = 0, FourRoomCount = 0
            };

            var dto = DtoMapper.ToDto("c1", p);
            Assert.Null(dto.ApartmentTypes);
        }

        [Fact]
        public void ToDto_ZeroOptionalValues_OmitsThem()
        {
            var p = new GenerationParameters
            {
                MinApartmentArea = 0,
                MaxApartmentArea = 0,
                MopAreaTarget = 0,
                MinCorridorWidth = 0
            };

            var dto = DtoMapper.ToDto("c1", p);

            Assert.Null(dto.MinApartmentArea);
            Assert.Null(dto.MaxApartmentArea);
            Assert.Null(dto.MopAreaTarget);
            Assert.Null(dto.MinCorridorWidth);
        }

        [Fact]
        public void ToDto_RequestContext_MapsPromptAndRevitContext()
        {
            var context = new GenerationRequestContext
            {
                RequestId = "req-1",
                Prompt = "Полный prompt",
                Contour = new BuildingContour
                {
                    Id = "c1",
                    Name = "Контур",
                    SourceUnit = "m",
                    OuterLoop = new List<ContourSegment>
                    {
                        new() { Start = new Point2D(0, 0), End = new Point2D(10, 0) }
                    }
                },
                ProjectContext = new RevitProjectContext
                {
                    DocumentTitle = "Project.rvt",
                    ActiveViewName = "Level 1",
                    ActiveViewType = "FloorPlan",
                    LevelId = "42",
                    LevelName = "Level 1",
                    LevelElevationMeters = 0,
                    ContourSource = "revit_selection",
                    ExistingElements = new List<RevitModelElementContext>
                    {
                        new()
                        {
                            ElementId = "100",
                            Category = "Walls",
                            Name = "Wall",
                            ElementType = "Basic Wall",
                            LevelName = "Level 1",
                            Parameters = new Dictionary<string, string> { ["Length"] = "12000" }
                        }
                    }
                },
                Parameters = new GenerationParameters
                {
                    GenerationType = GenerationType.Residential,
                    ValidationMode = ValidationMode.Advisory
                }
            };

            var dto = DtoMapper.ToDto(context);

            Assert.Equal("req-1", dto.RequestId);
            Assert.Equal("Полный prompt", dto.LlmPrompt);
            Assert.Equal("c1", dto.Context!.Contour!.Id);
            Assert.Equal("Project.rvt", dto.Context.RevitContext!.DocumentTitle);
            Assert.Equal("revit_selection", dto.Context.RevitContext.ContourSource);
            Assert.Single(dto.Context.RevitContext.ExistingElements);
            Assert.Equal("Walls", dto.Context.RevitContext.ExistingElements[0].Category);
        }
    }
}
