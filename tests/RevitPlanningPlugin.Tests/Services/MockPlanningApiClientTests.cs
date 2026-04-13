using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Services.Api;

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
    }
}
