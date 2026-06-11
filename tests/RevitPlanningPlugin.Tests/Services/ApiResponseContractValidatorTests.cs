using System.Collections.Generic;
using RevitPlanningPlugin.Models.Api;
using RevitPlanningPlugin.Services.Api;
using Xunit;

namespace RevitPlanningPlugin.Tests.Services
{
    public class ApiResponseContractValidatorTests
    {
        [Fact]
        public void ValidateGenerationResult_ValidResponse_DoesNotThrow()
        {
            var result = MakeResult(2);

            var exception = Record.Exception(
                () => ApiResponseContractValidator.ValidateGenerationResult(result, expectedVariantCount: 2));

            Assert.Null(exception);
        }

        [Fact]
        public void ValidateGenerationResult_VariantCountMismatch_ThrowsContractException()
        {
            var result = MakeResult(1);

            var exception = Assert.Throws<PlanningApiException>(
                () => ApiResponseContractValidator.ValidateGenerationResult(result, expectedVariantCount: 3));

            Assert.Equal("INVALID_API_CONTRACT", exception.ErrorCode);
            Assert.Contains("exactly 3", exception.Message);
        }

        [Fact]
        public void ValidateGenerationResult_MissingRoomBoundary_ThrowsContractException()
        {
            var result = MakeResult(1);
            result.Variants[0].Rooms[0].Boundary.Clear();

            var exception = Assert.Throws<PlanningApiException>(
                () => ApiResponseContractValidator.ValidateGenerationResult(result, expectedVariantCount: 1));

            Assert.Equal("INVALID_API_CONTRACT", exception.ErrorCode);
            Assert.Contains("boundary", exception.Message);
        }

        [Fact]
        public void ValidateGenerationResult_InvalidCoordinate_ThrowsContractException()
        {
            var result = MakeResult(1);
            result.Variants[0].Rooms[0].Boundary[0].Start.X = double.NaN;

            var exception = Assert.Throws<PlanningApiException>(
                () => ApiResponseContractValidator.ValidateGenerationResult(result, expectedVariantCount: 1));

            Assert.Equal("INVALID_API_CONTRACT", exception.ErrorCode);
            Assert.Contains("invalid coordinates", exception.Message);
        }

        [Fact]
        public void ValidateGenerationResult_UnknownRoomType_ThrowsContractException()
        {
            var result = MakeResult(1);
            result.Variants[0].Rooms[0].Type = "MagicZone";

            var exception = Assert.Throws<PlanningApiException>(
                () => ApiResponseContractValidator.ValidateGenerationResult(result, expectedVariantCount: 1));

            Assert.Equal("INVALID_API_CONTRACT", exception.ErrorCode);
            Assert.Contains("unknown room type", exception.Message);
        }

        [Fact]
        public void ValidateGenerationResult_UnknownApartmentType_ThrowsContractException()
        {
            var result = MakeResult(1);
            result.Variants[0].Rooms[0].Properties!["apartment_type"] = "MegaFlat";

            var exception = Assert.Throws<PlanningApiException>(
                () => ApiResponseContractValidator.ValidateGenerationResult(result, expectedVariantCount: 1));

            Assert.Equal("INVALID_API_CONTRACT", exception.ErrorCode);
            Assert.Contains("unknown apartment type", exception.Message);
        }

        [Fact]
        public void ValidateGenerationResult_UnknownSegmentType_ThrowsContractException()
        {
            var result = MakeResult(1);
            result.Variants[0].Rooms[0].Boundary[0].Type = "teleport";

            var exception = Assert.Throws<PlanningApiException>(
                () => ApiResponseContractValidator.ValidateGenerationResult(result, expectedVariantCount: 1));

            Assert.Equal("INVALID_API_CONTRACT", exception.ErrorCode);
            Assert.Contains("unknown segment type", exception.Message);
        }

        [Fact]
        public void ValidateGenerationResult_ArcWithoutCenterOrRadius_ThrowsContractException()
        {
            var result = MakeResult(1);
            result.Variants[0].Rooms[0].Boundary[0].Type = "arc";
            result.Variants[0].Rooms[0].Boundary[0].Center = null;
            result.Variants[0].Rooms[0].Boundary[0].Radius = null;

            var exception = Assert.Throws<PlanningApiException>(
                () => ApiResponseContractValidator.ValidateGenerationResult(result, expectedVariantCount: 1));

            Assert.Equal("INVALID_API_CONTRACT", exception.ErrorCode);
            Assert.Contains("center is required", exception.Message);
            Assert.Contains("radius must be positive", exception.Message);
        }

        private static ApiGenerationResultDto MakeResult(int variantCount)
        {
            var result = new ApiGenerationResultDto
            {
                RequestId = "req-1",
                Status = "completed"
            };

            for (int i = 0; i < variantCount; i++)
            {
                result.Variants.Add(new ApiLayoutVariantDto
                {
                    Id = $"v{i + 1}",
                    Name = $"Вариант {i + 1}",
                    VariantIndex = i + 1,
                    TotalArea = 100,
                    UsableArea = 70,
                    MopArea = 20,
                    RoomCount = 2,
                    ApartmentCount = 1,
                    ApartmentTypeDistribution = new Dictionary<string, int> { ["OneRoom"] = 1 },
                    EfficiencyScore = 80,
                    Rooms = new List<ApiRoomDto>
                    {
                        new()
                        {
                            Id = "r1",
                            Name = "Квартира",
                            Type = "LivingRoom",
                            Area = 70,
                            LabelPoint = new ApiPointDto { X = 3, Y = 3 },
                            Boundary = MakeRectangle(0, 0, 7, 10),
                            Properties = new Dictionary<string, string> { ["apartment_type"] = "OneRoom" }
                        },
                        new()
                        {
                            Id = "mop1",
                            Name = "МОП",
                            Type = "CommonArea",
                            Area = 30,
                            LabelPoint = new ApiPointDto { X = 8.5, Y = 5 },
                            Boundary = MakeRectangle(7, 0, 10, 10)
                        }
                    },
                    Partitions = new List<ApiSegmentDto>
                    {
                        new()
                        {
                            Type = "line",
                            Start = new ApiPointDto { X = 7, Y = 0 },
                            End = new ApiPointDto { X = 7, Y = 10 }
                        }
                    }
                });
            }

            return result;
        }

        private static List<ApiSegmentDto> MakeRectangle(double x0, double y0, double x1, double y1)
        {
            return new List<ApiSegmentDto>
            {
                new() { Type = "line", Start = new ApiPointDto { X = x0, Y = y0 }, End = new ApiPointDto { X = x1, Y = y0 } },
                new() { Type = "line", Start = new ApiPointDto { X = x1, Y = y0 }, End = new ApiPointDto { X = x1, Y = y1 } },
                new() { Type = "line", Start = new ApiPointDto { X = x1, Y = y1 }, End = new ApiPointDto { X = x0, Y = y1 } },
                new() { Type = "line", Start = new ApiPointDto { X = x0, Y = y1 }, End = new ApiPointDto { X = x0, Y = y0 } }
            };
        }
    }
}
