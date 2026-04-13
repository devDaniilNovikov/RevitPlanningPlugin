using System;
using System.Collections.Generic;
using System.Linq;
using RevitPlanningPlugin.Models.Api;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Services.Api
{
    /// <summary>
    /// Маппер DTO ↔ Domain.
    /// Изолирует доменную модель от изменений API-контракта.
    /// </summary>
    public static class DtoMapper
    {
        // ————— Contour —————

        public static BuildingContour ToDomain(ApiContourDto dto)
        {
            return new BuildingContour
            {
                Id = dto.Id,
                Name = dto.Name,
                Description = dto.Description,
                SourceUnit = dto.Unit ?? "m",
                OuterLoop = dto.OuterLoop?.Select(MapSegment).ToList() ?? new List<ContourSegment>(),
                InnerLoops = dto.InnerLoops?.Select(
                    loop => loop.Select(MapSegment).ToList()).ToList() ?? new List<List<ContourSegment>>(),
                Metadata = dto.Metadata ?? new Dictionary<string, string>()
            };
        }

        // ————— Layout variant —————

        public static LayoutVariant ToDomain(ApiLayoutVariantDto dto)
        {
            return new LayoutVariant
            {
                Id = dto.Id,
                Name = dto.Name,
                VariantIndex = dto.VariantIndex,
                TotalArea = dto.TotalArea,
                UsableArea = dto.UsableArea,
                RoomCount = dto.RoomCount,
                CorridorArea = dto.CorridorArea,
                EfficiencyScore = dto.EfficiencyScore,
                Rooms = dto.Rooms?.Select(MapRoom).ToList() ?? new List<RoomLayout>(),
                Partitions = dto.Partitions?.Select(MapSegment).ToList() ?? new List<ContourSegment>(),
                CustomMetrics = dto.CustomMetrics ?? new Dictionary<string, double>(),
                Metadata = dto.Metadata ?? new Dictionary<string, string>()
            };
        }

        // ————— Generation request —————

        public static ApiGenerationRequestDto ToDto(string contourId, GenerationParameters parameters)
        {
            return new ApiGenerationRequestDto
            {
                ContourId = contourId,
                VariantCount = parameters.VariantCount,
                RoomTypes = parameters.RequiredRoomTypes?.Select(r => r.ToString()).ToList(),
                MinRoomArea = parameters.MinRoomArea,
                MaxRoomArea = parameters.MaxRoomArea,
                MinCorridorWidth = parameters.MinCorridorWidth,
                OptimizationPriority = parameters.OptimizationPriority,
                CustomParameters = parameters.CustomParameters
            };
        }

        // ————— Helpers —————

        private static ContourSegment MapSegment(ApiSegmentDto dto)
        {
            var type = (dto.Type?.ToLowerInvariant()) switch
            {
                "arc" => SegmentType.Arc,
                "spline" => SegmentType.Spline,
                _ => SegmentType.Line
            };

            return new ContourSegment
            {
                Type = type,
                Start = new Point2D(dto.Start.X, dto.Start.Y),
                End = new Point2D(dto.End.X, dto.End.Y),
                ArcCenter = dto.Center != null ? new Point2D(dto.Center.X, dto.Center.Y) : null,
                ArcRadius = dto.Radius,
                ArcClockwise = dto.Clockwise ?? false,
                SplineControlPoints = dto.ControlPoints?
                    .Select(p => new Point2D(p.X, p.Y)).ToList()
            };
        }

        private static RoomLayout MapRoom(ApiRoomDto dto)
        {
            return new RoomLayout
            {
                Id = dto.Id,
                Name = dto.Name,
                Type = ParseRoomType(dto.Type),
                Area = dto.Area,
                Boundary = dto.Boundary?.Select(MapSegment).ToList() ?? new List<ContourSegment>(),
                LabelPoint = dto.LabelPoint != null
                    ? new Point2D(dto.LabelPoint.X, dto.LabelPoint.Y)
                    : null,
                Properties = dto.Properties ?? new Dictionary<string, string>()
            };
        }

        private static RoomType ParseRoomType(string? type)
        {
            if (string.IsNullOrWhiteSpace(type)) return RoomType.Other;
            return Enum.TryParse<RoomType>(type, true, out var result) ? result : RoomType.Other;
        }
    }
}
