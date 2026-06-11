using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Services.Diagnostics
{
    /// <summary>
    /// Stable, non-secret fingerprints for correlating generation inputs in logs.
    /// </summary>
    public static class GenerationRequestDiagnostics
    {
        private const int DefaultHashLength = 16;

        public static string BuildParameterFingerprint(GenerationRequestContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            var payload = BuildParameterPayload(context);
            return ShortHash(payload);
        }

        public static string BuildPromptFingerprint(string? prompt)
            => ShortHash(prompt ?? string.Empty);

        public static string CreateGenerationNonce()
            => Guid.NewGuid().ToString("N").Substring(0, 12);

        public static string SecretFingerprint(string? secret)
            => string.IsNullOrWhiteSpace(secret) ? "empty" : ShortHash(secret.Trim(), 12);

        public static string SafeDisplayUrl(string? url)
        {
            var normalized = string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(normalized))
                return string.Empty;

            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                var builder = new UriBuilder(uri)
                {
                    UserName = string.Empty,
                    Password = string.Empty,
                    Query = string.Empty,
                    Fragment = string.Empty
                };

                return builder.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            }

            var queryStart = normalized.IndexOfAny(new[] { '?', '#' });
            var withoutQuery = queryStart >= 0 ? normalized.Substring(0, queryStart) : normalized;
            var schemeIndex = withoutQuery.IndexOf("://", StringComparison.Ordinal);
            if (schemeIndex >= 0)
            {
                var userInfoEnd = withoutQuery.IndexOf('@', schemeIndex + 3);
                if (userInfoEnd >= 0)
                    withoutQuery = withoutQuery.Substring(0, schemeIndex + 3) + withoutQuery.Substring(userInfoEnd + 1);
            }

            return withoutQuery.TrimEnd('/');
        }

        public static string ShortHash(string value, int length = DefaultHashLength)
        {
            if (length <= 0) throw new ArgumentOutOfRangeException(nameof(length));

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
            var hex = BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            return hex.Substring(0, Math.Min(length, hex.Length));
        }

        private static string BuildParameterPayload(GenerationRequestContext context)
        {
            var p = context.Parameters ?? new GenerationParameters();
            var c = context.Contour ?? new BuildingContour();
            var project = context.ProjectContext ?? new RevitProjectContext();
            var sb = new StringBuilder();

            Append(sb, "generation_type", p.GenerationType);
            Append(sb, "planning_detail_mode", p.PlanningDetailMode);
            Append(sb, "validation_mode", p.ValidationMode);
            Append(sb, "variant_count", p.VariantCount);
            Append(sb, "text_prompt", p.TextPrompt);
            Append(sb, "studio_count", p.StudioCount);
            Append(sb, "one_room_count", p.OneRoomCount);
            Append(sb, "two_room_count", p.TwoRoomCount);
            Append(sb, "three_room_count", p.ThreeRoomCount);
            Append(sb, "four_room_count", p.FourRoomCount);
            Append(sb, "min_apartment_area", p.MinApartmentArea);
            Append(sb, "max_apartment_area", p.MaxApartmentArea);
            Append(sb, "studio_max_apartment_area", p.StudioMaxApartmentArea);
            Append(sb, "one_room_max_apartment_area", p.OneRoomMaxApartmentArea);
            Append(sb, "two_room_max_apartment_area", p.TwoRoomMaxApartmentArea);
            Append(sb, "three_room_max_apartment_area", p.ThreeRoomMaxApartmentArea);
            Append(sb, "four_room_max_apartment_area", p.FourRoomMaxApartmentArea);
            Append(sb, "mop_area_target", p.MopAreaTarget);
            Append(sb, "min_corridor_width", p.MinCorridorWidth);
            Append(sb, "min_room_area", p.MinRoomArea);
            Append(sb, "max_room_area", p.MaxRoomArea);
            Append(sb, "optimization_priority", p.OptimizationPriority);

            Append(sb, "effective_apartment_types", FormatDictionary(p.GetEffectiveApartmentTypeRequirements()));
            Append(sb, "max_area_by_type", FormatDictionary(p.GetApartmentTypeMaxAreaOverrides()));
            Append(sb, "required_room_types", string.Join(",", p.GetEffectiveRequiredRoomTypes().OrderBy(t => t.ToString())));
            Append(sb, "custom_parameters", FormatCustomParameters(p.CustomParameters));

            Append(sb, "contour_id", c.Id);
            Append(sb, "contour_name", c.Name);
            Append(sb, "contour_area", c.ApproximateArea);
            Append(sb, "contour_geometry", c.GeometryDescription);
            Append(sb, "outer_segments", c.OuterLoop?.Count ?? 0);
            Append(sb, "inner_loops", c.InnerLoops?.Count ?? 0);
            Append(sb, "outer_vertices", FormatVertices(c.GetOuterVertices()));

            Append(sb, "level_id", project.LevelId);
            Append(sb, "level_name", project.LevelName);
            Append(sb, "level_elevation", project.LevelElevationMeters);
            Append(sb, "active_view", project.ActiveViewName);
            Append(sb, "active_view_type", project.ActiveViewType);
            Append(sb, "contour_source", project.ContourSource);
            Append(sb, "existing_elements", project.ExistingElements?.Count ?? 0);

            return sb.ToString();
        }

        private static string FormatVertices(IEnumerable<Point2D> vertices)
            => string.Join(";", vertices.Select(p =>
                $"{p.X.ToString("F3", CultureInfo.InvariantCulture)},{p.Y.ToString("F3", CultureInfo.InvariantCulture)}"));

        private static string FormatDictionary<TValue>(IDictionary<string, TValue>? values)
        {
            if (values == null || values.Count == 0)
                return string.Empty;

            return string.Join(",",
                values
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key}:{FormatValue(kv.Value)}"));
        }

        private static string FormatCustomParameters(IDictionary<string, string>? values)
        {
            if (values == null || values.Count == 0)
                return string.Empty;

            return string.Join(",",
                values
                    .Where(kv => !IsSecretKey(kv.Key))
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => $"{kv.Key}:{kv.Value}"));
        }

        public static bool IsSecretKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            var normalized = key.ToLowerInvariant();
            return normalized.Contains("api_key")
                   || normalized.Contains("apikey")
                   || normalized.Contains("access_key")
                   || normalized.Contains("accesskey")
                   || normalized.Contains("authorization")
                   || normalized == "auth"
                   || normalized.Contains("bearer")
                   || normalized.Contains("credential")
                   || normalized.Contains("private_key")
                   || normalized.Contains("privatekey")
                   || normalized.Contains("token")
                   || normalized.Contains("secret")
                   || normalized.Contains("password");
        }

        private static void Append(StringBuilder sb, string key, object? value)
            => sb.Append(key).Append('=').Append(FormatValue(value)).Append('\n');

        private static string FormatValue(object? value)
        {
            if (value == null)
                return string.Empty;

            return value switch
            {
                double d => d.ToString("G17", CultureInfo.InvariantCulture),
                float f => f.ToString("G9", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty
            };
        }
    }
}
