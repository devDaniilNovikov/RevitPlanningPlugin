namespace RevitPlanningPlugin.Models.Enums
{
    /// <summary>
    /// Уровень детализации генерируемой планировки.
    /// </summary>
    public enum PlanningDetailMode
    {
        /// <summary>Этаж с квартирами как крупными блоками и МОПами.</summary>
        FloorLayout,

        /// <summary>Внутренняя покомнатная планировка одной квартиры.</summary>
        ApartmentRooms
    }
}
