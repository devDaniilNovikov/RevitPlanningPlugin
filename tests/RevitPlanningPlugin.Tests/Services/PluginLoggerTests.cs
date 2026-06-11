using RevitPlanningPlugin.Services.Logging;
using Xunit;

namespace RevitPlanningPlugin.Tests.Services
{
    public class PluginLoggerTests
    {
        [Theory]
        [InlineData("https://user:pass@example.com/v1?api_key=x#frag", "https://example.com/v1")]
        [InlineData("https://api.example.com/v1/chat/completions?token=x", "https://api.example.com/v1/chat/completions")]
        public void SanitizeUrlForLog_RemovesUserInfoQueryAndFragment(string input, string expected)
        {
            Assert.Equal(expected, PluginLogger.SanitizeUrlForLog(input));
        }
    }
}
