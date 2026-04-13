namespace RevitPlanningPlugin.Models.Enums
{
    /// <summary>
    /// Статус операции генерации.
    /// </summary>
    public enum GenerationStatus
    {
        Idle,
        Loading,
        Validating,
        Generating,
        Completed,
        Error
    }
}
