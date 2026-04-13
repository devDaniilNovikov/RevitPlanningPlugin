using System.Linq;
using Xunit;
using RevitPlanningPlugin.Models.Domain;

namespace RevitPlanningPlugin.Tests.Domain
{
    public class GenerationParametersTests
    {
        [Fact]
        public void Defaults_AreReasonable()
        {
            var p = new GenerationParameters();
            Assert.Equal(3, p.VariantCount);
            Assert.Equal(25.0, p.MinApartmentArea);
            Assert.Equal(120.0, p.MaxApartmentArea);
            Assert.Equal(1.4, p.MinCorridorWidth);
            Assert.Equal("efficiency", p.OptimizationPriority);
        }

        [Fact]
        public void TotalApartmentsRequested_SumsAllTypes()
        {
            var p = new GenerationParameters
            {
                StudioCount = 1,
                OneRoomCount = 2,
                TwoRoomCount = 3,
                ThreeRoomCount = 2,
                FourRoomCount = 1
            };
            Assert.Equal(9, p.TotalApartmentsRequested);
        }

        [Fact]
        public void TotalApartmentsRequested_AllZero()
        {
            var p = new GenerationParameters
            {
                StudioCount = 0,
                OneRoomCount = 0,
                TwoRoomCount = 0,
                ThreeRoomCount = 0,
                FourRoomCount = 0
            };
            Assert.Equal(0, p.TotalApartmentsRequested);
        }

        [Fact]
        public void GetApartmentTypeRequirements_OnlyNonZeroTypesIncluded()
        {
            var p = new GenerationParameters
            {
                StudioCount = 0,
                OneRoomCount = 3,
                TwoRoomCount = 0,
                ThreeRoomCount = 2,
                FourRoomCount = 0
            };
            var dict = p.GetApartmentTypeRequirements();
            Assert.Equal(2, dict.Count);
            Assert.True(dict.ContainsKey("OneRoom"));
            Assert.Equal(3, dict["OneRoom"]);
            Assert.True(dict.ContainsKey("ThreeRoom"));
            Assert.Equal(2, dict["ThreeRoom"]);
            Assert.False(dict.ContainsKey("Studio"));
        }

        [Fact]
        public void GetApartmentTypeRequirements_AllZero_ReturnsEmptyDict()
        {
            var p = new GenerationParameters();
            p.StudioCount = 0; p.OneRoomCount = 0; p.TwoRoomCount = 0;
            p.ThreeRoomCount = 0; p.FourRoomCount = 0;
            var dict = p.GetApartmentTypeRequirements();
            Assert.Empty(dict);
        }

        [Fact]
        public void GetApartmentTypeRequirements_AllTypes_AllPresent()
        {
            var p = new GenerationParameters
            {
                StudioCount = 1, OneRoomCount = 2, TwoRoomCount = 3,
                ThreeRoomCount = 4, FourRoomCount = 5
            };
            var dict = p.GetApartmentTypeRequirements();
            Assert.Equal(5, dict.Count);
            Assert.Equal(1, dict["Studio"]);
            Assert.Equal(5, dict["FourRoom"]);
        }
    }
}
