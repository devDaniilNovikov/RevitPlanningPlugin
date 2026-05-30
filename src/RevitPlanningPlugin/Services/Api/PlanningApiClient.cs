using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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
    /// Исключение, специфичное для API плагина.
    /// </summary>
    public class PlanningApiException : Exception
    {
        public int? StatusCode { get; }
        public string? ErrorCode { get; }

        public PlanningApiException(string message, int? statusCode = null, string? errorCode = null, Exception? inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// Интерфейс API-клиента для тестируемости.
    /// </summary>
    public interface IPlanningApiClient
    {
        Task<List<ApiContourSummaryDto>> GetContourListAsync(CancellationToken ct = default);
        Task<BuildingContour> GetContourAsync(string contourId, CancellationToken ct = default);
        Task<List<LayoutVariant>> GenerateLayoutsAsync(string contourId, GenerationParameters parameters, CancellationToken ct = default);
        Task<List<LayoutVariant>> GenerateLayoutsAsync(GenerationRequestContext context, CancellationToken ct = default);
        Task<bool> TestConnectionAsync(CancellationToken ct = default);
    }

    /// <summary>
    /// REST-клиент для внешнего API контуров и генерации.
    /// Поддерживает retry, таймауты, журналирование.
    /// </summary>
    public class PlanningApiClient : IPlanningApiClient, IDisposable
    {
        private readonly HttpClient _http;
        private readonly PluginSettings _settings;

        public PlanningApiClient(PluginSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri(_settings.EffectiveBaseUrl),
                Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds)
            };

            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Авторизация
            if (!string.IsNullOrWhiteSpace(_settings.BearerToken))
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _settings.BearerToken);

            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
                _http.DefaultRequestHeaders.Add("X-API-Key", _settings.ApiKey);
        }

        // ——— Публичные методы ———

        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            await SendAsync<object>(HttpMethod.Get, "/health", ct: ct);
            return true;
        }

        public async Task<List<ApiContourSummaryDto>> GetContourListAsync(CancellationToken ct = default)
        {
            var response = await SendAsync<ApiResponse<ApiContourListDto>>(HttpMethod.Get, "/contours", ct: ct);
            EnsureSuccess(response);
            return response?.Data?.Contours ?? new List<ApiContourSummaryDto>();
        }

        public async Task<BuildingContour> GetContourAsync(string contourId, CancellationToken ct = default)
        {
            var response = await SendAsync<ApiResponse<ApiContourDto>>(
                HttpMethod.Get, $"/contours/{Uri.EscapeDataString(contourId)}", ct: ct);
            EnsureSuccess(response);

            if (response?.Data == null)
                throw new PlanningApiException("Контур не найден.", 404, "CONTOUR_NOT_FOUND");

            return DtoMapper.ToDomain(response.Data);
        }

        public async Task<List<LayoutVariant>> GenerateLayoutsAsync(
            string contourId, GenerationParameters parameters, CancellationToken ct = default)
        {
            var requestDto = DtoMapper.ToDto(contourId, parameters);
            return await GenerateLayoutsCoreAsync(requestDto, ct);
        }

        public async Task<List<LayoutVariant>> GenerateLayoutsAsync(
            GenerationRequestContext context, CancellationToken ct = default)
        {
            var requestDto = DtoMapper.ToDto(context);
            return await GenerateLayoutsCoreAsync(requestDto, ct);
        }

        private async Task<List<LayoutVariant>> GenerateLayoutsCoreAsync(
            ApiGenerationRequestDto requestDto, CancellationToken ct)
        {
            var body = JsonConvert.SerializeObject(requestDto);

            var response = await SendAsync<ApiResponse<ApiGenerationResultDto>>(
                HttpMethod.Post, "/generate", body, ct);
            EnsureSuccess(response);

            if (response?.Data == null)
                throw new PlanningApiException("Пустой результат генерации.", errorCode: "EMPTY_RESULT");

            if (response.Data.Status == "error")
                throw new PlanningApiException(
                    response.Data.Error ?? "Ошибка генерации.", errorCode: "GENERATION_ERROR");

            ApiResponseContractValidator.ValidateGenerationResult(response.Data, requestDto.VariantCount);
            PluginLogger.Info(
                $"AI generation response '{response.Data.RequestId}' validated: {response.Data.Variants.Count} variant(s).");

            return response.Data.Variants?.Select(DtoMapper.ToDomain).ToList()
                   ?? new List<LayoutVariant>();
        }

        // ——— Внутренние методы с retry ———

        private async Task<T?> SendAsync<T>(HttpMethod method, string path,
            string? jsonBody = null, CancellationToken ct = default)
        {
            Exception? lastException = null;
            var maxRetries = _settings.MaxRetries;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    // Экспоненциальная задержка: 1s, 2s, 4s …
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                    PluginLogger.Debug($"Retry {attempt}/{maxRetries} через {delay.TotalSeconds}s");
                    await Task.Delay(delay, ct);
                }

                HttpRequestMessage? request = null;
                HttpResponseMessage? response = null;
                try
                {
                    request = new HttpRequestMessage(method, BuildUri(path));
                    if (jsonBody != null)
                        request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    PluginLogger.ApiRequest(method.Method, _settings.EffectiveBaseUrl + path);
                    response = await _http.SendAsync(request, ct);

                    PluginLogger.ApiRequest(method.Method, path, (int)response.StatusCode);

                    // Не retry для клиентских ошибок (кроме 429)
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorBody = await response.Content.ReadAsStringAsync();
                        var statusCode = (int)response.StatusCode;

                        if (statusCode == 401 || statusCode == 403)
                            throw new PlanningApiException(
                                "Ошибка авторизации. Проверьте API-ключ или токен.", statusCode, "AUTH_ERROR");

                        if (statusCode == 404)
                            throw new PlanningApiException(
                                "Ресурс не найден.", statusCode, "NOT_FOUND");

                        if (statusCode == 429 || statusCode >= 500)
                        {
                            lastException = new PlanningApiException(
                                $"HTTP {statusCode}: {TruncateBody(errorBody)}", statusCode);
                            continue; // retry
                        }

                        throw new PlanningApiException(
                            $"HTTP {statusCode}: {TruncateBody(errorBody)}", statusCode);
                    }

                    var content = await response.Content.ReadAsStringAsync();

                    if (typeof(T) == typeof(object))
                        return default; // health check — нет тела

                    if (string.IsNullOrWhiteSpace(content))
                        throw new PlanningApiException("Пустой ответ от API.", errorCode: "EMPTY_RESPONSE");

                    var result = JsonConvert.DeserializeObject<T>(content);
                    if (result == null)
                        throw new PlanningApiException("Невалидный JSON от API.", errorCode: "INVALID_JSON");

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
                    throw new PlanningApiException("Невалидный JSON от API.", errorCode: "INVALID_JSON", inner: ex);
                }
                catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    lastException = new PlanningApiException("Таймаут запроса к API.", inner: ex);
                    if (attempt == maxRetries) throw lastException;
                    continue;
                }
                catch (HttpRequestException ex)
                {
                    lastException = new PlanningApiException("Сетевая ошибка.", inner: ex);
                    if (attempt == maxRetries) throw lastException;
                    continue;
                }
                finally
                {
                    request?.Dispose();
                    response?.Dispose();
                }
            }

            throw lastException ?? new PlanningApiException("Неизвестная ошибка API.");
        }

        private static void EnsureSuccess<T>(ApiResponse<T>? response)
        {
            if (response == null)
                throw new PlanningApiException("Нулевой ответ от API.");
            if (!response.Success && response.Error != null)
                throw new PlanningApiException(
                    response.Error.Message, errorCode: response.Error.Code);
            if (!response.Success)
                throw new PlanningApiException("API вернул success=false без описания ошибки.", errorCode: "API_ERROR");
        }

        private static string TruncateBody(string? body, int maxLen = 500)
            => string.IsNullOrEmpty(body) ? "(пусто)"
                : body.Length <= maxLen ? body
                : body.Substring(0, maxLen) + "…";

        private Uri BuildUri(string path)
            => new Uri(_settings.EffectiveBaseUrl.TrimEnd('/') + "/" + path.TrimStart('/'));

        public void Dispose() => _http?.Dispose();
    }
}
