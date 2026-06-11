using System.Collections.Generic;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Краткое описание существующего элемента Revit, передаваемое в AI-контекст.
    /// </summary>
    public class RevitModelElementContext
    {
        public string ElementId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ElementType { get; set; } = string.Empty;
        public string? LevelName { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new();
    }
}
