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
                MopArea = dto.MopArea,
                CorridorArea = dto.CorridorArea,
                RoomCount = dto.RoomCount,
                ApartmentCount = dto.ApartmentCount,
                ApartmentTypeDistribution = dto.ApartmentTypeDistribution
                    ?? new Dictionary<string, int>(),
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
            var dto = new ApiGenerationRequestDto
            {
                ContourId = contourId,
                VariantCount = parameters.VariantCount,
                GenerationType = parameters.GenerationType.ToString(),
                ValidationMode = parameters.ValidationMode.ToString(),
                TextPrompt = string.IsNullOrWhiteSpace(parameters.TextPrompt) ? null : parameters.TextPrompt,

                // Квартиры
                ApartmentTypes = parameters.GetApartmentTypeRequirements(),
                MinApartmentArea = parameters.MinApartmentArea > 0
                    ? parameters.MinApartmentArea : (double?)null,
                MaxApartmentArea = parameters.MaxApartmentArea > 0
                    ? parameters.MaxApartmentArea : (double?)null,

                // МОП
                MopAreaTarget = parameters.MopAreaTarget > 0
                    ? parameters.MopAreaTarget : (double?)null,
                MinCorridorWidth = parameters.MinCorridorWidth > 0
                    ? parameters.MinCorridorWidth : (double?)null,

                // Общие
                RoomTypes = parameters.RequiredRoomTypes?.Select(r => r.ToString()).ToList(),
                MinRoomArea = parameters.MinRoomArea > 0
                    ? parameters.MinRoomArea : (double?)null,
                MaxRoomArea = parameters.MaxRoomArea > 0
                    ? parameters.MaxRoomArea : (double?)null,
                OptimizationPriority = parameters.OptimizationPriority,
                CustomParameters = parameters.CustomParameters
            };

            // Убираем пустой словарь apartment_types, чтобы не слать {}
            if (dto.ApartmentTypes?.Count == 0)
                dto.ApartmentTypes = null;

            return dto;
        }

        public static ApiGenerationRequestDto ToDto(GenerationRequestContext context)
        {
            var dto = ToDto(context.Contour.Id, context.Parameters);
            dto.RequestId = string.IsNullOrWhiteSpace(context.RequestId) ? null : context.RequestId;
            dto.LlmPrompt = string.IsNullOrWhiteSpace(context.Prompt) ? null : context.Prompt;
            dto.Context = new ApiGenerationContextDto
            {
                Contour = ToApiDto(context.Contour),
                RevitContext = ToApiDto(context.ProjectContext)
            };
            return dto;
        }

        // ————— Helpers —————

        private static ContourSegment MapSegment(ApiSegmentDto dto)
        {
            var type = (dto.Type?.ToLowerInvariant()) switch
            {
                "arc" => SegmentType.Arc,
                "spline" => SegmentType.Spline,
                "ellipse" => SegmentType.Ellipse,
                "nurbs" or "nurbsspline" or "nurbs_spline" => SegmentType.NurbsSpline,
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

        private static ApiContourDto ToApiDto(BuildingContour contour)
        {
            return new ApiContourDto
            {
                Id = contour.Id,
                Name = contour.Name,
                Description = contour.Description,
                Unit = "m",
                OuterLoop = contour.OuterLoop.Select(ToApiDto).ToList(),
                InnerLoops = contour.InnerLoops.Select(loop => loop.Select(ToApiDto).ToList()).ToList(),
                Metadata = contour.Metadata
            };
        }

        private static ApiSegmentDto ToApiDto(ContourSegment segment)
        {
            return new ApiSegmentDto
            {
                Type = ToApiSegmentType(segment.Type),
                Start = ToApiDto(segment.Start),
                End = ToApiDto(segment.End),
                Center = segment.ArcCenter != null ? ToApiDto(segment.ArcCenter) : null,
                Radius = segment.ArcRadius,
                Clockwise = segment.ArcClockwise,
                ControlPoints = segment.SplineControlPoints?.Select(ToApiDto).ToList()
            };
        }

        private static ApiPointDto ToApiDto(Point2D point)
        {
            return new ApiPointDto { X = point.X, Y = point.Y };
        }

        private static ApiRevitProjectContextDto ToApiDto(RevitProjectContext context)
        {
            return new ApiRevitProjectContextDto
            {
                DocumentTitle = context.DocumentTitle,
                ActiveViewName = context.ActiveViewName,
                ActiveViewType = context.ActiveViewType,
                LevelId = context.LevelId,
                LevelName = context.LevelName,
                LevelElevationMeters = context.LevelElevationMeters,
                ContourSource = context.ContourSource,
                ProjectParameters = context.ProjectParameters,
                ExistingElements = context.ExistingElements.Select(ToApiDto).ToList()
            };
        }

        private static ApiRevitElementContextDto ToApiDto(RevitModelElementContext element)
        {
            return new ApiRevitElementContextDto
            {
                ElementId = element.ElementId,
                Category = element.Category,
                Name = element.Name,
                ElementType = element.ElementType,
                LevelName = element.LevelName,
                Parameters = element.Parameters
            };
        }

        private static string ToApiSegmentType(SegmentType type)
        {
            return type switch
            {
                SegmentType.Arc => "arc",
                SegmentType.Spline => "spline",
                SegmentType.Ellipse => "ellipse",
                SegmentType.NurbsSpline => "nurbs_spline",
                _ => "line"
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

            // Явное сопоставление строк API → enum (регистронезависимо)
            return type.ToLowerInvariant() switch
            {
                "commonarea" or "common_area" or "mop" => RoomType.CommonArea,
                "lobby"                                 => RoomType.Lobby,
                "elevator"                              => RoomType.Elevator,
                "corridor"                              => RoomType.Corridor,
                "staircase"                             => RoomType.Staircase,
                "livingroom" or "living_room"           => RoomType.LivingRoom,
                "bedroom"                               => RoomType.Bedroom,
                "kitchen"                               => RoomType.Kitchen,
                "bathroom"                              => RoomType.Bathroom,
                "storage"                               => RoomType.Storage,
                "office"                                => RoomType.Office,
                "balcony"                               => RoomType.Balcony,
                _ => Enum.TryParse<RoomType>(type, true, out var result)
                        ? result
                        : RoomType.Other
            };
        }
    }
}
