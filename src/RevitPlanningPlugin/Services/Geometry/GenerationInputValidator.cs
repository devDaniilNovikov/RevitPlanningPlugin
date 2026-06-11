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
            ValidateApartmentTypeMaxAreas(parameters, result);
            ValidateAreaRange(parameters.MinRoomArea, parameters.MaxRoomArea,
                "площади помещения", "ROOM_AREA_RANGE_INVALID", result);

            if (parameters.MopAreaTarget < 0)
                result.AddError("Целевая площадь МОП не может быть отрицательной.", "MOP_AREA_NEGATIVE");

            if (parameters.MinCorridorWidth < 0)
                result.AddError("Минимальная ширина коридора МОП не может быть отрицательной.", "MOP_WIDTH_NEGATIVE");

            if (!string.IsNullOrEmpty(parameters.TextPrompt) && parameters.TextPrompt.Length > MaxPromptLength)
                result.AddError($"Текстовое задание слишком длинное: максимум {MaxPromptLength} символов.", "PROMPT_TOO_LONG");

            ValidateRequiredRoomTypeTokens(requiredRoomTypesText, result);
            ValidateFeasibility(parameters, contour, result);

            if (LooksLikePromptInjection(parameters.TextPrompt))
            {
                result.AddWarning(
                    "Текстовое задание содержит инструкции, похожие на попытку переопределить системные правила. Оно будет передано как пользовательское требование, а не как управляющая инструкция.",
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

            var positiveCounts = counts.Where(kv => kv.Value > 0).ToList();
            if (parameters.GenerationType == GenerationType.Residential
                && positiveCounts.Count == 0)
            {
                var message = parameters.PlanningDetailMode == PlanningDetailMode.ApartmentRooms
                    ? "Для режима планировки квартиры должен быть задан тип одной квартиры."
                    : "Для жилой генерации должен быть задан хотя бы один тип квартиры.";
                result.AddError(message, "APARTMENT_PROGRAM_EMPTY");
            }

            if (parameters.PlanningDetailMode == PlanningDetailMode.ApartmentRooms
                && positiveCounts.Count > 1)
            {
                result.AddWarning(
                    "В режиме планировки квартиры генерируется только одна квартира. Тип будет выбран по первому положительному счетчику: Студия, 1К, 2К, 3К, 4К.",
                    "APARTMENT_MODE_MULTIPLE_TYPES");
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

        private static void ValidateApartmentTypeMaxAreas(GenerationParameters parameters, ValidationResult result)
        {
            var limits = new Dictionary<string, double>
            {
                ["студии"] = parameters.StudioMaxApartmentArea,
                ["1-комнатной квартиры"] = parameters.OneRoomMaxApartmentArea,
                ["2-комнатной квартиры"] = parameters.TwoRoomMaxApartmentArea,
                ["3-комнатной квартиры"] = parameters.ThreeRoomMaxApartmentArea,
                ["4-комнатной квартиры"] = parameters.FourRoomMaxApartmentArea
            };

            foreach (var kv in limits)
            {
                if (kv.Value < 0)
                {
                    result.AddError($"Максимальная площадь {kv.Key} не может быть отрицательной.", "APARTMENT_TYPE_MAX_AREA_INVALID");
                    continue;
                }

                if (parameters.MinApartmentArea > 0 && kv.Value > 0 && kv.Value < parameters.MinApartmentArea)
                {
                    result.AddError(
                        $"Максимальная площадь {kv.Key} {kv.Value:F1} м² меньше общей минимальной площади квартиры {parameters.MinApartmentArea:F1} м².",
                        "APARTMENT_TYPE_MAX_AREA_INVALID");
                }
            }
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
                    $"Неизвестные типы помещений: {string.Join(", ", unknown)}. Используйте русские названия или значения контракта, например: Жилое помещение, МОП, LivingRoom, CommonArea.",
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

            var requestedMinimum = parameters.PlanningDetailMode == PlanningDetailMode.ApartmentRooms
                ? Math.Max(parameters.MinApartmentArea, 0)
                : parameters.TotalApartmentsRequested * Math.Max(parameters.MinApartmentArea, 0)
                  + Math.Max(parameters.MopAreaTarget, 0);
            if (requestedMinimum > contour.ApproximateArea * 1.05)
            {
                result.AddError(
                    $"Минимально требуемая площадь {requestedMinimum:F1} м² превышает площадь контура {contour.ApproximateArea:F1} м².",
                    "PROGRAM_AREA_EXCEEDS_CONTOUR");
            }

            if (parameters.MaxApartmentArea <= 0 && parameters.GetApartmentTypeMaxAreaOverrides().Count == 0)
                return;

            if (parameters.PlanningDetailMode == PlanningDetailMode.ApartmentRooms)
            {
                var apartmentType = parameters.GetPrimaryApartmentType();
                var maxApartmentArea = parameters.GetMaxApartmentAreaForType(apartmentType);
                if (maxApartmentArea > 0 && contour.ApproximateArea > maxApartmentArea * 1.05)
                {
                    result.AddError(
                        $"Площадь выбранного контура квартиры {contour.ApproximateArea:F1} м² больше максимальной площади для выбранного типа {maxApartmentArea:F1} м².",
                        "APARTMENT_CONTOUR_EXCEEDS_MAX_AREA");
                }

                return;
            }

            if (parameters.TotalApartmentsRequested <= 0 || parameters.MopAreaTarget <= 0)
                return;

            var maximumApartmentsArea = parameters.GetMaximumApartmentProgramArea();
            var maximumProgramArea = maximumApartmentsArea + parameters.MopAreaTarget;
            if (maximumProgramArea < contour.ApproximateArea * 0.95)
            {
                result.AddError(
                    $"Заданные ограничения физически не покрывают контур: максимум по квартирографии {maximumApartmentsArea:F1} м² + МОП {parameters.MopAreaTarget:F1} м² = {maximumProgramArea:F1} м², а площадь контура {contour.ApproximateArea:F1} м².",
                    "PROGRAM_MAX_AREA_UNDERFILLS_CONTOUR");
            }
        }

        private static string NormalizeRoomType(string token)
        {
            return token.ToLowerInvariant() switch
            {
                "mop" or "моп" or "common_area" or "common area"
                    or "место общего пользования" or "места общего пользования" or "общая зона" or "общий коридор"
                    => nameof(RoomType.CommonArea),
                "living_room" or "living room" or "жилое помещение" or "квартира" or "квартиры"
                    => nameof(RoomType.LivingRoom),
                "bedroom" or "спальня" or "спальная комната" => nameof(RoomType.Bedroom),
                "kitchen" or "кухня" or "кухонная зона" => nameof(RoomType.Kitchen),
                "bathroom" or "санузел" or "ванная" or "ванная комната" or "туалет" or "wc"
                    => nameof(RoomType.Bathroom),
                "corridor" or "коридор" or "общий проход" => nameof(RoomType.Corridor),
                "storage" or "кладовая" or "гардеробная" => nameof(RoomType.Storage),
                "lobby" or "холл" or "лифтовый холл" => nameof(RoomType.Lobby),
                "elevator" or "лифт" or "лифтовая шахта" => nameof(RoomType.Elevator),
                "staircase" or "лестница" or "лестничная клетка" => nameof(RoomType.Staircase),
                "balcony" or "балкон" or "лоджия" => nameof(RoomType.Balcony),
                "technical" or "техническое помещение" or "техпомещение" => nameof(RoomType.Technical),
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
