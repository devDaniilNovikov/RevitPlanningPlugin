using System;
using System.Collections.Generic;
using System.Linq;
using RevitPlanningPlugin.Models.Api;

namespace RevitPlanningPlugin.Services.Api
{
    /// <summary>
    /// Проверяет обязательные поля ответа AI-сервиса до преобразования в доменную модель.
    /// </summary>
    public static class ApiResponseContractValidator
    {
        private static readonly HashSet<string> AllowedRoomTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "LivingRoom", "Bedroom", "Kitchen", "Bathroom", "Corridor", "Storage",
            "Office", "MeetingRoom", "OpenSpace", "Lobby", "Technical", "Staircase",
            "Elevator", "Balcony", "CommonArea", "Other",
            "living_room", "meeting_room", "open_space", "common_area", "mop"
        };

        private static readonly HashSet<string> AllowedApartmentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Studio", "OneRoom", "TwoRoom", "ThreeRoom", "FourRoom"
        };

        private static readonly HashSet<string> AllowedSegmentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "line", "arc", "spline", "ellipse", "nurbs", "nurbsspline", "nurbs_spline"
        };

        public static void ValidateGenerationResult(ApiGenerationResultDto result, int? expectedVariantCount = null)
        {
            var issues = new List<string>();

            if (result == null)
                throw new PlanningApiException("Пустой результат генерации.", errorCode: "EMPTY_RESULT");

            if (string.IsNullOrWhiteSpace(result.RequestId))
                issues.Add("data.request_id is required");

            if (string.IsNullOrWhiteSpace(result.Status))
                issues.Add("data.status is required");

            if (!string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase))
                issues.Add("data.status must be 'completed' for successful responses");

            if (result.Variants == null || result.Variants.Count == 0)
                issues.Add("data.variants must contain at least one variant");
            else
            {
                if (expectedVariantCount.HasValue && result.Variants.Count != expectedVariantCount.Value)
                {
                    issues.Add(
                        $"data.variants must contain exactly {expectedVariantCount.Value} variant(s), actual {result.Variants.Count}");
                }

                ValidateVariants(result.Variants, issues);
            }

            if (issues.Count > 0)
            {
                throw new PlanningApiException(
                    "Ответ AI-сервиса не соответствует контракту: " + string.Join("; ", issues),
                    errorCode: "INVALID_API_CONTRACT");
            }
        }

        private static void ValidateVariants(List<ApiLayoutVariantDto> variants, List<string> issues)
        {
            for (int i = 0; i < variants.Count; i++)
            {
                var variant = variants[i];
                var prefix = $"data.variants[{i}]";

                if (string.IsNullOrWhiteSpace(variant.Id))
                    issues.Add($"{prefix}.id is required");
                if (string.IsNullOrWhiteSpace(variant.Name))
                    issues.Add($"{prefix}.name is required");
                if (variant.TotalArea <= 0)
                    issues.Add($"{prefix}.total_area must be positive");
                if (variant.UsableArea < 0)
                    issues.Add($"{prefix}.usable_area must be non-negative");
                if (variant.MopArea < 0)
                    issues.Add($"{prefix}.mop_area must be non-negative");
                if (variant.Rooms == null || variant.Rooms.Count == 0)
                    issues.Add($"{prefix}.rooms must contain at least one room");
                else
                    ValidateRooms(variant.Rooms, prefix, issues);

                if (variant.ApartmentTypeDistribution != null)
                    ValidateApartmentTypeDistribution(variant.ApartmentTypeDistribution, $"{prefix}.apartment_type_distribution", issues);

                if (variant.Partitions != null)
                    ValidateSegments(variant.Partitions, $"{prefix}.partitions", issues);
            }
        }

        private static void ValidateRooms(List<ApiRoomDto> rooms, string prefix, List<string> issues)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                var roomPrefix = $"{prefix}.rooms[{i}]";

                if (string.IsNullOrWhiteSpace(room.Id))
                    issues.Add($"{roomPrefix}.id is required");
                if (string.IsNullOrWhiteSpace(room.Name))
                    issues.Add($"{roomPrefix}.name is required");
                if (string.IsNullOrWhiteSpace(room.Type))
                    issues.Add($"{roomPrefix}.type is required");
                else if (!AllowedRoomTypes.Contains(room.Type))
                    issues.Add($"{roomPrefix}.type contains unknown room type '{room.Type}'");
                if (room.Area <= 0)
                    issues.Add($"{roomPrefix}.area must be positive");
                if (room.LabelPoint == null)
                    issues.Add($"{roomPrefix}.label_point is required");
                else
                    ValidatePoint(room.LabelPoint, $"{roomPrefix}.label_point", issues);

                if (room.Boundary == null || room.Boundary.Count < 3)
                    issues.Add($"{roomPrefix}.boundary must contain at least 3 segments");
                else
                    ValidateSegments(room.Boundary, $"{roomPrefix}.boundary", issues);

                if (room.Properties != null
                    && room.Properties.TryGetValue("apartment_type", out var apartmentType)
                    && !string.IsNullOrWhiteSpace(apartmentType)
                    && !AllowedApartmentTypes.Contains(apartmentType))
                {
                    issues.Add($"{roomPrefix}.properties.apartment_type contains unknown apartment type '{apartmentType}'");
                }

                if (room.Properties != null
                    && room.Properties.TryGetValue("apartment_id", out var apartmentId)
                    && !string.IsNullOrWhiteSpace(apartmentId)
                    && (!room.Properties.TryGetValue("apartment_type", out var groupedApartmentType)
                        || string.IsNullOrWhiteSpace(groupedApartmentType)))
                {
                    issues.Add($"{roomPrefix}.properties.apartment_type is required when apartment_id is provided");
                }
            }
        }

        private static void ValidateApartmentTypeDistribution(
            Dictionary<string, int> distribution,
            string prefix,
            List<string> issues)
        {
            foreach (var kv in distribution)
            {
                if (!AllowedApartmentTypes.Contains(kv.Key))
                    issues.Add($"{prefix} contains unknown apartment type '{kv.Key}'");
                if (kv.Value < 0)
                    issues.Add($"{prefix}.{kv.Key} must be non-negative");
            }
        }

        private static void ValidateSegments(List<ApiSegmentDto> segments, string prefix, List<string> issues)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                var segmentPrefix = $"{prefix}[{i}]";

                if (segment.Start == null)
                    issues.Add($"{segmentPrefix}.start is required");
                else
                    ValidatePoint(segment.Start, $"{segmentPrefix}.start", issues);

                if (segment.End == null)
                    issues.Add($"{segmentPrefix}.end is required");
                else
                    ValidatePoint(segment.End, $"{segmentPrefix}.end", issues);

                if (string.IsNullOrWhiteSpace(segment.Type))
                    issues.Add($"{segmentPrefix}.type is required");
                else if (!AllowedSegmentTypes.Contains(segment.Type))
                    issues.Add($"{segmentPrefix}.type contains unknown segment type '{segment.Type}'");

                if (string.Equals(segment.Type, "arc", StringComparison.OrdinalIgnoreCase)
                    && segment.Center == null)
                {
                    issues.Add($"{segmentPrefix}.center is required for arc segments");
                }
                else if (string.Equals(segment.Type, "arc", StringComparison.OrdinalIgnoreCase)
                         && segment.Center != null)
                {
                    ValidatePoint(segment.Center, $"{segmentPrefix}.center", issues);
                }

                if (string.Equals(segment.Type, "arc", StringComparison.OrdinalIgnoreCase)
                    && (!segment.Radius.HasValue || segment.Radius.Value <= 0))
                {
                    issues.Add($"{segmentPrefix}.radius must be positive for arc segments");
                }

                if (segment.ControlPoints != null)
                {
                    foreach (var point in segment.ControlPoints.Select((value, index) => new { value, index }))
                        ValidatePoint(point.value, $"{segmentPrefix}.control_points[{point.index}]", issues);
                }
            }
        }

        private static void ValidatePoint(ApiPointDto point, string prefix, List<string> issues)
        {
            if (double.IsNaN(point.X) || double.IsInfinity(point.X)
                || double.IsNaN(point.Y) || double.IsInfinity(point.Y))
            {
                issues.Add($"{prefix} contains invalid coordinates");
            }
        }
    }
}
