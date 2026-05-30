using System.Collections.Generic;
using System.Linq;
using Xunit;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Tests.Domain
{
    public class LayoutVariantTests
    {
        private static RoomLayout MakeRoom(RoomType type, double area, string id = "r1")
            => new() { Id = id, Name = id, Type = type, Area = area };

        // ——— UsableRatio ———

        [Fact]
        public void UsableRatio_NormalCase_Correct()
        {
            var v = new LayoutVariant { TotalArea = 100.0, UsableArea = 75.0 };
            Assert.Equal(0.75, v.UsableRatio, precision: 10);
        }

        [Fact]
        public void UsableRatio_TotalAreaZero_ReturnsZero()
        {
            var v = new LayoutVariant { TotalArea = 0.0, UsableArea = 50.0 };
            Assert.Equal(0.0, v.UsableRatio);
        }

        // ——— MopRooms ———

        [Fact]
        public void MopRooms_ReturnsCorrectTypes()
        {
            var v = new LayoutVariant
            {
                Rooms = new List<RoomLayout>
                {
                    MakeRoom(RoomType.CommonArea, 20, "mop1"),
                    MakeRoom(RoomType.Lobby, 15, "lobby"),
                    MakeRoom(RoomType.Elevator, 5, "elev"),
                    MakeRoom(RoomType.Staircase, 12, "stairs"),
                    MakeRoom(RoomType.Corridor, 10, "corr"),
                    MakeRoom(RoomType.LivingRoom, 40, "apt1"),
                }
            };
            var mops = v.MopRooms.ToList();
            Assert.Equal(5, mops.Count);
            Assert.All(mops, r => Assert.Contains(r.Type, new[]
            {
                RoomType.CommonArea, RoomType.Corridor, RoomType.Lobby, RoomType.Elevator, RoomType.Staircase
            }));
        }

        // ——— ResidentialRooms ———

        [Fact]
        public void ResidentialRooms_ReturnsOnlyResidentialTypes()
        {
            var v = new LayoutVariant
            {
                Rooms = new List<RoomLayout>
                {
                    MakeRoom(RoomType.LivingRoom, 40, "lr"),
                    MakeRoom(RoomType.Bedroom, 20, "br"),
                    MakeRoom(RoomType.Kitchen, 15, "kitch"),
                    MakeRoom(RoomType.Bathroom, 8, "bath"),
                    MakeRoom(RoomType.CommonArea, 20, "mop")
                }
            };
            var res = v.ResidentialRooms.ToList();
            Assert.Equal(3, res.Count);
        }

        // ——— CatalogSummary ———

        [Fact]
        public void CatalogSummary_WithApartments_ContainsAptCount()
        {
            var v = new LayoutVariant
            {
                ApartmentCount = 8,
                MopArea = 50,
                TotalArea = 500,
                UsableArea = 400,
                EfficiencyScore = 72
            };
            var s = v.CatalogSummary;
            Assert.Contains("8", s);
            Assert.Contains("72", s);
        }

        [Fact]
        public void CatalogSummary_NoApartments_ShowsRoomCount()
        {
            var v = new LayoutVariant
            {
                ApartmentCount = 0,
                RoomCount = 5,
                TotalArea = 100,
                UsableArea = 80
            };
            var s = v.CatalogSummary;
            Assert.Contains("5", s);
            Assert.Contains("пом.", s);
        }

        // ——— MetricsDetail ———

        [Fact]
        public void MetricsDetail_ContainsAllMetrics()
        {
            var v = new LayoutVariant
            {
                TotalArea = 600, UsableArea = 450, MopArea = 80,
                CorridorArea = 40, ApartmentCount = 10, EfficiencyScore = 75
            };
            var s = v.MetricsDetail;
            Assert.Contains("600", s);
            Assert.Contains("450", s);
            Assert.Contains("80", s);
            Assert.Contains("40", s);
            Assert.Contains("75", s);
        }

        // ——— ApartmentTypeSummary ———

        [Fact]
        public void ApartmentTypeSummary_KnownTypes_LocalizedLabels()
        {
            var v = new LayoutVariant
            {
                ApartmentTypeDistribution = new Dictionary<string, int>
                {
                    ["Studio"] = 2,
                    ["OneRoom"] = 4
                }
            };
            var s = v.ApartmentTypeSummary;
            Assert.Contains("Студии", s);
            Assert.Contains("1К", s);
        }

        [Fact]
        public void ApartmentTypeSummary_Empty_ReturnsEmptyString()
        {
            var v = new LayoutVariant
            {
                ApartmentTypeDistribution = new Dictionary<string, int>()
            };
            Assert.Equal(string.Empty, v.ApartmentTypeSummary);
        }

        [Fact]
        public void ApartmentTypeSummary_NullDistribution_ReturnsEmptyString()
        {
            var v = new LayoutVariant
            {
                ApartmentTypeDistribution = null!
            };
            Assert.Equal(string.Empty, v.ApartmentTypeSummary);
        }
    }
}
