using System.Collections.Generic;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Geometry;
using Xunit;

namespace RevitPlanningPlugin.Tests.Services
{
    public class LayoutVariantValidatorTests
    {
        private readonly LayoutVariantValidator _validator = new();

        [Fact]
        public void Validate_StrictMode_MissingRequestedApartmentType_ReturnsError()
        {
            var parameters = new GenerationParameters
            {
                ValidationMode = ValidationMode.Strict,
                OneRoomCount = 2,
                TwoRoomCount = 0,
                ThreeRoomCount = 0,
                MinApartmentArea = 10,
                MaxApartmentArea = 100
            };

            var variant = MakeVariant();
            variant.ApartmentTypeDistribution.Clear();
            variant.ApartmentTypeDistribution["OneRoom"] = 1;

            var result = _validator.Validate(new[] { variant }, parameters, MakeContour());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "APARTMENT_TYPE_MISSING");
        }

        [Fact]
        public void Validate_AdvisoryMode_MissingMop_ReturnsWarningOnly()
        {
            var parameters = new GenerationParameters
            {
                ValidationMode = ValidationMode.Advisory,
                OneRoomCount = 1,
                TwoRoomCount = 0,
                ThreeRoomCount = 0
            };

            var variant = MakeVariant();
            variant.Rooms.RemoveAll(r => r.Type == RoomType.CommonArea);
            variant.MopArea = 0;

            var result = _validator.Validate(new[] { variant }, parameters, MakeContour());

            Assert.True(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "MOP_MISSING" && i.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void Validate_StrictMode_MissingRequiredRoomType_ReturnsError()
        {
            var parameters = new GenerationParameters
            {
                ValidationMode = ValidationMode.Strict,
                OneRoomCount = 1,
                TwoRoomCount = 0,
                ThreeRoomCount = 0,
                RequiredRoomTypes = new List<RoomType> { RoomType.Lobby }
            };

            var result = _validator.Validate(new[] { MakeVariant() }, parameters, MakeContour());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "ROOM_TYPE_MISSING");
        }

        [Fact]
        public void Validate_StrictMode_RoomOutsideContour_ReturnsError()
        {
            var parameters = MakeStrictParameters();
            var variant = MakeVariant();
            variant.Rooms[0] = MakeRoom("outside", RoomType.LivingRoom, 11, 0, 13, 2, "OneRoom");

            var result = _validator.Validate(new[] { variant }, parameters, MakeContour());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "ROOM_OUTSIDE_CONTOUR");
        }

        [Fact]
        public void Validate_StrictMode_RoomInsideInnerVoid_ReturnsError()
        {
            var parameters = MakeStrictParameters();
            var variant = MakeVariant();
            variant.Rooms[0] = MakeRoom("inside-void", RoomType.LivingRoom, 4, 4, 6, 6, "OneRoom");

            var result = _validator.Validate(new[] { variant }, parameters, MakeContourWithInnerVoid());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "ROOM_OUTSIDE_CONTOUR");
        }

        [Fact]
        public void Validate_StrictMode_RoomCrossesInnerVoid_ReturnsError()
        {
            var parameters = MakeStrictParameters();
            var variant = MakeVariant();
            variant.Rooms[0] = MakeRoom("crosses-void", RoomType.LivingRoom, 2, 4, 8, 6, "OneRoom");
            variant.Rooms[0].LabelPoint = new Point2D(2.5, 5);

            var result = _validator.Validate(new[] { variant }, parameters, MakeContourWithInnerVoid());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "ROOM_OUTSIDE_CONTOUR");
        }

        [Fact]
        public void Validate_StrictMode_OverlappingRooms_ReturnsError()
        {
            var parameters = MakeStrictParameters();
            var variant = MakeVariant();
            variant.Rooms.Add(MakeRoom("storage", RoomType.Storage, 1, 1, 3, 3));

            var result = _validator.Validate(new[] { variant }, parameters, MakeContour());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "ROOMS_OVERLAP");
        }

        [Fact]
        public void Validate_StrictMode_LabelPointOutsideRoom_ReturnsError()
        {
            var parameters = MakeStrictParameters();
            var variant = MakeVariant();
            variant.Rooms[0].LabelPoint = new Point2D(9, 9);

            var result = _validator.Validate(new[] { variant }, parameters, MakeContour());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "ROOM_LABEL_POINT_OUTSIDE_ROOM");
        }

        [Fact]
        public void Validate_StrictMode_NonPositiveRoomArea_ReturnsError()
        {
            var parameters = MakeStrictParameters();
            var variant = MakeVariant();
            variant.Rooms[0].Area = 0;

            var result = _validator.Validate(new[] { variant }, parameters, MakeContour());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "ROOM_AREA_NON_POSITIVE");
        }

        [Fact]
        public void Validate_StrictMode_SmallElevator_DoesNotUseGenericMinRoomArea()
        {
            var parameters = MakeStrictParameters();
            parameters.MinRoomArea = 8;
            var variant = MakeVariant();
            variant.Rooms = new List<RoomLayout>
            {
                MakeRoom("apt1", RoomType.LivingRoom, 0, 0, 7, 10, "OneRoom"),
                MakeRoom("elev", RoomType.Elevator, 7, 0, 9, 2)
            };
            variant.MopArea = 4;

            var result = _validator.Validate(new[] { variant }, parameters, MakeContour());

            Assert.DoesNotContain(result.Issues, i => i.Code == "ROOM_AREA_TOO_SMALL");
        }

        [Fact]
        public void Validate_StrictMode_MultiRoomApartment_UsesApartmentIdGroupArea()
        {
            var parameters = MakeStrictParameters();
            parameters.MinApartmentArea = 30;
            var variant = MakeVariant();
            variant.Rooms = new List<RoomLayout>
            {
                MakeRoom("bedroom", RoomType.Bedroom, 0, 0, 3, 5, "OneRoom", "apt-1"),
                MakeRoom("kitchen", RoomType.Kitchen, 3, 0, 7, 5, "OneRoom", "apt-1"),
                MakeRoom("mop1", RoomType.CommonArea, 7, 0, 10, 10)
            };
            variant.ApartmentCount = 1;
            variant.ApartmentTypeDistribution = new Dictionary<string, int> { ["OneRoom"] = 1 };

            var result = _validator.Validate(new[] { variant }, parameters, MakeContour());

            Assert.DoesNotContain(result.Issues, i => i.Code == "APARTMENT_AREA_TOO_SMALL");
        }

        [Fact]
        public void Validate_ApartmentRooms_DoesNotRequireMopAndAcceptsOneApartment()
        {
            var parameters = MakeStrictParameters();
            parameters.PlanningDetailMode = PlanningDetailMode.ApartmentRooms;
            parameters.OneRoomCount = 1;
            parameters.RequiredRoomTypes = new List<RoomType>
            {
                RoomType.LivingRoom,
                RoomType.Kitchen,
                RoomType.Bathroom,
                RoomType.CommonArea
            };

            var result = _validator.Validate(new[] { MakeApartmentRoomsVariant() }, parameters, MakeContour());

            Assert.True(result.IsValid);
            Assert.DoesNotContain(result.Issues, i => i.Code == "MOP_MISSING");
            Assert.DoesNotContain(result.Issues, i => i.Code == "ROOM_TYPE_MISSING");
            Assert.DoesNotContain(result.Issues, i => i.Code == "APARTMENT_COUNT_MISMATCH");
        }

        [Fact]
        public void Validate_ApartmentRooms_TwoApartmentIds_ReturnsCountMismatch()
        {
            var parameters = MakeStrictParameters();
            parameters.PlanningDetailMode = PlanningDetailMode.ApartmentRooms;
            parameters.OneRoomCount = 1;

            var variant = MakeApartmentRoomsVariant();
            variant.Rooms.Add(MakeRoom("extra", RoomType.LivingRoom, 7, 0, 10, 3, "OneRoom", "apt_2"));

            var result = _validator.Validate(new[] { variant }, parameters, MakeContour());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "APARTMENT_COUNT_MISMATCH");
        }

        [Fact]
        public void Validate_StrictMode_TotalAreaContourMismatch_ReturnsWarningOnly()
        {
            var parameters = MakeStrictParameters();
            var variant = MakeVariant();
            variant.TotalArea = 20;

            var result = _validator.Validate(new[] { variant }, parameters, MakeContour());

            Assert.True(result.IsValid);
            Assert.Contains(result.Issues,
                i => i.Code == "TOTAL_AREA_CONTOUR_MISMATCH"
                     && i.Severity == ValidationSeverity.Warning);
        }

        [Fact]
        public void Validate_OffMode_ApartmentAreaAboveMax_ReturnsError()
        {
            var parameters = MakeStrictParameters();
            parameters.ValidationMode = ValidationMode.Off;
            parameters.MaxApartmentArea = 10;

            var result = _validator.Validate(new[] { MakeVariant() }, parameters, MakeContour());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "APARTMENT_AREA_TOO_LARGE"
                                               && i.Severity == ValidationSeverity.Error);
            Assert.Contains(result.Issues, i => i.Code == "VALIDATION_OFF"
                                               && i.Severity == ValidationSeverity.Info);
        }

        [Fact]
        public void Validate_TypeSpecificApartmentAreaAboveMax_ReturnsError()
        {
            var parameters = MakeStrictParameters();
            parameters.MaxApartmentArea = 120;
            parameters.OneRoomMaxApartmentArea = 45;

            var result = _validator.Validate(new[] { MakeVariant() }, parameters, MakeContour());

            Assert.False(result.IsValid);
            Assert.Contains(result.Issues, i => i.Code == "APARTMENT_AREA_TOO_LARGE"
                                               && i.Message.Contains("45"));
        }

        private static LayoutVariant MakeVariant()
        {
            return new LayoutVariant
            {
                Id = "v1",
                Name = "Вариант 1",
                VariantIndex = 1,
                TotalArea = 100,
                UsableArea = 70,
                MopArea = 20,
                ApartmentCount = 1,
                ApartmentTypeDistribution = new Dictionary<string, int> { ["OneRoom"] = 1 },
                Rooms = new List<RoomLayout>
                {
                    MakeRoom("apt1", RoomType.LivingRoom, 0, 0, 7, 10, "OneRoom"),
                    MakeRoom("mop1", RoomType.CommonArea, 7, 0, 10, 10)
                }
            };
        }

        private static LayoutVariant MakeApartmentRoomsVariant()
        {
            return new LayoutVariant
            {
                Id = "apt-rooms",
                Name = "Квартира 1К",
                VariantIndex = 1,
                TotalArea = 100,
                UsableArea = 42,
                MopArea = 0,
                CorridorArea = 0,
                ApartmentCount = 1,
                ApartmentTypeDistribution = new Dictionary<string, int> { ["OneRoom"] = 1 },
                Rooms = new List<RoomLayout>
                {
                    MakeRoom("living", RoomType.LivingRoom, 0, 0, 4, 6, "OneRoom", "apt_1"),
                    MakeRoom("kitchen", RoomType.Kitchen, 4, 0, 7, 3, "OneRoom", "apt_1"),
                    MakeRoom("bathroom", RoomType.Bathroom, 4, 3, 7, 6, "OneRoom", "apt_1")
                }
            };
        }

        private static RoomLayout MakeRoom(
            string id,
            RoomType type,
            double x0,
            double y0,
            double x1,
            double y1,
            string? apartmentType = null)
        {
            var room = new RoomLayout
            {
                Id = id,
                Name = id,
                Type = type,
                Area = (x1 - x0) * (y1 - y0),
                LabelPoint = new Point2D((x0 + x1) / 2, (y0 + y1) / 2),
                Boundary = new List<ContourSegment>
                {
                    new() { Start = new Point2D(x0, y0), End = new Point2D(x1, y0) },
                    new() { Start = new Point2D(x1, y0), End = new Point2D(x1, y1) },
                    new() { Start = new Point2D(x1, y1), End = new Point2D(x0, y1) },
                    new() { Start = new Point2D(x0, y1), End = new Point2D(x0, y0) }
                }
            };

            if (apartmentType != null)
                room.Properties["apartment_type"] = apartmentType;

            return room;
        }

        private static RoomLayout MakeRoom(
            string id,
            RoomType type,
            double x0,
            double y0,
            double x1,
            double y1,
            string apartmentType,
            string apartmentId)
        {
            var room = MakeRoom(id, type, x0, y0, x1, y1, apartmentType);
            room.Properties["apartment_id"] = apartmentId;
            return room;
        }

        private static GenerationParameters MakeStrictParameters()
        {
            return new GenerationParameters
            {
                ValidationMode = ValidationMode.Strict,
                OneRoomCount = 1,
                TwoRoomCount = 0,
                ThreeRoomCount = 0,
                MinApartmentArea = 1,
                MaxApartmentArea = 100,
                MinRoomArea = 1,
                MaxRoomArea = 200
            };
        }

        private static BuildingContour MakeContour()
        {
            return new BuildingContour
            {
                Id = "c1",
                Name = "Контур",
                OuterLoop = new List<ContourSegment>
                {
                    new() { Start = new Point2D(0, 0), End = new Point2D(10, 0) },
                    new() { Start = new Point2D(10, 0), End = new Point2D(10, 10) },
                    new() { Start = new Point2D(10, 10), End = new Point2D(0, 10) },
                    new() { Start = new Point2D(0, 10), End = new Point2D(0, 0) }
                }
            };
        }

        private static BuildingContour MakeContourWithInnerVoid()
        {
            var contour = MakeContour();
            contour.InnerLoops.Add(new List<ContourSegment>
            {
                new() { Start = new Point2D(3, 3), End = new Point2D(7, 3) },
                new() { Start = new Point2D(7, 3), End = new Point2D(7, 7) },
                new() { Start = new Point2D(7, 7), End = new Point2D(3, 7) },
                new() { Start = new Point2D(3, 7), End = new Point2D(3, 3) }
            });
            return contour;
        }
    }
}
