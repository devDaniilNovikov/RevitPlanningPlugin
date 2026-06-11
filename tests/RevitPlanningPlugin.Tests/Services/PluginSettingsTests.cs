using RevitPlanningPlugin.Services.Configuration;
using Xunit;

namespace RevitPlanningPlugin.Tests.Services
{
    public class PluginSettingsTests
    {
        [Fact]
        public void NormalizeDefaults_ReplacesLegacyLocalModel()
        {
            var settings = new PluginSettings
            {
                LmStudioModel = "qwen3-7b"
            };

            settings.NormalizeDefaults();

            Assert.Equal("gemma-4-31b-it", settings.LmStudioModel);
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
        public void NormalizeDefaults_KeepsCustomAiTunnelModel()
        {
            var settings = new PluginSettings
            {
                LmStudioBaseUrl = "https://api.aitunnel.ru/v1",
                LmStudioModel = "gpt-5-mini"
            };

            settings.NormalizeDefaults();

            Assert.Equal("gpt-5-mini", settings.LmStudioModel);
        }

        [Fact]
        public void Defaults_AreProductionAiTunnelSettings()
        {
            var settings = new PluginSettings();

            Assert.Equal("https://api.aitunnel.ru/v1", settings.LmStudioBaseUrl);
            Assert.Equal("gemma-4-31b-it", settings.LmStudioModel);
            Assert.Equal(0.1, settings.LmStudioTemperature);
            Assert.Equal(12000, settings.LmStudioMaxTokens);
            Assert.Equal(180, settings.RequestTimeoutSeconds);
        }

        [Fact]
        public void NormalizeDefaults_ReplacesLegacyLocalBaseUrl()
        {
            var settings = new PluginSettings
            {
                LmStudioBaseUrl = "http://localhost:1234/v1",
                LmStudioModel = "google/gemma-4-e4b"
            };

            settings.NormalizeDefaults();

            Assert.Equal("https://api.aitunnel.ru/v1", settings.LmStudioBaseUrl);
            Assert.Equal("gemma-4-31b-it", settings.LmStudioModel);
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
