using RevitPlanningPlugin.Services.Configuration;
using Xunit;

namespace RevitPlanningPlugin.Tests.Services
{
    public class PluginSettingsTests
    {
        [Fact]
        public void NormalizeDefaults_ReplacesLegacyLmStudioDefault()
        {
            var settings = new PluginSettings
            {
                LmStudioModel = "qwen3-7b"
            };

            settings.NormalizeDefaults();

            Assert.Equal("google/gemma-4-e4b", settings.LmStudioModel);
        }

        [Fact]
        public void NormalizeDefaults_KeepsCustomLmStudioModel()
        {
            var settings = new PluginSettings
            {
                LmStudioModel = "custom/local-model"
            };

            settings.NormalizeDefaults();

            Assert.Equal("custom/local-model", settings.LmStudioModel);
        }
    }
}
