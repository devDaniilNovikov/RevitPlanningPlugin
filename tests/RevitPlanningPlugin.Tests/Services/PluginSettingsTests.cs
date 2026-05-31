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

        [Fact]
        public void Defaults_AreProductionLmStudioSettings()
        {
            var settings = new PluginSettings();

            Assert.Equal("google/gemma-4-e4b", settings.LmStudioModel);
            Assert.Equal(0.1, settings.LmStudioTemperature);
            Assert.Equal(12000, settings.LmStudioMaxTokens);
            Assert.Equal(180, settings.RequestTimeoutSeconds);
        }

        [Fact]
        public void NormalizeDefaults_UpgradesShortLmStudioLimits()
        {
            var settings = new PluginSettings
            {
                LmStudioTemperature = 0.2,
                LmStudioMaxTokens = 8192,
                RequestTimeoutSeconds = 30
            };

            settings.NormalizeDefaults();

            Assert.Equal(0.1, settings.LmStudioTemperature);
            Assert.Equal(12000, settings.LmStudioMaxTokens);
            Assert.Equal(180, settings.RequestTimeoutSeconds);
        }
    }
}
