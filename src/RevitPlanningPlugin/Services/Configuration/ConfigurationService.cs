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
        public string BaseUrl { get; set; } = "https://api.example.com/v1";
        public ApiEnvironment Environment { get; set; } = ApiEnvironment.Development;
        public string ApiKey { get; set; } = string.Empty;
        public string BearerToken { get; set; } = string.Empty;
        public int RequestTimeoutSeconds { get; set; } = 30;
        public int MaxRetries { get; set; } = 3;
        public string SourceUnit { get; set; } = "m";
        public bool AutoValidateContours { get; set; } = true;
        public string LogLevel { get; set; } = "Info";

        /// <summary>
        /// Использовать мок-клиент вместо реального API.
        /// Удобно для разработки и тестирования без доступа к внешнему сервису.
        /// </summary>
        public bool UseMockApi { get; set; } = false;

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

            // Сохраняем копию с зашифрованными секретами
            var toSave = new PluginSettings
            {
                BaseUrl = settings.BaseUrl,
                Environment = settings.Environment,
                ApiKey = EncryptString(settings.ApiKey),
                BearerToken = EncryptString(settings.BearerToken),
                RequestTimeoutSeconds = settings.RequestTimeoutSeconds,
                MaxRetries = settings.MaxRetries,
                SourceUnit = settings.SourceUnit,
                AutoValidateContours = settings.AutoValidateContours,
                LogLevel = settings.LogLevel,
                UseMockApi = settings.UseMockApi
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
            catch
            {
                return plainText; // fallback — не шифруем при ошибке
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
