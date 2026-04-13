using System.Collections.Generic;
using Newtonsoft.Json;

namespace RevitPlanningPlugin.Models.Api
{
    // ——————————————————————————————————————————————
    //  DTO для получения контуров из внешнего API
    // ——————————————————————————————————————————————

    public class ApiPointDto
    {
        [JsonProperty("x")] public double X { get; set; }
        [JsonProperty("y")] public double Y { get; set; }
    }

    public class ApiSegmentDto
    {
        [JsonProperty("type")] public string Type { get; set; } = "line";
        [JsonProperty("start")] public ApiPointDto Start { get; set; } = new();
        [JsonProperty("end")] public ApiPointDto End { get; set; } = new();
        [JsonProperty("center")] public ApiPointDto? Center { get; set; }
        [JsonProperty("radius")] public double? Radius { get; set; }
        [JsonProperty("clockwise")] public bool? Clockwise { get; set; }
        [JsonProperty("control_points")] public List<ApiPointDto>? ControlPoints { get; set; }
    }

    public class ApiContourDto
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("description")] public string? Description { get; set; }
        [JsonProperty("unit")] public string Unit { get; set; } = "m";
        [JsonProperty("outer_loop")] public List<ApiSegmentDto> OuterLoop { get; set; } = new();
        [JsonProperty("inner_loops")] public List<List<ApiSegmentDto>>? InnerLoops { get; set; }
        [JsonProperty("metadata")] public Dictionary<string, string>? Metadata { get; set; }
    }

    public class ApiContourListDto
    {
        [JsonProperty("contours")] public List<ApiContourSummaryDto> Contours { get; set; } = new();
        [JsonProperty("total")] public int Total { get; set; }
    }

    public class ApiContourSummaryDto
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("area")] public double? Area { get; set; }
        [JsonProperty("description")] public string? Description { get; set; }
    }

    // ——————————————————————————————————————————————
    //  DTO для генерации планировок
    // ——————————————————————————————————————————————

    public class ApiGenerationRequestDto
    {
        [JsonProperty("contour_id")] public string ContourId { get; set; } = string.Empty;
        [JsonProperty("variant_count")] public int VariantCount { get; set; } = 3;

        // ——— Параметры квартир ———

        /// <summary>Состав квартир: тип → требуемое количество.</summary>
        [JsonProperty("apartment_types")] public Dictionary<string, int>? ApartmentTypes { get; set; }

        /// <summary>Минимальная площадь квартиры, м².</summary>
        [JsonProperty("min_apartment_area")] public double? MinApartmentArea { get; set; }

        /// <summary>Максимальная площадь квартиры, м².</summary>
        [JsonProperty("max_apartment_area")] public double? MaxApartmentArea { get; set; }

        // ——— Параметры МОП ———

        /// <summary>Целевая площадь МОПов, м². 0 = автоопределение.</summary>
        [JsonProperty("mop_area_target")] public double? MopAreaTarget { get; set; }

        /// <summary>Минимальная ширина коридора МОП, м.</summary>
        [JsonProperty("min_corridor_width")] public double? MinCorridorWidth { get; set; }

        // ——— Общие параметры ———

        [JsonProperty("room_types")] public List<string>? RoomTypes { get; set; }
        [JsonProperty("min_room_area")] public double? MinRoomArea { get; set; }
        [JsonProperty("max_room_area")] public double? MaxRoomArea { get; set; }
        [JsonProperty("optimization_priority")] public string? OptimizationPriority { get; set; }
        [JsonProperty("custom_parameters")] public Dictionary<string, string>? CustomParameters { get; set; }
    }

    public class ApiRoomDto
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("type")] public string Type { get; set; } = string.Empty;
        [JsonProperty("area")] public double Area { get; set; }
        [JsonProperty("boundary")] public List<ApiSegmentDto> Boundary { get; set; } = new();
        [JsonProperty("label_point")] public ApiPointDto? LabelPoint { get; set; }
        [JsonProperty("properties")] public Dictionary<string, string>? Properties { get; set; }
    }

    public class ApiLayoutVariantDto
    {
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("name")] public string Name { get; set; } = string.Empty;
        [JsonProperty("variant_index")] public int VariantIndex { get; set; }
        [JsonProperty("rooms")] public List<ApiRoomDto> Rooms { get; set; } = new();
        [JsonProperty("partitions")] public List<ApiSegmentDto>? Partitions { get; set; }

        // ——— Метрики площадей ———
        [JsonProperty("total_area")] public double TotalArea { get; set; }
        [JsonProperty("usable_area")] public double UsableArea { get; set; }

        /// <summary>Суммарная площадь МОПов (лифтовые холлы, общие коридоры), м².</summary>
        [JsonProperty("mop_area")] public double MopArea { get; set; }

        [JsonProperty("corridor_area")] public double CorridorArea { get; set; }

        // ——— Метрики квартир ———
        [JsonProperty("room_count")] public int RoomCount { get; set; }

        /// <summary>Количество квартирных единиц в варианте.</summary>
        [JsonProperty("apartment_count")] public int ApartmentCount { get; set; }

        /// <summary>Распределение квартир по типам: тип → количество.</summary>
        [JsonProperty("apartment_type_distribution")]
        public Dictionary<string, int>? ApartmentTypeDistribution { get; set; }

        // ——— Интегральная оценка ———
        [JsonProperty("efficiency_score")] public double EfficiencyScore { get; set; }

        [JsonProperty("custom_metrics")] public Dictionary<string, double>? CustomMetrics { get; set; }
        [JsonProperty("metadata")] public Dictionary<string, string>? Metadata { get; set; }
    }

    public class ApiGenerationResultDto
    {
        [JsonProperty("request_id")] public string RequestId { get; set; } = string.Empty;
        [JsonProperty("status")] public string Status { get; set; } = string.Empty;
        [JsonProperty("variants")] public List<ApiLayoutVariantDto> Variants { get; set; } = new();
        [JsonProperty("error")] public string? Error { get; set; }
    }

    // ——————————————————————————————————————————————
    //  Общий конверт ответа
    // ——————————————————————————————————————————————

    public class ApiResponse<T>
    {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("data")] public T? Data { get; set; }
        [JsonProperty("error")] public ApiErrorDto? Error { get; set; }
    }

    public class ApiErrorDto
    {
        [JsonProperty("code")] public string Code { get; set; } = string.Empty;
        [JsonProperty("message")] public string Message { get; set; } = string.Empty;
    }
}
