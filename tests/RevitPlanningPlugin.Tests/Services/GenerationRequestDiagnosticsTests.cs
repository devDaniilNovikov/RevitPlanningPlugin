using System.Collections.Generic;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Services.Diagnostics;
using Xunit;

namespace RevitPlanningPlugin.Tests.Services
{
    public class GenerationRequestDiagnosticsTests
    {
        [Fact]
        public void BuildParameterFingerprint_SameInput_ReturnsSameHash()
        {
            var first = GenerationRequestDiagnostics.BuildParameterFingerprint(MakeContext());
            var second = GenerationRequestDiagnostics.BuildParameterFingerprint(MakeContext());

            Assert.Equal(first, second);
        }

        [Fact]
        public void BuildParameterFingerprint_ChangedPromptOrParameters_ReturnsDifferentHash()
        {
            var baseline = MakeContext();
            var changedPrompt = MakeContext();
            changedPrompt.Parameters.TextPrompt = "Сделай больше угловых квартир.";
            var changedMop = MakeContext();
            changedMop.Parameters.MopAreaTarget = 35;

            var baselineHash = GenerationRequestDiagnostics.BuildParameterFingerprint(baseline);

            Assert.NotEqual(baselineHash, GenerationRequestDiagnostics.BuildParameterFingerprint(changedPrompt));
            Assert.NotEqual(baselineHash, GenerationRequestDiagnostics.BuildParameterFingerprint(changedMop));
        }

        [Fact]
        public void BuildParameterFingerprint_IgnoresSecretCustomParameters()
        {
            var first = MakeContext();
            first.Parameters.CustomParameters["api_key"] = "SECRET_ONE";
            first.Parameters.CustomParameters["access_key"] = "ACCESS_ONE";
            first.Parameters.CustomParameters["Authorization"] = "Bearer ONE";
            first.Parameters.CustomParameters["token"] = "TOKEN_ONE";

            var second = MakeContext();
            second.Parameters.CustomParameters["api_key"] = "SECRET_TWO";
            second.Parameters.CustomParameters["access_key"] = "ACCESS_TWO";
            second.Parameters.CustomParameters["Authorization"] = "Bearer TWO";
            second.Parameters.CustomParameters["token"] = "TOKEN_TWO";

            Assert.Equal(
                GenerationRequestDiagnostics.BuildParameterFingerprint(first),
                GenerationRequestDiagnostics.BuildParameterFingerprint(second));

            second.Parameters.CustomParameters["design_note"] = "Новый сценарий";

            Assert.NotEqual(
                GenerationRequestDiagnostics.BuildParameterFingerprint(first),
                GenerationRequestDiagnostics.BuildParameterFingerprint(second));
        }

        [Theory]
        [InlineData("Authorization")]
        [InlineData("access_key")]
        [InlineData("private_key")]
        [InlineData("credential")]
        [InlineData("api_key")]
        [InlineData("token")]
        public void IsSecretKey_RecognizesSecretLikeNames(string key)
        {
            Assert.True(GenerationRequestDiagnostics.IsSecretKey(key));
        }

        [Fact]
        public void BuildPromptFingerprint_SamePromptStableChangedPromptDifferent()
        {
            var prompt = "prompt";

            Assert.Equal(
                GenerationRequestDiagnostics.BuildPromptFingerprint(prompt),
                GenerationRequestDiagnostics.BuildPromptFingerprint(prompt));
            Assert.NotEqual(
                GenerationRequestDiagnostics.BuildPromptFingerprint(prompt),
                GenerationRequestDiagnostics.BuildPromptFingerprint("prompt changed"));
        }

        [Theory]
        [InlineData("https://user:secret@api.example.com:8443/v1?token=abc#frag", "https://api.example.com:8443/v1")]
        [InlineData("https://api.example.com/v1?api_key=abc", "https://api.example.com/v1")]
        [InlineData("http://localhost:1234/v1/", "http://localhost:1234/v1")]
        public void SafeDisplayUrl_RemovesUserInfoQueryAndFragment(string input, string expected)
        {
            Assert.Equal(expected, GenerationRequestDiagnostics.SafeDisplayUrl(input));
        }

        private static GenerationRequestContext MakeContext()
        {
            return new GenerationRequestContext
            {
                RequestId = "req_1",
                Contour = new BuildingContour
                {
                    Id = "contour-1",
                    Name = "Контур",
                    OuterLoop = new List<ContourSegment>
                    {
                        new() { Start = new Point2D(0, 0), End = new Point2D(10, 0) },
                        new() { Start = new Point2D(10, 0), End = new Point2D(10, 8) },
                        new() { Start = new Point2D(10, 8), End = new Point2D(0, 8) },
                        new() { Start = new Point2D(0, 8), End = new Point2D(0, 0) }
                    }
                },
                ProjectContext = new RevitProjectContext
                {
                    LevelId = "1",
                    LevelName = "Level 1",
                    LevelElevationMeters = 0,
                    ActiveViewName = "Plan",
                    ActiveViewType = "FloorPlan",
                    ContourSource = "revit_selection"
                },
                Parameters = new GenerationParameters
                {
                    VariantCount = 2,
                    ValidationMode = ValidationMode.Strict,
                    TextPrompt = "Сделай компактный МОП.",
                    OneRoomCount = 1,
                    TwoRoomCount = 2,
                    ThreeRoomCount = 0,
                    MopAreaTarget = 50,
                    RequiredRoomTypes = new List<RoomType>
                    {
                        RoomType.LivingRoom,
                        RoomType.CommonArea,
                        RoomType.Corridor
                    }
                }
            };
        }
    }
}
