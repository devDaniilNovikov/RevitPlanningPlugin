namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Единый объект запроса к AI-сервису: Revit-контекст, контур, параметры и prompt.
    /// </summary>
    public class GenerationRequestContext
    {
        public string RequestId { get; set; } = string.Empty;
        public BuildingContour Contour { get; set; } = new();
        public RevitProjectContext ProjectContext { get; set; } = new();
        public GenerationParameters Parameters { get; set; } = new();
        public string Prompt { get; set; } = string.Empty;
    }
}
