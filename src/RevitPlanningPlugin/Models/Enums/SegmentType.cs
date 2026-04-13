namespace RevitPlanningPlugin.Models.Enums
{
    /// <summary>
    /// Тип сегмента контура.
    /// Поддерживает ортогональные, неортогональные и органичные формы.
    /// </summary>
    public enum SegmentType
    {
        /// <summary>Прямолинейный сегмент.</summary>
        Line,

        /// <summary>Дуга окружности (задаётся центром, радиусом, направлением).</summary>
        Arc,

        /// <summary>Эрмитов сплайн (через контрольные точки).</summary>
        Spline,

        /// <summary>Эллиптическая дуга (органичные, скруглённые формы).</summary>
        Ellipse,

        /// <summary>NURBS-кривая (произвольные органичные формы фасадов).</summary>
        NurbsSpline
    }
}
