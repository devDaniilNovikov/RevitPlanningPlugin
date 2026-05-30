using System;
using System.Collections.Generic;
using System.Linq;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Services.Geometry
{
    /// <summary>
    /// Проверяет пользовательские параметры до отправки запроса во внешний AI-сервис.
    /// </summary>
    public class GenerationInputValidator
    {
        private const int MaxPromptLength = 4000;

        public ValidationResult Validate(
            GenerationParameters parameters,
            BuildingContour? contour,
            string? requiredRoomTypesText)
        {
            var result = new ValidationResult();

            if (contour == null)
                result.AddError("Не выбран контур генерации.", "CONTOUR_REQUIRED");

            if (parameters.VariantCount < 1 || parameters.VariantCount > 20)
                result.AddError("Количество вариантов должно быть в диапазоне 1-20.", "VARIANT_COUNT_OUT_OF_RANGE");

            ValidateNonNegativeCounts(parameters, result);
            ValidateAreaRange(parameters.MinApartmentArea, parameters.MaxApartmentArea,
                "площади квартиры", "APARTMENT_AREA_RANGE_INVALID", result);
            ValidateAreaRange(parameters.MinRoomArea, parameters.MaxRoomArea,
                "площади помещения", "ROOM_AREA_RANGE_INVALID", result);

            if (parameters.MopAreaTarget < 0)
                result.AddError("Целевая площадь МОП не может быть отрицательной.", "MOP_AREA_NEGATIVE");

            if (parameters.MinCorridorWidth < 0)
                result.AddError("Минимальная ширина коридора МОП не может быть отрицательной.", "MOP_WIDTH_NEGATIVE");

            if (!string.IsNullOrEmpty(parameters.TextPrompt) && parameters.TextPrompt.Length > MaxPromptLength)
                result.AddError($"Текстовый prompt слишком длинный: максимум {MaxPromptLength} символов.", "PROMPT_TOO_LONG");

            ValidateRequiredRoomTypeTokens(requiredRoomTypesText, result);
            ValidateFeasibility(parameters, contour, result);

            if (LooksLikePromptInjection(parameters.TextPrompt))
            {
                result.AddWarning(
                    "Текстовый prompt содержит инструкции, похожие на попытку переопределить системные правила. Он будет передан как пользовательское требование, а не как управляющая инструкция.",
                    "PROMPT_INJECTION_PATTERN");
            }

            return result;
        }

        private static void ValidateNonNegativeCounts(GenerationParameters parameters, ValidationResult result)
        {
            var counts = new Dictionary<string, int>
            {
                ["студий"] = parameters.StudioCount,
                ["1-комнатных квартир"] = parameters.OneRoomCount,
                ["2-комнатных квартир"] = parameters.TwoRoomCount,
                ["3-комнатных квартир"] = parameters.ThreeRoomCount,
                ["4-комнатных квартир"] = parameters.FourRoomCount
            };

            foreach (var kv in counts.Where(kv => kv.Value < 0))
                result.AddError($"Количество {kv.Key} не может быть отрицательным.", "APARTMENT_COUNT_NEGATIVE");

            if (parameters.GenerationType == GenerationType.Residential
                && counts.Values.All(v => v == 0))
            {
                result.AddError("Для жилой генерации должен быть задан хотя бы один тип квартиры.", "APARTMENT_PROGRAM_EMPTY");
            }
        }

        private static void ValidateAreaRange(
            double min,
            double max,
            string label,
            string code,
            ValidationResult result)
        {
            if (min < 0 || max < 0)
            {
                result.AddError($"Ограничения {label} не могут быть отрицательными.", code);
                return;
            }

            if (min > 0 && max > 0 && min > max)
                result.AddError($"Минимальное значение {label} не может быть больше максимального.", code);
        }

        private static void ValidateRequiredRoomTypeTokens(string? text, ValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var tokens = text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            var unknown = new List<string>();
            foreach (var token in tokens.Select(t => t.Trim()).Where(t => t.Length > 0))
            {
                if (!Enum.TryParse<RoomType>(NormalizeRoomType(token), true, out _))
                    unknown.Add(token);
            }

            if (unknown.Count > 0)
            {
                result.AddError(
                    $"Неизвестные типы помещений: {string.Join(", ", unknown)}. Используйте значения RoomType или общепринятые алиасы: МОП, common_area.",
                    "ROOM_TYPE_UNKNOWN");
            }
        }

        private static void ValidateFeasibility(
            GenerationParameters parameters,
            BuildingContour? contour,
            ValidationResult result)
        {
            if (contour == null || contour.ApproximateArea <= 0)
                return;

            var requestedMinimum = parameters.TotalApartmentsRequested * Math.Max(parameters.MinApartmentArea, 0)
                                   + Math.Max(parameters.MopAreaTarget, 0);
            if (requestedMinimum > contour.ApproximateArea * 1.05)
            {
                result.AddError(
                    $"Минимально требуемая площадь {requestedMinimum:F1} м² превышает площадь контура {contour.ApproximateArea:F1} м².",
                    "PROGRAM_AREA_EXCEEDS_CONTOUR");
            }
        }

        private static string NormalizeRoomType(string token)
        {
            return token.ToLowerInvariant() switch
            {
                "mop" or "моп" or "common_area" or "common area" => nameof(RoomType.CommonArea),
                "living_room" or "living room" => nameof(RoomType.LivingRoom),
                "meeting_room" or "meeting room" => nameof(RoomType.MeetingRoom),
                "open_space" or "open space" => nameof(RoomType.OpenSpace),
                _ => token
            };
        }

        private static bool LooksLikePromptInjection(string? prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return false;

            var normalized = prompt.ToLowerInvariant();
            return normalized.Contains("ignore previous")
                   || normalized.Contains("ignore all")
                   || normalized.Contains("system prompt")
                   || normalized.Contains("developer message")
                   || normalized.Contains("игнорируй предыдущ")
                   || normalized.Contains("забудь инструкц");
        }
    }
}
