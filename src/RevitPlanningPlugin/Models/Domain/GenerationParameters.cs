using System.Collections.Generic;
using System.Linq;
using RevitPlanningPlugin.Models.Enums;

namespace RevitPlanningPlugin.Models.Domain
{
    /// <summary>
    /// Параметры запуска AI-генерации планировок.
    /// Передаются во внешний AI-сервис вместе с геометрией контура.
    /// </summary>
    public class GenerationParameters
    {
        /// <summary>Количество вариантов (1–20).</summary>
        public int VariantCount { get; set; } = 3;

        /// <summary>Тип генерации: жилье, офис, смешанное использование или пользовательский сценарий.</summary>
        public GenerationType GenerationType { get; set; } = GenerationType.Residential;

        /// <summary>Уровень детализации: планировка этажа или покомнатная планировка квартиры.</summary>
        public PlanningDetailMode PlanningDetailMode { get; set; } = PlanningDetailMode.FloorLayout;

        /// <summary>Режим проверки результата генерации.</summary>
        public ValidationMode ValidationMode { get; set; } = ValidationMode.Advisory;

        /// <summary>Текстовое пожелание пользователя к AI-генерации.</summary>
        public string TextPrompt { get; set; } =
            "Сгенерируй квартирографию типового жилого этажа с учетом выбранного режима детализации: " +
            "для режима планировки этажа покажи квартиры крупными блоками без внутренних комнат, " +
            "для режима планировки квартиры раздели одну выбранную квартиру на жилую комнату, спальни, кухню и санузел. " +
            "Места общего пользования сформируй компактным общим коридором с лифтово-лестничным узлом. " +
            "Соблюдай заданное количество квартир, минимальную и максимальную площадь квартир, " +
            "типовые максимумы площадей и минимальную ширину коридора.";

        // ——— Параметры квартир ———

        /// <summary>Требуемое количество студий.</summary>
        public int StudioCount { get; set; } = 0;

        /// <summary>Требуемое количество 1-комнатных квартир.</summary>
        public int OneRoomCount { get; set; } = 2;

        /// <summary>Требуемое количество 2-комнатных квартир.</summary>
        public int TwoRoomCount { get; set; } = 4;

        /// <summary>Требуемое количество 3-комнатных квартир.</summary>
        public int ThreeRoomCount { get; set; } = 2;

        /// <summary>Требуемое количество 4-комнатных квартир.</summary>
        public int FourRoomCount { get; set; } = 0;

        /// <summary>Минимальная площадь квартиры, м².</summary>
        public double MinApartmentArea { get; set; } = 25.0;

        /// <summary>Максимальная площадь квартиры, м².</summary>
        public double MaxApartmentArea { get; set; } = 120.0;

        /// <summary>Максимальная площадь студии, м². 0 = использовать общий максимум.</summary>
        public double StudioMaxApartmentArea { get; set; } = 0.0;

        /// <summary>Максимальная площадь 1-комнатной квартиры, м². 0 = использовать общий максимум.</summary>
        public double OneRoomMaxApartmentArea { get; set; } = 0.0;

        /// <summary>Максимальная площадь 2-комнатной квартиры, м². 0 = использовать общий максимум.</summary>
        public double TwoRoomMaxApartmentArea { get; set; } = 0.0;

        /// <summary>Максимальная площадь 3-комнатной квартиры, м². 0 = использовать общий максимум.</summary>
        public double ThreeRoomMaxApartmentArea { get; set; } = 0.0;

        /// <summary>Максимальная площадь 4-комнатной квартиры, м². 0 = использовать общий максимум.</summary>
        public double FourRoomMaxApartmentArea { get; set; } = 0.0;

        // ——— Параметры МОП ———

        /// <summary>
        /// Целевая площадь МОПов (мест общего пользования), м².
        /// 0 = определить автоматически.
        /// </summary>
        public double MopAreaTarget { get; set; } = 90.0;

        /// <summary>Минимальная ширина коридора МОП, м.</summary>
        public double MinCorridorWidth { get; set; } = 1.8;

        // ——— Общие параметры ———

        /// <summary>Требуемые типы помещений (расширенный список для AI).</summary>
        public List<RoomType> RequiredRoomTypes { get; set; } = new();

        /// <summary>Минимальная площадь помещения (для не-квартирных зон), м².</summary>
        public double MinRoomArea { get; set; } = 8.0;

        /// <summary>Максимальная площадь помещения, м².</summary>
        public double MaxRoomArea { get; set; } = 80.0;

        /// <summary>Приоритет оптимизации: «efficiency» | «area» | «rooms».</summary>
        public string OptimizationPriority { get; set; } = "efficiency";

        /// <summary>Дополнительные пользовательские параметры.</summary>
        public Dictionary<string, string> CustomParameters { get; set; } = new();

        // ——— Вспомогательные методы ———

        /// <summary>
        /// Возвращает состав квартир в виде словаря для передачи в API.
        /// </summary>
        public Dictionary<string, int> GetApartmentTypeRequirements()
        {
            var dict = new Dictionary<string, int>();
            if (StudioCount > 0)     dict["Studio"]    = StudioCount;
            if (OneRoomCount > 0)    dict["OneRoom"]   = OneRoomCount;
            if (TwoRoomCount > 0)    dict["TwoRoom"]   = TwoRoomCount;
            if (ThreeRoomCount > 0)  dict["ThreeRoom"] = ThreeRoomCount;
            if (FourRoomCount > 0)   dict["FourRoom"]  = FourRoomCount;
            return dict;
        }

        /// <summary>
        /// Тип одной квартиры для режима покомнатной планировки.
        /// Если задано несколько положительных счетчиков, берется первый по порядку.
        /// </summary>
        public string GetPrimaryApartmentType()
        {
            if (StudioCount > 0) return "Studio";
            if (OneRoomCount > 0) return "OneRoom";
            if (TwoRoomCount > 0) return "TwoRoom";
            if (ThreeRoomCount > 0) return "ThreeRoom";
            if (FourRoomCount > 0) return "FourRoom";
            return "OneRoom";
        }

        /// <summary>
        /// Состав квартир с учетом режима детализации.
        /// Для режима квартиры всегда возвращается одна квартира выбранного типа.
        /// </summary>
        public Dictionary<string, int> GetEffectiveApartmentTypeRequirements()
        {
            if (PlanningDetailMode == PlanningDetailMode.ApartmentRooms)
            {
                return new Dictionary<string, int>
                {
                    [GetPrimaryApartmentType()] = 1
                };
            }

            return GetApartmentTypeRequirements();
        }

        /// <summary>
        /// Возвращает явно заданные максимумы площади по типам квартир.
        /// Значение 0 в UI означает "использовать общий максимум" и не попадает в словарь.
        /// </summary>
        public Dictionary<string, double> GetApartmentTypeMaxAreaOverrides()
        {
            var dict = new Dictionary<string, double>();
            if (StudioMaxApartmentArea > 0) dict["Studio"] = StudioMaxApartmentArea;
            if (OneRoomMaxApartmentArea > 0) dict["OneRoom"] = OneRoomMaxApartmentArea;
            if (TwoRoomMaxApartmentArea > 0) dict["TwoRoom"] = TwoRoomMaxApartmentArea;
            if (ThreeRoomMaxApartmentArea > 0) dict["ThreeRoom"] = ThreeRoomMaxApartmentArea;
            if (FourRoomMaxApartmentArea > 0) dict["FourRoom"] = FourRoomMaxApartmentArea;
            return dict;
        }

        /// <summary>
        /// Эффективный максимум площади для типа квартиры: типовой максимум, если он задан,
        /// иначе общий MaxApartmentArea.
        /// </summary>
        public double GetMaxApartmentAreaForType(string? apartmentType)
        {
            return apartmentType switch
            {
                "Studio" when StudioMaxApartmentArea > 0 => StudioMaxApartmentArea,
                "OneRoom" when OneRoomMaxApartmentArea > 0 => OneRoomMaxApartmentArea,
                "TwoRoom" when TwoRoomMaxApartmentArea > 0 => TwoRoomMaxApartmentArea,
                "ThreeRoom" when ThreeRoomMaxApartmentArea > 0 => ThreeRoomMaxApartmentArea,
                "FourRoom" when FourRoomMaxApartmentArea > 0 => FourRoomMaxApartmentArea,
                _ => MaxApartmentArea
            };
        }

        /// <summary>
        /// Максимальная суммарная площадь квартир по квартирографии с учетом типовых лимитов.
        /// </summary>
        public double GetMaximumApartmentProgramArea()
        {
            return GetEffectiveApartmentTypeRequirements()
                .Sum(kv => kv.Value * GetMaxApartmentAreaForType(kv.Key));
        }

        /// <summary>
        /// Требуемые типы помещений с учетом режима детализации.
        /// В режиме этажа исключает внутренние комнаты квартир, в режиме квартиры исключает МОП этажа.
        /// </summary>
        public List<RoomType> GetEffectiveRequiredRoomTypes()
        {
            if (RequiredRoomTypes == null || RequiredRoomTypes.Count == 0)
                return new List<RoomType>();

            var allowed = PlanningDetailMode == PlanningDetailMode.ApartmentRooms
                ? BuildApartmentAllowedRoomTypes(GetPrimaryApartmentType())
                : new HashSet<RoomType>
                {
                    RoomType.LivingRoom,
                    RoomType.CommonArea,
                    RoomType.Corridor,
                    RoomType.Lobby,
                    RoomType.Elevator,
                    RoomType.Staircase,
                    RoomType.Technical,
                    RoomType.Other
                };

            return RequiredRoomTypes.Where(allowed.Contains).Distinct().ToList();
        }

        private static HashSet<RoomType> BuildApartmentAllowedRoomTypes(string apartmentType)
        {
            var allowed = new HashSet<RoomType>
            {
                RoomType.LivingRoom,
                RoomType.Kitchen,
                RoomType.Bathroom,
                RoomType.Storage,
                RoomType.Balcony,
                RoomType.Corridor,
                RoomType.Technical,
                RoomType.Other
            };

            if (apartmentType == "TwoRoom"
                || apartmentType == "ThreeRoom"
                || apartmentType == "FourRoom")
            {
                allowed.Add(RoomType.Bedroom);
            }

            return allowed;
        }

        /// <summary>Общее запрошенное количество квартир.</summary>
        public int TotalApartmentsRequested =>
            StudioCount + OneRoomCount + TwoRoomCount + ThreeRoomCount + FourRoomCount;

        /// <summary>Количество квартир с учетом режима детализации.</summary>
        public int EffectiveApartmentsRequested =>
            PlanningDetailMode == PlanningDetailMode.ApartmentRooms
                ? 1
                : TotalApartmentsRequested;
    }
}
