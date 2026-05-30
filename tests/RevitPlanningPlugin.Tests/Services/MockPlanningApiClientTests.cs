using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Api;
using RevitPlanningPlugin.Services.Geometry;

namespace RevitPlanningPlugin.Tests.Services
{
    public class MockPlanningApiClientTests
    {
        private readonly MockPlanningApiClient _client = new();

        // ——— TestConnectionAsync ———

        [Fact]
        public async Task TestConnectionAsync_ReturnsTrue()
        {
            var result = await _client.TestConnectionAsync();
            Assert.True(result);
        }

        // ——— GetContourListAsync ———

        [Fact]
        public async Task GetContourListAsync_ReturnsSixDemoContours()
        {
            var list = await _client.GetContourListAsync();
            Assert.Equal(6, list.Count);
        }

        [Fact]
        public async Task GetContourListAsync_AllHaveIdAndName()
        {
            var list = await _client.GetContourListAsync();
            Assert.All(list, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Id));
                Assert.False(string.IsNullOrWhiteSpace(c.Name));
            });
        }

        // ——— GetContourAsync ———

        [Theory]
        [InlineData("demo-rect")]
        [InlineData("demo-lshape")]
        [InlineData("demo-polygon")]
        [InlineData("demo-organic")]
        [InlineData("demo-spline")]
        [InlineData("demo-courtyard")]
        public async Task GetContourAsync_KnownIds_ReturnsContourWithSegments(string id)
        {
            var contour = await _client.GetContourAsync(id);
            Assert.NotNull(contour);
            Assert.True(contour.OuterLoop.Count >= 3,
                $"Контур '{id}' должен иметь не менее 3 сегментов");
        }

        [Fact]
        public async Task GetContourAsync_CourtyardContour_HasInnerLoop()
        {
            var contour = await _client.GetContourAsync("demo-courtyard");
            Assert.Single(contour.InnerLoops);
        }

        [Fact]
        public async Task GetContourAsync_OrganicContour_HasCurvedSegments()
        {
            var contour = await _client.GetContourAsync("demo-organic");
            Assert.True(contour.HasCurvedGeometry);
        }

        [Fact]
        public async Task GetContourAsync_SplineContour_HasSplineSegments()
        {
            var contour = await _client.GetContourAsync("demo-spline");
            Assert.True(contour.HasCurvedGeometry);
        }

        [Fact]
        public async Task GetContourAsync_UnknownId_ReturnsFallbackContour()
        {
            // Неизвестный id → прямоугольник (fallback)
            var contour = await _client.GetContourAsync("unknown-id-xyz");
            Assert.NotNull(contour);
            Assert.True(contour.OuterLoop.Count >= 3);
        }

        [Fact]
        public async Task GetContourAsync_RectContour_IsClosedAndHasCorrectArea()
        {
            var contour = await _client.GetContourAsync("demo-rect");
            Assert.True(contour.IsClosed);
            // 30×20 = 600 м²
            Assert.Equal(600.0, contour.ApproximateArea, precision: 1);
        }

        // ——— GenerateLayoutsAsync ———

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(5)]
        public async Task GenerateLayoutsAsync_ReturnsRequestedVariantCount(int count)
        {
            var parms = new GenerationParameters { VariantCount = count };
            var variants = await _client.GenerateLayoutsAsync("demo-rect", parms);
            Assert.Equal(count, variants.Count);
        }

        [Fact]
        public async Task GenerateLayoutsAsync_AllVariantsHaveRooms()
        {
            var parms = new GenerationParameters { VariantCount = 3 };
            var variants = await _client.GenerateLayoutsAsync("demo-rect", parms);
            Assert.All(variants, v => Assert.NotEmpty(v.Rooms));
        }

        [Fact]
        public async Task GenerateLayoutsAsync_AllVariantsHaveMetrics()
        {
            var parms = new GenerationParameters { VariantCount = 2 };
            var variants = await _client.GenerateLayoutsAsync("demo-rect", parms);
            Assert.All(variants, v =>
            {
                Assert.True(v.TotalArea > 0, "TotalArea должна быть > 0");
                Assert.True(v.UsableArea > 0, "UsableArea должна быть > 0");
                Assert.True(v.MopArea > 0, "MopArea должна быть > 0");
                Assert.True(v.EfficiencyScore >= 55, "EfficiencyScore должен быть >= 55");
            });
        }

        [Fact]
        public async Task GenerateLayoutsAsync_VariantsHaveDifferentIds()
        {
            var parms = new GenerationParameters { VariantCount = 3 };
            var variants = await _client.GenerateLayoutsAsync("demo-rect", parms);
            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var v in variants)
                ids.Add(v.Id);
            Assert.Equal(3, ids.Count);
        }

        [Fact]
        public async Task GenerateLayoutsAsync_VariantsHaveDifferentGeometryFingerprints()
        {
            var parms = new GenerationParameters { VariantCount = 5 };
            var variants = await _client.GenerateLayoutsAsync("demo-rect", parms);

            var fingerprints = variants
                .Select(GetGeometryFingerprint)
                .Distinct()
                .ToList();

            Assert.True(fingerprints.Count >= 4,
                $"Ожидались разные схемы планировок, фактически уникальных отпечатков: {fingerprints.Count}");
        }

        [Fact]
        public async Task GenerateLayoutsAsync_ContainsLobbyAndElevator()
        {
            var parms = new GenerationParameters { VariantCount = 1 };
            var variants = await _client.GenerateLayoutsAsync("demo-rect", parms);
            var rooms = variants[0].Rooms;
            // Должен быть хотя бы один лифтовый холл и один лифт
            Assert.Contains(rooms, r => r.Type == RevitPlanningPlugin.Models.Enums.RoomType.Lobby);
            Assert.Contains(rooms, r => r.Type == RevitPlanningPlugin.Models.Enums.RoomType.Elevator);
        }

        [Fact]
        public async Task GenerateLayoutsAsync_HasApartmentTypeDistribution()
        {
            var parms = new GenerationParameters { VariantCount = 1 };
            var variants = await _client.GenerateLayoutsAsync("demo-rect", parms);
            Assert.NotNull(variants[0].ApartmentTypeDistribution);
            Assert.NotEmpty(variants[0].ApartmentTypeDistribution);
        }

        [Fact]
        public async Task GenerateLayoutsAsync_VariantIndex_StartsAtOne()
        {
            var parms = new GenerationParameters { VariantCount = 3 };
            var variants = await _client.GenerateLayoutsAsync("demo-rect", parms);
            Assert.Equal(1, variants[0].VariantIndex);
            Assert.Equal(2, variants[1].VariantIndex);
            Assert.Equal(3, variants[2].VariantIndex);
        }

        [Fact]
        public async Task GenerateLayoutsAsync_Deterministic_SameResultForSameSeed()
        {
            var parms = new GenerationParameters { VariantCount = 2 };
            var r1 = await _client.GenerateLayoutsAsync("demo-rect", parms);
            var r2 = await _client.GenerateLayoutsAsync("demo-rect", parms);
            // Одинаковые метрики для одного и того же вызова (детерминированный seed)
            Assert.Equal(r1[0].TotalArea, r2[0].TotalArea);
            Assert.Equal(r1[0].EfficiencyScore, r2[0].EfficiencyScore);
        }

        [Fact]
        public async Task GenerateLayoutsAsync_MockContextChangesGeometry()
        {
            var contour = await _client.GetContourAsync("demo-rect");
            var parms = new GenerationParameters { VariantCount = 1 };
            var contextA = new GenerationRequestContext
            {
                Contour = contour,
                Parameters = parms,
                ProjectContext = new RevitProjectContext
                {
                    DocumentTitle = "Plan-A",
                    ActiveViewName = "Level 2",
                    LevelName = "Level 2",
                    ContourSource = "api",
                    ExistingElements = new List<RevitModelElementContext>
                    {
                        new RevitModelElementContext
                        {
                            ElementId = "101",
                            Category = "Walls",
                            Name = "Existing wall A",
                            ElementType = "Basic Wall"
                        }
                    }
                }
            };
            var contextB = new GenerationRequestContext
            {
                Contour = contour,
                Parameters = parms,
                ProjectContext = new RevitProjectContext
                {
                    DocumentTitle = "Plan-B",
                    ActiveViewName = "Level 2",
                    LevelName = "Level 2",
                    ContourSource = "api",
                    ExistingElements = new List<RevitModelElementContext>
                    {
                        new RevitModelElementContext
                        {
                            ElementId = "201",
                            Category = "Rooms",
                            Name = "Updated room",
                            ElementType = "Room"
                        },
                        new RevitModelElementContext
                        {
                            ElementId = "202",
                            Category = "Room Separation Lines",
                            Name = "Generated separator",
                            ElementType = "ModelCurve"
                        }
                    }
                }
            };

            var variantsA = await _client.GenerateLayoutsAsync(contextA);
            var variantsB = await _client.GenerateLayoutsAsync(contextB);

            Assert.NotEqual(GetGeometryFingerprint(variantsA[0]), GetGeometryFingerprint(variantsB[0]));
        }

        [Fact]
        public async Task GenerateLayoutsAsync_DefaultResidentialProgram_PassesStrictValidationForRectContour()
        {
            var parms = new GenerationParameters
            {
                VariantCount = 3,
                ValidationMode = ValidationMode.Strict,
                RequiredRoomTypes = new List<RoomType>
                {
                    RoomType.LivingRoom,
                    RoomType.CommonArea,
                    RoomType.Lobby,
                    RoomType.Elevator
                }
            };
            var contour = await _client.GetContourAsync("demo-rect");
            var variants = await _client.GenerateLayoutsAsync("demo-rect", parms);

            var result = new LayoutVariantValidator().Validate(variants, parms, contour);

            Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}")));
        }

        [Theory]
        [InlineData("demo-rect")]
        [InlineData("demo-lshape")]
        [InlineData("demo-polygon")]
        [InlineData("demo-organic")]
        [InlineData("demo-spline")]
        [InlineData("demo-courtyard")]
        public async Task GenerateLayoutsAsync_AllDemoContours_CanProduceStrictValidSmallProgram(string contourId)
        {
            var parms = new GenerationParameters
            {
                VariantCount = 1,
                ValidationMode = ValidationMode.Strict,
                OneRoomCount = 2,
                TwoRoomCount = 0,
                ThreeRoomCount = 0,
                RequiredRoomTypes = new List<RoomType>
                {
                    RoomType.LivingRoom,
                    RoomType.CommonArea,
                    RoomType.Lobby,
                    RoomType.Elevator
                }
            };
            var contour = await _client.GetContourAsync(contourId);
            var variants = await _client.GenerateLayoutsAsync(contourId, parms);

            var result = new LayoutVariantValidator().Validate(variants, parms, contour);

            Assert.True(result.IsValid, string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}")));
        }

        [Fact]
        public async Task GenerateLayoutsAsync_GenerationErrorScenario_ThrowsPlanningApiException()
        {
            var client = new MockPlanningApiClient(MockScenario.GenerationError);
            var parms = new GenerationParameters { VariantCount = 1 };

            var exception = await Assert.ThrowsAsync<PlanningApiException>(
                () => client.GenerateLayoutsAsync("demo-rect", parms));

            Assert.Equal("MOCK_GENERATION_ERROR", exception.ErrorCode);
        }

        [Fact]
        public async Task GenerateLayoutsAsync_HallucinationScenario_ReturnsStrictValidationError()
        {
            var client = new MockPlanningApiClient(MockScenario.Hallucination);
            var parms = new GenerationParameters
            {
                VariantCount = 1,
                ValidationMode = ValidationMode.Strict,
                OneRoomCount = 1,
                TwoRoomCount = 0,
                ThreeRoomCount = 0,
                MinRoomArea = 1,
                MaxRoomArea = 200
            };
            var contour = await client.GetContourAsync("demo-rect");
            var variants = await client.GenerateLayoutsAsync("demo-rect", parms);

            var result = new LayoutVariantValidator().Validate(variants, parms, contour);

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "ROOM_OUTSIDE_CONTOUR");
        }

        private static string GetGeometryFingerprint(LayoutVariant variant)
        {
            return string.Join("|", variant.Rooms
                .OrderBy(r => r.Id)
                .Select(r =>
                {
                    var x = r.LabelPoint != null ? r.LabelPoint.X : 0;
                    var y = r.LabelPoint != null ? r.LabelPoint.Y : 0;
                    return $"{r.Type}:{x:F1}:{y:F1}:{r.Area:F1}";
                }));
        }
    }
}
