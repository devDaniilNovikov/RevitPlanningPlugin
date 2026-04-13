using System;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace RevitPlanningPlugin.Services.Logging
{
    /// <summary>
    /// Сервис логирования (обертка над NLog).
    /// Маскирует секреты перед записью.
    /// </summary>
    public static class PluginLogger
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static bool _initialized;

        public static void Initialize(string logLevel = "Info")
        {
            if (_initialized) return;

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RevitPlanningPlugin", "Logs");

            Directory.CreateDirectory(logDir);

            var config = new LoggingConfiguration();

            var fileTarget = new FileTarget("file")
            {
                FileName = Path.Combine(logDir, "plugin_${shortdate}.log"),
                Layout = "${longdate} | ${level:uppercase=true:padding=-5} | ${logger:shortName=true} | ${message} ${exception:format=tostring}",
                MaxArchiveFiles = 30,
                ArchiveEvery = FileArchivePeriod.Day
            };

            config.AddTarget(fileTarget);

            var level = LogLevel.FromString(logLevel);
            config.AddRule(level, LogLevel.Fatal, fileTarget);

            LogManager.Configuration = config;
            _initialized = true;
        }

        public static void Info(string message) => Logger.Info(Sanitize(message));
        public static void Debug(string message) => Logger.Debug(Sanitize(message));
        public static void Warn(string message) => Logger.Warn(Sanitize(message));
        public static void Error(string message, Exception? ex = null) => Logger.Error(ex, Sanitize(message));

        public static void ApiRequest(string method, string url, int? statusCode = null)
        {
            var sanitizedUrl = MaskQuerySecrets(url);
            if (statusCode.HasValue)
                Logger.Info($"API {method} {sanitizedUrl} → {statusCode}");
            else
                Logger.Info($"API {method} {sanitizedUrl}");
        }

        /// <summary>Маскировка секретов в строке.</summary>
        private static string Sanitize(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;
            // Маскируем Bearer-токены и API ключи
            message = System.Text.RegularExpressions.Regex.Replace(
                message, @"(Bearer\s+)\S+", "$1***MASKED***");
            message = System.Text.RegularExpressions.Regex.Replace(
                message, @"(api[_-]?key["":\s=]+)\S+", "$1***MASKED***", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return message;
        }

        private static string MaskQuerySecrets(string url)
        {
            return System.Text.RegularExpressions.Regex.Replace(
                url, @"(key|token|secret)=([^&]+)", "$1=***",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }
}
