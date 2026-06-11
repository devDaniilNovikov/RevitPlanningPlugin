using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitPlanningPlugin.Models.Api;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Services.Configuration;
using RevitPlanningPlugin.Services.Logging;

namespace RevitPlanningPlugin.Services.Api
{
    /// <summary>
    /// Клиент AI Tunnel/OpenAI-compatible Chat Completions API.
    /// Контуры берутся из Revit/внешнего API, а генерация выполняется внешним AI-сервисом.
    /// </summary>
    public class LmStudioPlanningApiClient : IPlanningApiClient, IDisposable
    {
        private const string ChatCompletionsPath = "/chat/completions";
        private const string ModelsPath = "/models";

        private readonly HttpClient _http;
        private readonly PluginSettings _settings;

        public LmStudioPlanningApiClient(PluginSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds)
            };

            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            _http.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");

            var authHeader = CreateBearerAuthenticationHeader(!string.IsNullOrWhiteSpace(_settings.BearerToken)
                ? _settings.BearerToken
                : _settings.ApiKey);
            if (authHeader != null)
                _http.DefaultRequestHeaders.Authorization = authHeader;
        }

        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            var models = await SendAsync<LmStudioModelsResponseDto>(HttpMethod.Get, ModelsPath, ct: ct);
            EnsureModelAvailable(models, _settings.LmStudioModel);
            return true;
        }

        public Task<List<ApiContourSummaryDto>> GetContourListAsync(CancellationToken ct = default)
        {
            throw new PlanningApiException(
                "AI Tunnel не хранит контуры. Для production-сценария извлеките контур из текущей Revit-модели или переключите backend на ExternalApi.",
                errorCode: "LM_STUDIO_CONTOURS_NOT_SUPPORTED");
        }

        public Task<BuildingContour> GetContourAsync(string contourId, CancellationToken ct = default)
        {
            throw new PlanningApiException(
                "AI Tunnel не возвращает контуры по ID. Используйте извлечение контура из Revit или backend ExternalApi.",
                errorCode: "LM_STUDIO_CONTOURS_NOT_SUPPORTED");
        }

        public Task<List<LayoutVariant>> GenerateLayoutsAsync(
            string contourId,
            GenerationParameters parameters,
            CancellationToken ct = default)
        {
            throw new PlanningApiException(
                "Для AI Tunnel требуется полный GenerationRequestContext с Revit-контекстом и геометрией контура.",
                errorCode: "LM_STUDIO_CONTEXT_REQUIRED");
        }

        public async Task<List<LayoutVariant>> GenerateLayoutsAsync(
            GenerationRequestContext context,
            CancellationToken ct = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(context.Prompt))
                throw new PlanningApiException("Prompt для AI Tunnel не сформирован.", errorCode: "LM_STUDIO_PROMPT_REQUIRED");

            var request = new LmStudioChatRequestDto
            {
                Model = _settings.LmStudioModel,
                Temperature = _settings.LmStudioTemperature,
                MaxTokens = _settings.LmStudioMaxTokens,
                Stream = false,
                Messages = new List<LmStudioChatMessageDto>
                {
                    new()
                    {
                        Role = "system",
                        Content =
                            "Ты AI-сервис генерации планировок для Revit. " +
                            "Верни только один JSON-объект по указанному контракту, без markdown, без пояснений и без reasoning-текста. " +
                            "Если модель использует thinking mode, не включай <think> или рассуждения в ответ."
                    },
                    new()
                    {
                        Role = "user",
                        Content = context.Prompt
                    }
                }
            };

            var response = await SendChatCompletionAsync(request, ct);

            if (response?.Error != null)
            {
                throw new PlanningApiException(
                    $"AI Tunnel: {response.Error.Message}",
                    errorCode: response.Error.Code ?? "LM_STUDIO_ERROR");
            }

            var content = response?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                throw new PlanningApiException("AI Tunnel вернул пустой ответ.", errorCode: "LM_STUDIO_EMPTY_RESPONSE");

            var json = ExtractJsonObject(content);
            ApiResponse<ApiGenerationResultDto>? envelope;
            try
            {
                envelope = JsonConvert.DeserializeObject<ApiResponse<ApiGenerationResultDto>>(json);
            }
            catch (JsonException ex)
            {
                throw new PlanningApiException(
                    "AI Tunnel вернул текст, который не удалось разобрать как JSON-контракт: " + ex.Message,
                    errorCode: "LM_STUDIO_INVALID_JSON",
                    inner: ex);
            }

            EnsureSuccess(envelope);
            if (envelope?.Data == null)
                throw new PlanningApiException("AI Tunnel не вернул data с результатом генерации.", errorCode: "LM_STUDIO_EMPTY_RESULT");

            if (string.Equals(envelope.Data.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new PlanningApiException(
                    envelope.Data.Error ?? "AI Tunnel не смог сгенерировать валидную планировку.",
                    errorCode: "LM_STUDIO_GENERATION_ERROR");
            }

            ApiResponseContractValidator.ValidateGenerationResult(envelope.Data, context.Parameters.VariantCount);
            PluginLogger.Info(
                $"AI Tunnel response '{envelope.Data.RequestId}' validated: {envelope.Data.Variants.Count} variant(s), model={_settings.LmStudioModel}.");

            return envelope.Data.Variants.Select(DtoMapper.ToDomain).ToList();
        }

        public static string NormalizeBearerToken(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return string.Empty;

            var normalized = StripSurroundingQuotes(token.Trim());
            normalized = Regex.Replace(normalized, @"[\r\n]+", " ").Trim();

            var authorizationMatch = Regex.Match(
                normalized,
                @"Authorization\s*:\s*Bearer\s+([^""'\s]+)",
                RegexOptions.IgnoreCase);
            if (authorizationMatch.Success)
                return authorizationMatch.Groups[1].Value.Trim();

            var bearerMatch = Regex.Match(
                normalized,
                @"^Bearer\s+(.+)$",
                RegexOptions.IgnoreCase);
            if (bearerMatch.Success)
                normalized = bearerMatch.Groups[1].Value.Trim();

            normalized = StripSurroundingQuotes(normalized);

            return normalized;
        }

        public static AuthenticationHeaderValue? CreateBearerAuthenticationHeader(string? token)
        {
            var normalized = NormalizeBearerToken(token);
            return string.IsNullOrWhiteSpace(normalized)
                ? null
                : new AuthenticationHeaderValue("Bearer", normalized);
        }

        private static string StripSurroundingQuotes(string value)
        {
            if (value.Length >= 2
                && ((value[0] == '"' && value[value.Length - 1] == '"')
                    || (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                return value.Substring(1, value.Length - 2).Trim();
            }

            return value;
        }

        public static string ExtractJsonObject(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new PlanningApiException("AI Tunnel вернул пустой ответ.", errorCode: "LM_STUDIO_EMPTY_RESPONSE");

            var trimmed = StripMarkdownFence(StripThinkBlocks(content.Trim()));
            var start = trimmed.IndexOf('{');
            if (start < 0)
                throw new PlanningApiException("В ответе AI Tunnel не найден JSON-объект.", errorCode: "LM_STUDIO_JSON_NOT_FOUND");

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (int i = start; i < trimmed.Length; i++)
            {
                var ch = trimmed[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (ch == '{') depth++;
                if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                        return trimmed.Substring(start, i - start + 1);
                }
            }

            throw new PlanningApiException("JSON-объект в ответе AI Tunnel не закрыт.", errorCode: "LM_STUDIO_JSON_NOT_CLOSED");
        }

        public static void EnsureModelAvailable(LmStudioModelsResponseDto? models, string configuredModel)
        {
            if (models == null)
                throw new PlanningApiException("AI Tunnel вернул пустой список моделей.", errorCode: "LM_STUDIO_MODELS_EMPTY");

            var modelIds = models.Data?
                .Select(m => m.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList() ?? new List<string>();

            if (modelIds.Count == 0)
                throw new PlanningApiException("AI Tunnel доступен, но список моделей пуст.", errorCode: "LM_STUDIO_MODELS_EMPTY");

            if (string.IsNullOrWhiteSpace(configuredModel))
                throw new PlanningApiException("Не задана модель AI Tunnel.", errorCode: "LM_STUDIO_MODEL_REQUIRED");

            if (modelIds.Any(id => string.Equals(id, configuredModel, StringComparison.OrdinalIgnoreCase)))
                return;

            throw new PlanningApiException(
                $"AI Tunnel доступен, но модель '{configuredModel}' не найдена. Доступные модели: {string.Join(", ", modelIds)}.",
                errorCode: "LM_STUDIO_MODEL_NOT_LOADED");
        }

        private async Task<LmStudioChatResponseDto?> SendChatCompletionAsync(
            LmStudioChatRequestDto request,
            CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(request);
            try
            {
                return await SendAsync<LmStudioChatResponseDto>(HttpMethod.Post, ChatCompletionsPath, body, ct);
            }
            catch (PlanningApiException ex) when (IsUnsupportedResponseFormatError(ex))
            {
                PluginLogger.Warn("AI Tunnel не принял response_format=json_object. Повторяем запрос без response_format.");
                request.ResponseFormat = null;
                body = JsonConvert.SerializeObject(request, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
                return await SendAsync<LmStudioChatResponseDto>(HttpMethod.Post, ChatCompletionsPath, body, ct);
            }
        }

        private async Task<T?> SendAsync<T>(
            HttpMethod method,
            string path,
            string? jsonBody = null,
            CancellationToken ct = default)
        {
            Exception? lastException = null;
            var maxRetries = _settings.MaxRetries;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    PluginLogger.Debug($"AI Tunnel retry {attempt}/{maxRetries} через {delay.TotalSeconds}s");
                    await Task.Delay(delay, ct);
                }

                HttpRequestMessage? request = null;
                HttpResponseMessage? response = null;
                try
                {
                    request = new HttpRequestMessage(method, BuildUri(path));
                    if (jsonBody != null)
                        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    PluginLogger.ApiRequest(method.Method, BuildUri(path).ToString());
                    response = await _http.SendAsync(request, ct);
                    PluginLogger.ApiRequest(method.Method, path, (int)response.StatusCode);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorBody = await response.Content.ReadAsStringAsync();
                        var statusCode = (int)response.StatusCode;

                        if (statusCode == 429 || statusCode >= 500)
                        {
                            lastException = new PlanningApiException(
                                $"AI Tunnel HTTP {statusCode}: {TruncateBody(errorBody)}",
                                statusCode,
                                "LM_STUDIO_HTTP_ERROR");
                            continue;
                        }

                        throw new PlanningApiException(
                            $"AI Tunnel HTTP {statusCode}: {TruncateBody(errorBody)}",
                            statusCode,
                            "LM_STUDIO_HTTP_ERROR");
                    }

                    var content = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(content))
                        throw new PlanningApiException("Пустой ответ от AI Tunnel.", errorCode: "LM_STUDIO_EMPTY_RESPONSE");

                    var result = JsonConvert.DeserializeObject<T>(content);
                    if (result == null)
                        throw new PlanningApiException("Невалидный JSON от AI Tunnel.", errorCode: "LM_STUDIO_INVALID_JSON");

                    return result;
                }
                catch (PlanningApiException) when (attempt == maxRetries)
                {
                    throw;
                }
                catch (PlanningApiException ex) when (ex.StatusCode >= 500 || ex.StatusCode == 429)
                {
                    lastException = ex;
                    continue;
                }
                catch (JsonException ex)
                {
                    throw new PlanningApiException("Невалидный JSON от AI Tunnel.", errorCode: "LM_STUDIO_INVALID_JSON", inner: ex);
                }
                catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    lastException = new PlanningApiException("Таймаут запроса к AI Tunnel.", errorCode: "LM_STUDIO_TIMEOUT", inner: ex);
                    if (attempt == maxRetries) throw lastException;
                    continue;
                }
                catch (HttpRequestException ex)
                {
                    lastException = new PlanningApiException("Сетевая ошибка AI Tunnel.", errorCode: "LM_STUDIO_NETWORK_ERROR", inner: ex);
                    if (attempt == maxRetries) throw lastException;
                    continue;
                }
                finally
                {
                    request?.Dispose();
                    response?.Dispose();
                }
            }

            throw lastException ?? new PlanningApiException("Неизвестная ошибка AI Tunnel.", errorCode: "LM_STUDIO_UNKNOWN_ERROR");
        }

        private static void EnsureSuccess<T>(ApiResponse<T>? response)
        {
            if (response == null)
                throw new PlanningApiException("Нулевой JSON-конверт от AI Tunnel.", errorCode: "LM_STUDIO_NULL_ENVELOPE");
            if (!response.Success && response.Error != null)
                throw new PlanningApiException(response.Error.Message, errorCode: response.Error.Code);
            if (!response.Success)
                throw new PlanningApiException("AI Tunnel вернул success=false без описания ошибки.", errorCode: "LM_STUDIO_API_ERROR");
        }

        private Uri BuildUri(string path)
            => new Uri(_settings.LmStudioBaseUrl.TrimEnd('/') + "/" + path.TrimStart('/'));

        private static string StripMarkdownFence(string content)
        {
            if (!content.StartsWith("```", StringComparison.Ordinal))
                return content;

            var firstLineEnd = content.IndexOf('\n');
            if (firstLineEnd < 0)
                return content;

            var lastFence = content.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence <= firstLineEnd)
                return content.Substring(firstLineEnd + 1);

            return content.Substring(firstLineEnd + 1, lastFence - firstLineEnd - 1).Trim();
        }

        private static string StripThinkBlocks(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            return Regex.Replace(
                    content,
                    @"<think\b[^>]*>.*?</think>",
                    string.Empty,
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Trim();
        }

        private static bool IsUnsupportedResponseFormatError(PlanningApiException ex)
        {
            if (ex.StatusCode != 400 && ex.StatusCode != 422)
                return false;

            var message = ex.Message?.ToLowerInvariant() ?? string.Empty;
            return message.Contains("response_format")
                   || message.Contains("json_object")
                   || message.Contains("unsupported")
                   || message.Contains("unknown field")
                   || message.Contains("extra_forbidden");
        }

        private static string TruncateBody(string? body, int maxLen = 500)
            => string.IsNullOrEmpty(body) ? "(пусто)"
                : body.Length <= maxLen ? body
                : body.Substring(0, maxLen) + "...";

        public void Dispose() => _http.Dispose();
    }
}
