using RevitPlanningPlugin.Services.Api;
using RevitPlanningPlugin.Models.Api;
using Xunit;

namespace RevitPlanningPlugin.Tests.Services
{
    public class LmStudioPlanningApiClientTests
    {
        [Fact]
        public void ExtractJsonObject_FromPlainJson_ReturnsObject()
        {
            var json = "{\"success\":true,\"data\":{\"request_id\":\"r1\"}}";

            var result = LmStudioPlanningApiClient.ExtractJsonObject(json);

            Assert.Equal(json, result);
        }

        [Fact]
        public void ExtractJsonObject_FromMarkdownFence_ReturnsObject()
        {
            var response = "```json\n{\"success\":true,\"data\":{\"request_id\":\"r1\"}}\n```";

            var result = LmStudioPlanningApiClient.ExtractJsonObject(response);

            Assert.Equal("{\"success\":true,\"data\":{\"request_id\":\"r1\"}}", result);
        }

        [Fact]
        public void ExtractJsonObject_FromReasoningPrefix_ReturnsFirstObject()
        {
            var response = "<think>Проверяю планировку</think>\n{\"success\":true,\"data\":{\"request_id\":\"r1\"}}\nГотово";

            var result = LmStudioPlanningApiClient.ExtractJsonObject(response);

            Assert.Equal("{\"success\":true,\"data\":{\"request_id\":\"r1\"}}", result);
        }

        [Fact]
        public void ExtractJsonObject_IgnoresJsonInsideThinkBlock()
        {
            var response = "<think>{\"draft\":true}</think>\n{\"success\":true,\"data\":{\"request_id\":\"r1\"}}";

            var result = LmStudioPlanningApiClient.ExtractJsonObject(response);

            Assert.Equal("{\"success\":true,\"data\":{\"request_id\":\"r1\"}}", result);
        }

        [Fact]
        public void ExtractJsonObject_NoJson_ThrowsPlanningApiException()
        {
            var exception = Assert.Throws<PlanningApiException>(
                () => LmStudioPlanningApiClient.ExtractJsonObject("не json"));

            Assert.Equal("LM_STUDIO_JSON_NOT_FOUND", exception.ErrorCode);
        }

        [Fact]
        public void EnsureModelAvailable_WhenConfiguredModelExists_Passes()
        {
            var models = new LmStudioModelsResponseDto
            {
                Data =
                {
                    new LmStudioModelDto { Id = "gemma-4-31b-it" }
                }
            };

            LmStudioPlanningApiClient.EnsureModelAvailable(models, "GEMMA-4-31B-IT");
        }

        [Fact]
        public void EnsureModelAvailable_WhenConfiguredModelMissing_ThrowsClearError()
        {
            var models = new LmStudioModelsResponseDto
            {
                Data =
                {
                    new LmStudioModelDto { Id = "loaded-model" }
                }
            };

            var exception = Assert.Throws<PlanningApiException>(
                () => LmStudioPlanningApiClient.EnsureModelAvailable(models, "gemma-4-31b-it"));

            Assert.Equal("LM_STUDIO_MODEL_NOT_LOADED", exception.ErrorCode);
            Assert.Contains("loaded-model", exception.Message);
        }

        [Theory]
        [InlineData("tok", "tok")]
        [InlineData(" Bearer tok ", "tok")]
        [InlineData("Authorization: Bearer tok", "tok")]
        [InlineData("\"Authorization: Bearer tok\"", "tok")]
        [InlineData("'Bearer tok'", "tok")]
        [InlineData("-H \"Authorization: Bearer tok\"", "tok")]
        [InlineData("Authorization:\r\n Bearer tok", "tok")]
        public void NormalizeBearerToken_StripsHeaderSyntax(string input, string expected)
        {
            var token = LmStudioPlanningApiClient.NormalizeBearerToken(input);

            Assert.Equal(expected, token);
        }

        [Fact]
        public void CreateBearerAuthenticationHeader_UsesNormalizedToken()
        {
            var header = LmStudioPlanningApiClient.CreateBearerAuthenticationHeader(
                "Authorization: Bearer tok");

            Assert.NotNull(header);
            Assert.Equal("Bearer", header!.Scheme);
            Assert.Equal("tok", header.Parameter);
        }
    }
}
