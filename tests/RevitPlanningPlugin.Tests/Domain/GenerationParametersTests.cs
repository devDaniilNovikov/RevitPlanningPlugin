using System.Linq;
using Xunit;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;

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
            Assert.Equal(0.0, p.OneRoomMaxApartmentArea);
            Assert.Equal(1.8, p.MinCorridorWidth);
            Assert.Equal(PlanningDetailMode.FloorLayout, p.PlanningDetailMode);
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
        public void GetMaxApartmentAreaForType_UsesTypeOverrideWhenSet()
        {
            var p = new GenerationParameters
            {
                MaxApartmentArea = 120,
                OneRoomMaxApartmentArea = 45
            };

            Assert.Equal(45.0, p.GetMaxApartmentAreaForType("OneRoom"));
            Assert.Equal(120.0, p.GetMaxApartmentAreaForType("TwoRoom"));
        }

        [Fact]
        public void GetMaximumApartmentProgramArea_SumsTypeSpecificLimits()
        {
            var p = new GenerationParameters
            {
                StudioCount = 0,
                OneRoomCount = 2,
                TwoRoomCount = 1,
                ThreeRoomCount = 0,
                FourRoomCount = 0,
                MaxApartmentArea = 120,
                OneRoomMaxApartmentArea = 45,
                TwoRoomMaxApartmentArea = 70
            };

            Assert.Equal(160.0, p.GetMaximumApartmentProgramArea());
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

        [Fact]
        public void GetEffectiveApartmentTypeRequirements_FloorLayout_UsesFullProgram()
        {
            var p = new GenerationParameters
            {
                PlanningDetailMode = PlanningDetailMode.FloorLayout,
                OneRoomCount = 2,
                TwoRoomCount = 3,
                ThreeRoomCount = 0,
                FourRoomCount = 0
            };

            var dict = p.GetEffectiveApartmentTypeRequirements();

            Assert.Equal(2, dict.Count);
            Assert.Equal(2, dict["OneRoom"]);
            Assert.Equal(3, dict["TwoRoom"]);
            Assert.Equal(5, p.EffectiveApartmentsRequested);
        }

        [Fact]
        public void GetEffectiveApartmentTypeRequirements_ApartmentRooms_UsesOnePrimaryApartment()
        {
            var p = new GenerationParameters
            {
                PlanningDetailMode = PlanningDetailMode.ApartmentRooms,
                StudioCount = 0,
                OneRoomCount = 0,
                TwoRoomCount = 4,
                ThreeRoomCount = 2,
                FourRoomCount = 0
            };

            var dict = p.GetEffectiveApartmentTypeRequirements();

            Assert.Single(dict);
            Assert.Equal("TwoRoom", p.GetPrimaryApartmentType());
            Assert.Equal(1, dict["TwoRoom"]);
            Assert.Equal(1, p.EffectiveApartmentsRequested);
        }

        [Fact]
        public void GetEffectiveRequiredRoomTypes_ApartmentRooms_FiltersFloorMopTypes()
        {
            var p = new GenerationParameters
            {
                PlanningDetailMode = PlanningDetailMode.ApartmentRooms,
                TwoRoomCount = 1,
                RequiredRoomTypes =
                {
                    RoomType.LivingRoom,
                    RoomType.Bedroom,
                    RoomType.Kitchen,
                    RoomType.Bathroom,
                    RoomType.CommonArea,
                    RoomType.Elevator,
                    RoomType.Staircase
                }
            };

            var types = p.GetEffectiveRequiredRoomTypes();

            Assert.Contains(RoomType.LivingRoom, types);
            Assert.Contains(RoomType.Bedroom, types);
            Assert.Contains(RoomType.Kitchen, types);
            Assert.Contains(RoomType.Bathroom, types);
            Assert.DoesNotContain(RoomType.CommonArea, types);
            Assert.DoesNotContain(RoomType.Elevator, types);
            Assert.DoesNotContain(RoomType.Staircase, types);
        }
    }
}
