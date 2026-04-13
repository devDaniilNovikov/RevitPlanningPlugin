namespace RevitPlanningPlugin.Models.Enums
{
    /// <summary>
    /// Типы помещений для генерации планировки.
    /// </summary>
    public enum RoomType
    {
        LivingRoom,
        Bedroom,
        Kitchen,
        Bathroom,
        Corridor,
        Storage,
        Office,
        MeetingRoom,
        OpenSpace,
        Lobby,
        Technical,
        Staircase,
        Elevator,
        Balcony,
        /// <summary>МОП — место общего пользования (лифтовый холл, общий коридор, прочие помещения общего назначения).</summary>
        CommonArea,
        Other
    }
}
