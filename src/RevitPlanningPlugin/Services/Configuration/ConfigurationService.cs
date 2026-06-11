using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Services.Configuration
{
    /// <summary>
    /// Настройки плагина, сериализуемые в JSON.
    /// </summary>
    public class PluginSettings
    {
        public const string DefaultAiServiceBaseUrl = "https://api.aitunnel.ru/v1";
        public const string DefaultAiServiceModel = "gemma-4-31b-it";
        public const double DefaultAiServiceTemperature = 0.1;
        public const int DefaultAiServiceMaxTokens = 12000;
        public const string DefaultLmStudioModel = DefaultAiServiceModel;
        public const double DefaultLmStudioTemperature = DefaultAiServiceTemperature;
        public const int DefaultLmStudioMaxTokens = DefaultAiServiceMaxTokens;
        public const int DefaultRequestTimeoutSeconds = 180;

        public GenerationBackend Backend { get; set; } = GenerationBackend.LmStudio;
        public string LmStudioBaseUrl { get; set; } = DefaultAiServiceBaseUrl;
        public string LmStudioModel { get; set; } = DefaultLmStudioModel;
        public double LmStudioTemperature { get; set; } = DefaultLmStudioTemperature;
        public int LmStudioMaxTokens { get; set; } = DefaultLmStudioMaxTokens;
        public string BaseUrl { get; set; } = "https://api.example.com/v1";
        public ApiEnvironment Environment { get; set; } = ApiEnvironment.Production;
        public string ApiKey { get; set; } = string.Empty;
        public string BearerToken { get; set; } = string.Empty;
        public int RequestTimeoutSeconds { get; set; } = DefaultRequestTimeoutSeconds;
        public int MaxRetries { get; set; } = 3;
        public string SourceUnit { get; set; } = "m";
        public bool AutoValidateContours { get; set; } = true;
        public string LogLevel { get; set; } = "Info";

        /// <summary>
        /// Использовать мок-клиент вместо реального API.
        /// Удобно для разработки и тестирования без доступа к внешнему сервису.
        /// </summary>
        public bool UseMockApi { get; set; } = false;

        /// <summary>
        /// Сценарий поведения мок-клиента для воспроизводимой дипломной демонстрации.
        /// </summary>
        public MockScenario MockScenario { get; set; } = MockScenario.HappyPath;

        /// <summary>
        /// Обратная совместимость со старым флагом mock-режима.
        /// </summary>
        [JsonIgnore]
        public GenerationBackend EffectiveBackend => UseMockApi ? GenerationBackend.Mock : Backend;

        /// <summary>Базовый URL для выбранного окружения.</summary>
        [JsonIgnore]
        public string EffectiveBaseUrl
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(BaseUrl))
                    return BaseUrl.TrimEnd('/');
                return Environment switch
                {
                    ApiEnvironment.Production => "https://api.planning.example.com/v1",
                    ApiEnvironment.Staging => "https://api-stage.planning.example.com/v1",
                    _ => "https://api-dev.planning.example.com/v1"
                };
            }
        }

        public void NormalizeDefaults()
        {
            if (string.IsNullOrWhiteSpace(LmStudioBaseUrl) || IsLegacyLocalBaseUrl(LmStudioBaseUrl))
                LmStudioBaseUrl = DefaultAiServiceBaseUrl;

            if (string.IsNullOrWhiteSpace(LmStudioModel)
                || string.Equals(LmStudioModel, "qwen3-7b", StringComparison.OrdinalIgnoreCase)
                || string.Equals(LmStudioModel, "google/gemma-4-e4b", StringComparison.OrdinalIgnoreCase))
            {
                LmStudioModel = DefaultAiServiceModel;
            }

            if (LmStudioTemperature <= 0
                || LmStudioTemperature > 1
                || Math.Abs(LmStudioTemperature - 0.2) < 0.0001)
            {
                LmStudioTemperature = DefaultAiServiceTemperature;
            }

            if (LmStudioMaxTokens < DefaultAiServiceMaxTokens)
                LmStudioMaxTokens = DefaultAiServiceMaxTokens;

            if (RequestTimeoutSeconds < DefaultRequestTimeoutSeconds)
                RequestTimeoutSeconds = DefaultRequestTimeoutSeconds;
        }

        private static bool IsLegacyLocalBaseUrl(string baseUrl)
        {
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
                return true;

            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Сервис конфигурации: загрузка/сохранение настроек с шифрованием секретов.
    /// </summary>
    public class ConfigurationService
    {
        private static readonly string ConfigDir =
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "RevitPlanningPlugin");

        private static readonly string ConfigPath = Path.Combine(ConfigDir, "settings.json");
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("RevitPlanningPlugin_v1");

        private PluginSettings? _cached;

        public PluginSettings Load()
        {
            if (_cached != null) return _cached;

            if (!File.Exists(ConfigPath))
            {
                _cached = new PluginSettings();
                return _cached;
            }

            try
            {
                var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                var settings = JsonConvert.DeserializeObject<PluginSettings>(json) ?? new PluginSettings();

                // Расшифровка секретов (DPAPI, только Windows)
                settings.ApiKey = DecryptString(settings.ApiKey);
                settings.BearerToken = DecryptString(settings.BearerToken);
                settings.NormalizeDefaults();
                if (settings.UseMockApi)
                {
                    settings.Backend = GenerationBackend.Mock;
                    settings.UseMockApi = false;
                }

                _cached = settings;
                return settings;
            }
            catch
            {
                _cached = new PluginSettings();
                return _cached;
            }
        }

        public void Save(PluginSettings settings)
        {
            Directory.CreateDirectory(ConfigDir);
            settings.NormalizeDefaults();

            // Сохраняем копию с зашифрованными секретами
            var toSave = new PluginSettings
            {
                BaseUrl = settings.BaseUrl,
                Backend = settings.Backend,
                LmStudioBaseUrl = settings.LmStudioBaseUrl,
                LmStudioModel = settings.LmStudioModel,
                LmStudioTemperature = settings.LmStudioTemperature,
                LmStudioMaxTokens = settings.LmStudioMaxTokens,
                Environment = settings.Environment,
                ApiKey = EncryptString(settings.ApiKey),
                BearerToken = EncryptString(settings.BearerToken),
                RequestTimeoutSeconds = settings.RequestTimeoutSeconds,
                MaxRetries = settings.MaxRetries,
                SourceUnit = settings.SourceUnit,
                AutoValidateContours = settings.AutoValidateContours,
                LogLevel = settings.LogLevel,
                UseMockApi = false,
                MockScenario = settings.MockScenario
            };

            var json = JsonConvert.SerializeObject(toSave, Formatting.Indented);
            File.WriteAllText(ConfigPath, json, Encoding.UTF8);

            _cached = settings;
        }

        public void ClearCache() => _cached = null;

        // ——— DPAPI encryption helpers ———

        private static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            try
            {
                var data = Encoding.UTF8.GetBytes(plainText);
                var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Не удалось зашифровать секреты настроек через DPAPI. Настройки не сохранены, чтобы не записать API-ключ открытым текстом.",
                    ex);
            }
        }

        private static string DecryptString(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText)) return string.Empty;
            try
            {
                var data = Convert.FromBase64String(encryptedText);
                var decrypted = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return encryptedText; // возможно, строка ещё не зашифрована
            }
        }
    }
}
