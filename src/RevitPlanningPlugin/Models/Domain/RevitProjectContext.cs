using System.Collections.Generic;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Проектный контекст, извлеченный из Revit-модели.
    /// </summary>
    public class RevitProjectContext
    {
        public string DocumentTitle { get; set; } = string.Empty;
        public string ActiveViewName { get; set; } = string.Empty;
        public string ActiveViewType { get; set; } = string.Empty;
        public string LevelId { get; set; } = string.Empty;
        public string LevelName { get; set; } = string.Empty;
        public double LevelElevationMeters { get; set; }
        public string ContourSource { get; set; } = "api";
        public Dictionary<string, string> ProjectParameters { get; set; } = new();
        public List<RevitModelElementContext> ExistingElements { get; set; } = new();
    }
}
