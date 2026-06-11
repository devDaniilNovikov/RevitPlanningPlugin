using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPlanningPlugin.Infrastructure;
using RevitPlanningPlugin.Models.Api;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Revit.Elements;
using RevitPlanningPlugin.Revit.ExternalEvents;
using RevitPlanningPlugin.Revit.Extraction;
using RevitPlanningPlugin.Services.Api;
using RevitPlanningPlugin.Services.Configuration;
using RevitPlanningPlugin.Services.Diagnostics;
using RevitPlanningPlugin.Services.Geometry;
using RevitPlanningPlugin.Services.Logging;
using RevitPlanningPlugin.Services.Prompt;

namespace RevitPlanningPlugin.UI.ViewModels
{
    /// <summary>
    /// Главная ViewModel плагина.
    /// Оркестрирует полный бесшовный цикл:
    /// подключение → контур → генерация → каталог вариантов → применение.
    /// Пользователь работает исключительно внутри Revit, без экспорта/импорта.
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        public class LevelOption
        {
            public int Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public double ElevationMeters { get; set; }
            public override string ToString() => $"{Name} ({ElevationMeters:F2} м)";
        }

        public class PreviewRoomItem
        {
            public PointCollection Points { get; set; } = new();
            public Brush Fill { get; set; } = Brushes.LightGray;
            public Brush Stroke { get; set; } = Brushes.Gray;
            public string Label { get; set; } = string.Empty;
        }

        public class BufferedContourElement
        {
            public int ElementId { get; set; }
            public string DisplayName { get; set; } = string.Empty;
            public string Detail { get; set; } = string.Empty;
        }

        public class ApartmentContourItem
        {
            public RoomLayout SourceRoom { get; set; } = new();
            public string ApartmentId { get; set; } = string.Empty;
            public string ApartmentType { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string Detail { get; set; } = string.Empty;
        }

        // ——— Зависимости ———
        private readonly ExternalCommandData _commandData;
        private readonly RevitExternalEventRunner _revitRunner;
        private readonly ConfigurationService _configService;
        private IPlanningApiClient? _apiClient;
        private string _apiClientSettingsSignature = string.Empty;
        private readonly ContourValidator _validator = new();
        private readonly LayoutVariantValidator _layoutValidator = new();
        private readonly GenerationInputValidator _inputValidator = new();
        private readonly RevitContextExtractor _contextExtractor = new();
        private readonly RevitElementCreator _elementCreator = new();

        // ——— Состояние ———
        private GenerationStatus _status = GenerationStatus.Idle;
        private string _statusMessage = "Готов к работе";
        private BuildingContour? _currentContour;
        private LayoutVariant? _selectedVariant;
        private BufferedContourElement? _selectedBufferedContourElement;
        private ApartmentContourItem? _selectedApartmentContour;
        private CancellationTokenSource? _cts;
        private int _generationProgress;
        private string _generationElapsed = string.Empty;

        public MainViewModel(ExternalCommandData commandData, RevitExternalEventRunner revitRunner)
        {
            _commandData = commandData;
            _revitRunner = revitRunner;
            _configService = new ConfigurationService();
            Settings = _configService.Load();

            InitializeLevels();
            UpdateRequiredRoomTypes(_requiredRoomTypesText);
            InitializeApiClient();
            InitializeCommands();
        }

        // ═══════════════════════════════════════════
        //  Свойства: состояние, статус, прогресс
        // ═══════════════════════════════════════════

        public PluginSettings Settings { get; private set; }

        public GenerationStatus Status
        {
            get => _status;
            set
            {
                if (!SetProperty(ref _status, value))
                    return;

                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsGenerating));
                OnPropertyChanged(nameof(CanLoadContour));
                OnPropertyChanged(nameof(CanBuildBufferedContour));
                OnPropertyChanged(nameof(CanLoadApartmentContour));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsIdle => Status == GenerationStatus.Idle
                              || Status == GenerationStatus.Completed
                              || Status == GenerationStatus.Error;
        public bool IsBusy => !IsIdle;
        public bool IsGenerating => Status == GenerationStatus.Generating;

        /// <summary>Прогресс генерации (0–100).</summary>
        public int GenerationProgress
        {
            get => _generationProgress;
            set => SetProperty(ref _generationProgress, value);
        }

        /// <summary>Затраченное время на генерацию.</summary>
        public string GenerationElapsed
        {
            get => _generationElapsed;
            set => SetProperty(ref _generationElapsed, value);
        }

        // ═══════════════════════════════════════════
        //  Свойства: контуры
        // ═══════════════════════════════════════════

        public ObservableCollection<ApiContourSummaryDto> AvailableContours { get; } = new();

        public ObservableCollection<LevelOption> AvailableLevels { get; } = new();

        private LevelOption? _selectedLevel;
        public LevelOption? SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                SetProperty(ref _selectedLevel, value);
                OnPropertyChanged(nameof(SelectedLevelInfo));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string SelectedLevelInfo => SelectedLevel != null
            ? $"Уровень генерации: {SelectedLevel.Name}, отметка {SelectedLevel.ElevationMeters:F2} м"
            : "Уровень генерации: активный вид";

        private ApiContourSummaryDto? _selectedContourSummary;
        public ApiContourSummaryDto? SelectedContourSummary
        {
            get => _selectedContourSummary;
            set
            {
                SetProperty(ref _selectedContourSummary, value);
                OnPropertyChanged(nameof(CanLoadContour));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public BuildingContour? CurrentContour
        {
            get => _currentContour;
            set
            {
                SetProperty(ref _currentContour, value);
                OnPropertyChanged(nameof(HasContour));
                OnPropertyChanged(nameof(ContourInfo));
                OnPropertyChanged(nameof(ContourGeometryInfo));
                OnPropertyChanged(nameof(GenerationHistoryInfo));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasContour => _currentContour != null;
        public bool CanLoadContour => SelectedContourSummary != null && IsIdle;

        public string ContourInfo => _currentContour != null
            ? $"{_currentContour.Name} | {_currentContour.ApproximateArea:F1} м² | " +
              $"{_currentContour.OuterLoop.Count} сегментов"
            : "Контур не загружен";

        /// <summary>Описание геометрии: ортогональный / неортогональный / органичный.</summary>
        public string ContourGeometryInfo => _currentContour != null
            ? _currentContour.GeometryDescription
            : string.Empty;

        /// <summary>Инфо об истории генераций для текущего контура.</summary>
        public string GenerationHistoryInfo => _currentContour != null && _currentContour.TotalGeneratedVariants > 0
            ? $"Всего сгенерировано: {_currentContour.TotalGeneratedVariants} вариантов за {_currentContour.GenerationHistory.Count} запуск(ов)"
            : string.Empty;

        // ═══════════════════════════════════════════
        //  Свойства: параметры генерации
        // ═══════════════════════════════════════════

        public GenerationParameters GenerationParams { get; set; } = new();

        private string _requiredRoomTypesText = "Жилое помещение, Спальня, Кухня, Санузел, МОП, Коридор, Холл, Лифт, Лестница";
        public string RequiredRoomTypesText
        {
            get => _requiredRoomTypesText;
            set
            {
                if (SetProperty(ref _requiredRoomTypesText, value))
                    UpdateRequiredRoomTypes(value);
            }
        }

        // ═══════════════════════════════════════════
        //  Свойства: варианты (каталожный режим)
        // ═══════════════════════════════════════════

        /// <summary>Текущий набор вариантов (последняя генерация).</summary>
        public ObservableCollection<LayoutVariant> Variants { get; } = new();

        /// <summary>Все когда-либо сгенерированные варианты для текущего контура.</summary>
        public ObservableCollection<LayoutVariant> AllVariants { get; } = new();

        public ObservableCollection<PreviewRoomItem> PreviewRooms { get; } = new();
        public ObservableCollection<BufferedContourElement> BufferedContourElements { get; } = new();
        public ObservableCollection<ApartmentContourItem> AvailableApartmentContours { get; } = new();
        public ObservableCollection<PointCollection> PreviewInnerContourPoints { get; } = new();

        private PointCollection _previewContourPoints = new();
        public PointCollection PreviewContourPoints
        {
            get => _previewContourPoints;
            set => SetProperty(ref _previewContourPoints, value);
        }

        public double PreviewCanvasWidth => 900;
        public double PreviewCanvasHeight => 560;
        public double PreviewViewportHeight => 300;
        public bool HasPreview => PreviewRooms.Count > 0;

        /// <summary>Показывать все варианты из истории (или только последнюю генерацию).</summary>
        private bool _showAllHistory;
        public bool ShowAllHistory
        {
            get => _showAllHistory;
            set
            {
                SetProperty(ref _showAllHistory, value);
                OnPropertyChanged(nameof(DisplayedVariants));
            }
        }

        /// <summary>Коллекция для отображения в галерее.</summary>
        public ObservableCollection<LayoutVariant> DisplayedVariants
            => ShowAllHistory ? AllVariants : Variants;

        public LayoutVariant? SelectedVariant
        {
            get => _selectedVariant;
            set
            {
                if (SetProperty(ref _selectedVariant, value))
                {
                    if (value != null)
                    {
                        PreviewVariant(value);
                        RefreshAvailableApartmentContours(value);
                    }
                    else
                    {
                        ClearPreview();
                        RefreshAvailableApartmentContours(null);
                    }
                }
                OnPropertyChanged(nameof(HasSelectedVariant));
                OnPropertyChanged(nameof(VariantInfo));
                OnPropertyChanged(nameof(VariantApartmentTypeSummary));
                OnPropertyChanged(nameof(HasApartmentTypeInfo));
                OnPropertyChanged(nameof(SelectedVariantIndex));
                OnPropertyChanged(nameof(VariantNavigationInfo));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool HasSelectedVariant => _selectedVariant != null;

        /// <summary>Индекс выбранного варианта (1-based) для каталожной навигации.</summary>
        public int SelectedVariantIndex
        {
            get
            {
                if (_selectedVariant == null) return 0;
                var list = DisplayedVariants;
                var idx = list.IndexOf(_selectedVariant);
                return idx >= 0 ? idx + 1 : 0;
            }
        }

        /// <summary>Навигационная строка «3 / 10».</summary>
        public string VariantNavigationInfo
        {
            get
            {
                var list = DisplayedVariants;
                if (list.Count == 0) return string.Empty;
                return $"{SelectedVariantIndex} / {list.Count}";
            }
        }

        public string VariantInfo => _selectedVariant != null
            ? _selectedVariant.MetricsDetail
            : string.Empty;

        public string VariantApartmentTypeSummary => _selectedVariant?.ApartmentTypeSummary ?? string.Empty;

        public bool HasApartmentTypeInfo =>
            _selectedVariant != null && _selectedVariant.ApartmentTypeDistribution.Count > 0;

        public string BufferedContourInfo => BufferedContourElements.Count == 0
            ? "Стены контура не добавлены"
            : $"Добавлено стен/линий: {BufferedContourElements.Count}";

        public bool CanBuildBufferedContour => BufferedContourElements.Count >= 3 && IsIdle;

        public BufferedContourElement? SelectedBufferedContourElement
        {
            get => _selectedBufferedContourElement;
            set
            {
                if (SetProperty(ref _selectedBufferedContourElement, value))
                    CommandManager.InvalidateRequerySuggested();
            }
        }

        public ApartmentContourItem? SelectedApartmentContour
        {
            get => _selectedApartmentContour;
            set
            {
                if (SetProperty(ref _selectedApartmentContour, value))
                {
                    OnPropertyChanged(nameof(CanLoadApartmentContour));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool CanLoadApartmentContour => SelectedApartmentContour != null && IsIdle;

        // ═══════════════════════════════════════════
        //  Валидация
        // ═══════════════════════════════════════════

        private ValidationResult? _validationResult;
        public ValidationResult? ValidationResult
        {
            get => _validationResult;
            set
            {
                SetProperty(ref _validationResult, value);
                OnPropertyChanged(nameof(ValidationMessages));
            }
        }

        public string ValidationMessages => _validationResult != null
            ? string.Join("\n", _validationResult.Issues.Select(FormatValidationIssue))
            : string.Empty;

        private ValidationResult? _generationValidationResult;
        public ValidationResult? GenerationValidationResult
        {
            get => _generationValidationResult;
            set
            {
                SetProperty(ref _generationValidationResult, value);
                OnPropertyChanged(nameof(GenerationValidationMessages));
                OnPropertyChanged(nameof(HasGenerationValidationMessages));
            }
        }

        public bool HasGenerationValidationMessages =>
            _generationValidationResult != null && _generationValidationResult.Issues.Count > 0;

        public string GenerationValidationMessages => _generationValidationResult != null
            ? string.Join("\n", _generationValidationResult.Issues.Select(FormatValidationIssue))
            : string.Empty;

        // ═══════════════════════════════════════════
        //  Команды
        // ═══════════════════════════════════════════

        public ICommand TestConnectionCommand { get; private set; } = null!;
        public ICommand LoadContoursCommand { get; private set; } = null!;
        public ICommand LoadSelectedContourCommand { get; private set; } = null!;
        public ICommand ExtractSelectedContourCommand { get; private set; } = null!;
        public ICommand AddSelectedWallCommand { get; private set; } = null!;
        public ICommand BuildBufferedContourCommand { get; private set; } = null!;
        public ICommand RemoveBufferedWallCommand { get; private set; } = null!;
        public ICommand ClearBufferedWallsCommand { get; private set; } = null!;
        public ICommand LoadApartmentContourCommand { get; private set; } = null!;
        public ICommand GenerateCommand { get; private set; } = null!;
        public ICommand ApplyVariantCommand { get; private set; } = null!;
        public ICommand SaveSettingsCommand { get; private set; } = null!;
        public ICommand CancelCommand { get; private set; } = null!;

        // Каталожная навигация
        public ICommand NextVariantCommand { get; private set; } = null!;
        public ICommand PreviousVariantCommand { get; private set; } = null!;
        public ICommand ToggleHistoryCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => IsIdle);
            LoadContoursCommand = new AsyncRelayCommand(LoadContoursAsync, () => IsIdle);
            LoadSelectedContourCommand = new AsyncRelayCommand(LoadSelectedContourAsync, () => CanLoadContour);
            ExtractSelectedContourCommand = new AsyncRelayCommand(ExtractSelectedContourFromRevitAsync, () => IsIdle);
            AddSelectedWallCommand = new AsyncRelayCommand(AddSelectedWallAsync, () => IsIdle);
            BuildBufferedContourCommand = new AsyncRelayCommand(BuildBufferedContourAsync, () => CanBuildBufferedContour);
            RemoveBufferedWallCommand = new RelayCommand(RemoveBufferedWall, () => SelectedBufferedContourElement != null && IsIdle);
            ClearBufferedWallsCommand = new RelayCommand(ClearBufferedWalls, () => BufferedContourElements.Count > 0 && IsIdle);
            LoadApartmentContourCommand = new RelayCommand(LoadApartmentContour, () => CanLoadApartmentContour);
            GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => HasContour && IsIdle);
            ApplyVariantCommand = new AsyncRelayCommand(ApplySelectedVariantAsync, () => HasSelectedVariant && IsIdle);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            CancelCommand = new RelayCommand(CancelOperation, () => IsBusy);

            // Быстрый переключатель ← →
            NextVariantCommand = new RelayCommand(NavigateNext, () => HasSelectedVariant);
            PreviousVariantCommand = new RelayCommand(NavigatePrevious, () => HasSelectedVariant);
            ToggleHistoryCommand = new RelayCommand(() => ShowAllHistory = !ShowAllHistory);
        }

        private void InitializeLevels()
        {
            try
            {
                var doc = _commandData.Application.ActiveUIDocument.Document;
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level))
                    .Cast<Level>()
                    .OrderBy(l => l.Elevation)
                    .Select(l => new LevelOption
                    {
                        Id = l.Id.IntegerValue,
                        Name = l.Name,
                        ElevationMeters = l.Elevation * UnitConverter.FeetToMeters
                    })
                    .ToList();

                AvailableLevels.Clear();
                foreach (var level in levels)
                    AvailableLevels.Add(level);

                var activeLevel = doc.ActiveView?.GenLevel;
                SelectedLevel = activeLevel != null
                    ? AvailableLevels.FirstOrDefault(l => l.Id == activeLevel.Id.IntegerValue) ?? AvailableLevels.FirstOrDefault()
                    : AvailableLevels.FirstOrDefault();
            }
            catch (Exception ex)
            {
                PluginLogger.Warn($"Не удалось загрузить уровни Revit: {ex.Message}");
            }
        }

        private void UpdateRequiredRoomTypes(string text)
        {
            GenerationParams.RequiredRoomTypes.Clear();
            if (string.IsNullOrWhiteSpace(text)) return;

            var tokens = text.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens.Select(t => t.Trim()).Where(t => t.Length > 0))
            {
                if (Enum.TryParse<RoomType>(NormalizeRoomType(token), true, out var roomType)
                    && !GenerationParams.RequiredRoomTypes.Contains(roomType))
                {
                    GenerationParams.RequiredRoomTypes.Add(roomType);
                }
            }
        }

        private static string NormalizeRoomType(string token)
        {
            return token.ToLowerInvariant() switch
            {
                "mop" or "моп" or "common_area" or "common area"
                    or "место общего пользования" or "места общего пользования" or "общая зона" or "общий коридор"
                    => nameof(RoomType.CommonArea),
                "living_room" or "living room" or "жилое помещение" or "квартира" or "квартиры"
                    => nameof(RoomType.LivingRoom),
                "bedroom" or "спальня" or "спальная комната" => nameof(RoomType.Bedroom),
                "kitchen" or "кухня" or "кухонная зона" => nameof(RoomType.Kitchen),
                "bathroom" or "санузел" or "ванная" or "ванная комната" or "туалет" or "wc"
                    => nameof(RoomType.Bathroom),
                "corridor" or "коридор" or "общий проход" => nameof(RoomType.Corridor),
                "storage" or "кладовая" or "гардеробная" => nameof(RoomType.Storage),
                "lobby" or "холл" or "лифтовый холл" => nameof(RoomType.Lobby),
                "elevator" or "лифт" or "лифтовая шахта" => nameof(RoomType.Elevator),
                "staircase" or "лестница" or "лестничная клетка" => nameof(RoomType.Staircase),
                "balcony" or "балкон" or "лоджия" => nameof(RoomType.Balcony),
                "technical" or "техническое помещение" or "техпомещение" => nameof(RoomType.Technical),
                "meeting_room" or "meeting room" => nameof(RoomType.MeetingRoom),
                "open_space" or "open space" => nameof(RoomType.OpenSpace),
                _ => token
            };
        }

        private static string FormatValidationIssue(ValidationIssue issue)
            => $"[{DisplayValidationSeverity(issue.Severity)}] {issue.Message}";

        private static string DisplayValidationSeverity(ValidationSeverity severity)
        {
            return severity switch
            {
                ValidationSeverity.Info => "Информация",
                ValidationSeverity.Warning => "Предупреждение",
                ValidationSeverity.Error => "Ошибка",
                _ => severity.ToString()
            };
        }

        private static string DisplayRoomType(RoomType type)
        {
            return type switch
            {
                RoomType.LivingRoom => "Жилое помещение",
                RoomType.Bedroom => "Спальня",
                RoomType.Kitchen => "Кухня",
                RoomType.Bathroom => "Санузел",
                RoomType.Corridor => "Коридор",
                RoomType.Storage => "Кладовая",
                RoomType.Office => "Кабинет",
                RoomType.MeetingRoom => "Переговорная",
                RoomType.OpenSpace => "Открытое пространство",
                RoomType.Lobby => "Холл",
                RoomType.Technical => "Техническое помещение",
                RoomType.Staircase => "Лестничная клетка",
                RoomType.Elevator => "Лифт",
                RoomType.Balcony => "Балкон",
                RoomType.CommonArea => "МОП",
                _ => "Другое"
            };
        }

        private static string DisplayApartmentType(string apartmentType)
        {
            return apartmentType switch
            {
                "Studio" => "Студия",
                "OneRoom" => "1К",
                "TwoRoom" => "2К",
                "ThreeRoom" => "3К",
                "FourRoom" => "4К",
                _ => string.IsNullOrWhiteSpace(apartmentType) ? "тип не задан" : apartmentType
            };
        }

        private void RefreshAvailableApartmentContours(LayoutVariant? variant)
        {
            var previousApartmentId = SelectedApartmentContour?.ApartmentId;
            AvailableApartmentContours.Clear();

            if (variant != null && GenerationParams.PlanningDetailMode == PlanningDetailMode.FloorLayout)
            {
                foreach (var room in variant.Rooms.Where(IsApartmentShellRoom))
                {
                    var apartmentId = room.Properties.TryGetValue("apartment_id", out var id)
                        ? id
                        : room.Id;
                    var apartmentType = room.Properties.TryGetValue("apartment_type", out var type)
                        ? type
                        : string.Empty;
                    var displayName = string.IsNullOrWhiteSpace(room.Name)
                        ? $"Квартира {apartmentId}"
                        : room.Name;

                    AvailableApartmentContours.Add(new ApartmentContourItem
                    {
                        SourceRoom = room,
                        ApartmentId = apartmentId,
                        ApartmentType = apartmentType,
                        DisplayName = displayName,
                        Detail = $"{DisplayApartmentType(apartmentType)} | {room.Area:F1} м² | {room.Boundary.Count} сегм."
                    });
                }
            }

            SelectedApartmentContour = AvailableApartmentContours.FirstOrDefault(item =>
                                           !string.IsNullOrWhiteSpace(previousApartmentId)
                                           && string.Equals(item.ApartmentId, previousApartmentId, StringComparison.OrdinalIgnoreCase))
                                       ?? AvailableApartmentContours.FirstOrDefault();
        }

        private static bool IsApartmentShellRoom(RoomLayout room)
        {
            if (!room.Properties.TryGetValue("apartment_id", out var apartmentId)
                || string.IsNullOrWhiteSpace(apartmentId)
                || !room.Properties.TryGetValue("apartment_type", out var apartmentType)
                || string.IsNullOrWhiteSpace(apartmentType)
                || room.Boundary.Count < 3)
            {
                return false;
            }

            if (room.Properties.TryGetValue("room_role", out var role)
                && string.Equals(role, "apartment_shell", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return room.Type == RoomType.LivingRoom;
        }

        private void LoadApartmentContour()
        {
            if (SelectedApartmentContour == null)
                return;

            try
            {
                var apartment = SelectedApartmentContour;
                var contour = CreateApartmentContour(apartment);
                var validation = _validator.Validate(contour);
                ValidationResult = validation;
                GenerationValidationResult = validation;

                if (!validation.IsValid)
                {
                    SetStatus(GenerationStatus.Error, "Контур квартиры из preview не прошел валидацию.");
                    return;
                }

                ConfigureSingleApartmentMode(apartment.ApartmentType);
                LoadContourIntoState(contour);
                SetStatus(GenerationStatus.Idle,
                    $"Контур квартиры '{apartment.DisplayName}' загружен из preview. Режим переключен на планировку квартиры.");
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка подготовки контура квартиры: {ex.Message}");
                PluginLogger.Error("Ошибка подготовки контура квартиры из preview", ex);
            }
        }

        private static BuildingContour CreateApartmentContour(ApartmentContourItem apartment)
        {
            var room = apartment.SourceRoom;
            var contour = new BuildingContour
            {
                Id = $"preview-apartment-{apartment.ApartmentId}-{DateTime.Now:yyyyMMddHHmmss}",
                Name = $"Контур квартиры {apartment.DisplayName}",
                Description = "Контур взят из preview-варианта этажа. Используйте его для режима планировки квартиры.",
                SourceUnit = "m",
                OuterLoop = room.Boundary.Select(CloneSegment).ToList()
            };
            contour.Metadata["source"] = "preview_apartment_contour";
            contour.Metadata["apartment_id"] = apartment.ApartmentId;
            contour.Metadata["apartment_type"] = apartment.ApartmentType;
            contour.Metadata["source_room_id"] = room.Id;
            return contour;
        }

        private static ContourSegment CloneSegment(ContourSegment segment)
        {
            return new ContourSegment
            {
                Type = segment.Type,
                Start = ClonePoint(segment.Start),
                End = ClonePoint(segment.End),
                ArcCenter = segment.ArcCenter != null ? ClonePoint(segment.ArcCenter) : null,
                ArcRadius = segment.ArcRadius,
                ArcClockwise = segment.ArcClockwise,
                SplineControlPoints = segment.SplineControlPoints?.Select(ClonePoint).ToList(),
                NurbsWeights = segment.NurbsWeights?.ToList(),
                NurbsKnots = segment.NurbsKnots?.ToList(),
                NurbsDegree = segment.NurbsDegree,
                EllipseCenter = segment.EllipseCenter != null ? ClonePoint(segment.EllipseCenter) : null,
                EllipseRadiusX = segment.EllipseRadiusX,
                EllipseRadiusY = segment.EllipseRadiusY,
                EllipseRotation = segment.EllipseRotation,
                EllipseStartAngle = segment.EllipseStartAngle,
                EllipseEndAngle = segment.EllipseEndAngle
            };
        }

        private static Point2D ClonePoint(Point2D point)
            => new(point.X, point.Y);

        private void ConfigureSingleApartmentMode(string apartmentType)
        {
            GenerationParams.PlanningDetailMode = PlanningDetailMode.ApartmentRooms;
            GenerationParams.StudioCount = 0;
            GenerationParams.OneRoomCount = 0;
            GenerationParams.TwoRoomCount = 0;
            GenerationParams.ThreeRoomCount = 0;
            GenerationParams.FourRoomCount = 0;

            switch (apartmentType)
            {
                case "Studio":
                    GenerationParams.StudioCount = 1;
                    break;
                case "TwoRoom":
                    GenerationParams.TwoRoomCount = 1;
                    break;
                case "ThreeRoom":
                    GenerationParams.ThreeRoomCount = 1;
                    break;
                case "FourRoom":
                    GenerationParams.FourRoomCount = 1;
                    break;
                default:
                    GenerationParams.OneRoomCount = 1;
                    break;
            }

            OnPropertyChanged(nameof(GenerationParams));
        }

        private void LoadContourIntoState(BuildingContour contour)
        {
            CurrentContour = contour;
            Variants.Clear();
            AllVariants.Clear();
            SelectedVariant = null;
            GenerationValidationResult = null;
            ClearPreview();
            OnPropertyChanged(nameof(DisplayedVariants));
            OnPropertyChanged(nameof(VariantNavigationInfo));
        }

        private void RefreshBufferedContourCommands()
        {
            OnPropertyChanged(nameof(BufferedContourInfo));
            OnPropertyChanged(nameof(CanBuildBufferedContour));
            CommandManager.InvalidateRequerySuggested();
        }

        // ═══════════════════════════════════════════
        //  Каталожная навигация
        // ═══════════════════════════════════════════

        private void NavigateNext()
        {
            var list = DisplayedVariants;
            if (list.Count == 0) return;
            var idx = list.IndexOf(_selectedVariant);
            if (idx < list.Count - 1)
                SelectedVariant = list[idx + 1];
            else
                SelectedVariant = list[0]; // цикличный переход
        }

        private void NavigatePrevious()
        {
            var list = DisplayedVariants;
            if (list.Count == 0) return;
            var idx = list.IndexOf(_selectedVariant);
            if (idx > 0)
                SelectedVariant = list[idx - 1];
            else
                SelectedVariant = list[list.Count - 1]; // цикличный переход
        }

        // ═══════════════════════════════════════════
        //  Методы: API и подключение
        // ═══════════════════════════════════════════

        private void InitializeApiClient()
        {
            (_apiClient as IDisposable)?.Dispose();

            switch (Settings.EffectiveBackend)
            {
                case GenerationBackend.Mock:
                    _apiClient = new MockPlanningApiClient(Settings.MockScenario);
                    PluginLogger.Info($"API-клиент: режим Mock (сценарий {Settings.MockScenario}, без реального API).");
                    break;
                case GenerationBackend.ExternalApi:
                    _apiClient = new PlanningApiClient(Settings);
                    PluginLogger.Info($"API-клиент: внешний API ({SafeDisplayBaseUrl(Settings.EffectiveBaseUrl)}).");
                    break;
                default:
                    _apiClient = new LmStudioPlanningApiClient(Settings);
                    PluginLogger.Info($"API-клиент: AI Tunnel ({SafeDisplayBaseUrl(Settings.LmStudioBaseUrl)}, model={Settings.LmStudioModel}).");
                    break;
            }

            _apiClientSettingsSignature = BuildApiClientSettingsSignature();
        }

        private void EnsureApiClientCurrent()
        {
            var currentSignature = BuildApiClientSettingsSignature();
            if (_apiClient != null
                && string.Equals(_apiClientSettingsSignature, currentSignature, StringComparison.Ordinal))
            {
                return;
            }

            PluginLogger.Info("Настройки источника генерации изменились: API-клиент будет пересоздан перед операцией.");
            InitializeApiClient();
        }

        private string BuildApiClientSettingsSignature()
        {
            var backend = Settings.EffectiveBackend;
            var parts = new List<string>
            {
                $"backend={Settings.Backend}",
                $"effective_backend={backend}",
                $"use_mock={Settings.UseMockApi}",
                $"timeout={Settings.RequestTimeoutSeconds}",
                $"retries={Settings.MaxRetries}"
            };

            switch (backend)
            {
                case GenerationBackend.Mock:
                    parts.Add($"mock_scenario={Settings.MockScenario}");
                    break;
                case GenerationBackend.ExternalApi:
                    parts.Add($"base_url={NormalizeBaseUrl(Settings.EffectiveBaseUrl)}");
                    parts.Add($"environment={Settings.Environment}");
                    parts.Add($"api_key={GenerationRequestDiagnostics.SecretFingerprint(Settings.ApiKey)}");
                    parts.Add($"bearer={GenerationRequestDiagnostics.SecretFingerprint(Settings.BearerToken)}");
                    break;
                default:
                    parts.Add($"lm_url={NormalizeBaseUrl(Settings.LmStudioBaseUrl)}");
                    parts.Add($"lm_model={Settings.LmStudioModel}");
                    parts.Add($"lm_temperature={Settings.LmStudioTemperature.ToString("R", CultureInfo.InvariantCulture)}");
                    parts.Add($"lm_max_tokens={Settings.LmStudioMaxTokens}");
                    parts.Add($"api_key={GenerationRequestDiagnostics.SecretFingerprint(Settings.ApiKey)}");
                    parts.Add($"bearer={GenerationRequestDiagnostics.SecretFingerprint(Settings.BearerToken)}");
                    break;
            }

            return string.Join("|", parts);
        }

        private static string NormalizeBaseUrl(string? url)
            => string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim().TrimEnd('/');

        private static string SafeDisplayBaseUrl(string? url)
            => GenerationRequestDiagnostics.SafeDisplayUrl(url);

        private string DescribeActiveGenerationBackend()
        {
            return Settings.EffectiveBackend switch
            {
                GenerationBackend.Mock => $"Mock ({Settings.MockScenario})",
                GenerationBackend.ExternalApi => $"ExternalApi ({SafeDisplayBaseUrl(Settings.EffectiveBaseUrl)})",
                _ => $"AI Tunnel ({SafeDisplayBaseUrl(Settings.LmStudioBaseUrl)}, model={Settings.LmStudioModel})"
            };
        }

        private async Task TestConnectionAsync()
        {
            try
            {
                EnsureApiClientCurrent();
                SetStatus(GenerationStatus.Loading, "Проверка соединения…");
                var ok = await _apiClient!.TestConnectionAsync();
                SetStatus(GenerationStatus.Idle,
                    ok ? "Соединение установлено ✓" : "Не удалось подключиться к API ✗");
            }
            catch (PlanningApiException ex)
            {
                SetStatus(GenerationStatus.Error, $"API: {ex.Message}");
                PluginLogger.Error("Ошибка проверки соединения", ex);
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка: {ex.Message}");
                PluginLogger.Error("Ошибка проверки соединения", ex);
            }
        }

        private async Task LoadContoursAsync()
        {
            try
            {
                EnsureApiClientCurrent();
                SetStatus(GenerationStatus.Loading, "Загрузка списка контуров…");
                _cts = new CancellationTokenSource();

                var contours = await _apiClient!.GetContourListAsync(_cts.Token);

                AvailableContours.Clear();
                foreach (var c in contours)
                    AvailableContours.Add(c);

                if (SelectedContourSummary == null && AvailableContours.Count > 0)
                    SelectedContourSummary = AvailableContours[0];

                SetStatus(GenerationStatus.Idle, $"Загружено {contours.Count} контуров.");
            }
            catch (OperationCanceledException) { SetStatus(GenerationStatus.Idle, "Отменено."); }
            catch (PlanningApiException ex) { SetStatus(GenerationStatus.Error, $"API: {ex.Message}"); PluginLogger.Error(ex.Message, ex); }
            catch (Exception ex) { SetStatus(GenerationStatus.Error, $"Ошибка: {ex.Message}"); PluginLogger.Error("Ошибка загрузки контуров", ex); }
        }

        // ═══════════════════════════════════════════
        //  Методы: загрузка контура
        // ═══════════════════════════════════════════

        private async Task LoadSelectedContourAsync()
        {
            if (SelectedContourSummary == null) return;

            try
            {
                EnsureApiClientCurrent();
                SetStatus(GenerationStatus.Loading, $"Загрузка контура '{SelectedContourSummary.Name}'…");
                _cts = new CancellationTokenSource();

                var contour = await _apiClient!.GetContourAsync(SelectedContourSummary.Id, _cts.Token);

                // Конвертация единиц
                contour = UnitConverter.ConvertToMeters(contour);

                // Валидация
                var validation = _validator.Validate(contour);
                ValidationResult = validation;

                if (!validation.IsValid)
                {
                    SetStatus(GenerationStatus.Error, "Контур не прошёл валидацию. Исправьте геометрию или выберите другой.");
                    return;
                }

                CurrentContour = contour;

                // Очищаем предыдущие варианты при смене контура
                Variants.Clear();
                AllVariants.Clear();
                SelectedVariant = null;
                GenerationValidationResult = null;

                var geoNote = contour.HasCurvedGeometry
                    ? " (неортогональная/органичная форма)"
                    : " (ортогональная форма)";

                SetStatus(GenerationStatus.Idle, $"Контур '{contour.Name}' загружен{geoNote}. Запись в Revit будет выполнена только после применения варианта.");
                EventAggregator.Instance.Publish(new ContourLoadedEvent { Contour = contour });
            }
            catch (OperationCanceledException) { SetStatus(GenerationStatus.Idle, "Отменено."); }
            catch (PlanningApiException ex)
            {
                SetStatus(GenerationStatus.Error, $"API: {ex.Message}");
                PluginLogger.Error("Ошибка API при загрузке контура", ex);
            }
            catch (Exception ex) { SetStatus(GenerationStatus.Error, $"Ошибка: {ex.Message}"); PluginLogger.Error("Ошибка загрузки контура", ex); }
        }

        private async Task AddSelectedWallAsync()
        {
            try
            {
                SetStatus(GenerationStatus.Loading, "Чтение текущего выделения Revit…");

                var selected = await _revitRunner.RunAsync(app =>
                {
                    var uiDoc = app.ActiveUIDocument;
                    var doc = uiDoc.Document;
                    return uiDoc.Selection.GetElementIds()
                        .Select(doc.GetElement)
                        .Where(IsContourBufferElement)
                        .Select(ToBufferedContourElement)
                        .ToList();
                });

                var added = 0;
                foreach (var item in selected)
                {
                    if (BufferedContourElements.Any(existing => existing.ElementId == item.ElementId))
                        continue;

                    BufferedContourElements.Add(item);
                    added++;
                }

                if (added > 0)
                {
                    SelectedBufferedContourElement = BufferedContourElements.LastOrDefault();
                    SetStatus(GenerationStatus.Idle,
                        $"Добавлено элементов контура: {added}. Всего в списке: {BufferedContourElements.Count}.");
                }
                else if (selected.Count > 0)
                {
                    SetStatus(GenerationStatus.Idle, "Выбранные стены/линии уже есть в списке контура.");
                }
                else
                {
                    SetStatus(GenerationStatus.Idle, "Выделите одну или несколько стен/линий Revit и нажмите добавление еще раз.");
                }

                RefreshBufferedContourCommands();
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка добавления выбранной стены: {ex.Message}");
                PluginLogger.Error("Ошибка добавления выбранной стены Revit", ex);
            }
        }

        private async Task BuildBufferedContourAsync()
        {
            try
            {
                SetStatus(GenerationStatus.Loading, "Построение контура из добавленных стен Revit…");
                var bufferedIds = BufferedContourElements
                    .Select(item => item.ElementId)
                    .Distinct()
                    .ToList();

                var contour = await _revitRunner.RunAsync(app =>
                {
                    var uiDoc = app.ActiveUIDocument;
                    var doc = uiDoc.Document;
                    var level = GetActiveLevel(doc);
                    var elementIds = bufferedIds.Select(id => new ElementId(id)).ToList();
                    return _contextExtractor.TryExtractContourFromElementIds(
                        doc,
                        elementIds,
                        level,
                        "revit_buffered_walls",
                        $"Контур из добавленных стен Revit ({elementIds.Count} элементов)");
                });

                if (contour == null)
                {
                    SetStatus(GenerationStatus.Error,
                        "Не удалось построить замкнутый контур из добавленных стен. Проверьте, что стены соединены в один контур.");
                    return;
                }

                contour = UnitConverter.ConvertToMeters(contour);
                var validation = _validator.Validate(contour);
                ValidationResult = validation;
                GenerationValidationResult = validation;
                if (!validation.IsValid)
                {
                    SetStatus(GenerationStatus.Error, "Контур из добавленных стен не прошел валидацию.");
                    return;
                }

                LoadContourIntoState(contour);
                SetStatus(GenerationStatus.Idle,
                    $"Контур из добавленных стен Revit загружен: {contour.OuterLoop.Count} сегментов, {contour.ApproximateArea:F1} м².");
                EventAggregator.Instance.Publish(new ContourLoadedEvent { Contour = contour });
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка построения контура из стен: {ex.Message}");
                PluginLogger.Error("Ошибка построения контура из добавленных стен Revit", ex);
            }
        }

        private void RemoveBufferedWall()
        {
            if (SelectedBufferedContourElement == null)
                return;

            BufferedContourElements.Remove(SelectedBufferedContourElement);
            SelectedBufferedContourElement = BufferedContourElements.FirstOrDefault();
            RefreshBufferedContourCommands();
            SetStatus(GenerationStatus.Idle, $"Элемент удален из списка контура. Осталось: {BufferedContourElements.Count}.");
        }

        private void ClearBufferedWalls()
        {
            BufferedContourElements.Clear();
            SelectedBufferedContourElement = null;
            RefreshBufferedContourCommands();
            SetStatus(GenerationStatus.Idle, "Список стен контура очищен.");
        }

        private static bool IsContourBufferElement(Element? element)
        {
            if (element == null) return false;
            if (element is Wall || element is CurveElement)
                return true;

            var categoryId = element.Category?.Id.IntegerValue;
            return categoryId == (int)BuiltInCategory.OST_RoomSeparationLines
                   || categoryId == (int)BuiltInCategory.OST_Walls;
        }

        private static BufferedContourElement ToBufferedContourElement(Element element)
        {
            var category = element.Category?.Name ?? "Без категории";
            var name = string.IsNullOrWhiteSpace(element.Name)
                ? category
                : element.Name;

            return new BufferedContourElement
            {
                ElementId = element.Id.IntegerValue,
                DisplayName = $"{name} [{element.Id.IntegerValue}]",
                Detail = $"{category} | {element.GetType().Name}"
            };
        }

        private async Task ExtractSelectedContourFromRevitAsync()
        {
            try
            {
                SetStatus(GenerationStatus.Loading, "Извлечение контура из выделения Revit…");

                var contour = await _revitRunner.RunAsync(app =>
                {
                    var uiDoc = app.ActiveUIDocument;
                    var doc = uiDoc.Document;
                    var level = GetActiveLevel(doc);
                    return _contextExtractor.TryExtractSelectedContour(uiDoc, doc, level);
                });
                if (contour == null)
                {
                    SetStatus(GenerationStatus.Error,
                        "Не удалось извлечь контур. Сначала выделите стены, помещения, разделители помещений или линии на активном уровне, затем нажмите кнопку еще раз.");
                    return;
                }

                contour = UnitConverter.ConvertToMeters(contour);
                var validation = _validator.Validate(contour);
                ValidationResult = validation;

                if (!validation.IsValid)
                {
                    SetStatus(GenerationStatus.Error, "Извлеченный контур не прошел валидацию.");
                    return;
                }

                LoadContourIntoState(contour);

                SetStatus(GenerationStatus.Idle,
                    $"Контур из Revit загружен: {contour.OuterLoop.Count} сегментов, {contour.ApproximateArea:F1} м². Модель не изменена.");
                EventAggregator.Instance.Publish(new ContourLoadedEvent { Contour = contour });
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка извлечения контура: {ex.Message}");
                PluginLogger.Error("Ошибка извлечения контура из Revit", ex);
            }
        }

        // ═══════════════════════════════════════════
        //  Методы: пакетная генерация с прогрессом
        // ═══════════════════════════════════════════

        private async Task GenerateAsync()
        {
            if (CurrentContour == null) return;

            try
            {
                EnsureApiClientCurrent();
                var sw = Stopwatch.StartNew();
                GenerationProgress = 0;
                GenerationElapsed = string.Empty;

                var inputValidation = _inputValidator.Validate(GenerationParams, CurrentContour, RequiredRoomTypesText);
                GenerationValidationResult = inputValidation;
                if (!inputValidation.IsValid)
                {
                    SetStatus(GenerationStatus.Error, "Параметры генерации не прошли проверку. Исправьте ошибки перед отправкой в ИИ-сервис.");
                    PluginLogger.Warn($"Генерация заблокирована preflight-валидацией: {GenerationValidationMessages}");
                    return;
                }

                var activeBackend = Settings.EffectiveBackend;
                var backendDescription = DescribeActiveGenerationBackend();
                var backendStatusSuffix = activeBackend == GenerationBackend.Mock
                    ? " (Mock-режим)"
                    : string.Empty;
                PluginLogger.Info($"Запуск генерации: backend={backendDescription}.");
                SetStatus(GenerationStatus.Generating,
                    $"Пакетная генерация {GenerationParams.VariantCount} вариантов{backendStatusSuffix}…");
                _cts = new CancellationTokenSource();

                // Прогресс: 10% — отправка, 80% — ожидание, 10% — обработка
                GenerationProgress = 10;
                GenerationElapsed = "Отправка запроса…";

                var requestContext = await _revitRunner.RunAsync(app =>
                {
                    var uiDoc = app.ActiveUIDocument;
                    var doc = uiDoc.Document;
                    var level = GetActiveLevel(doc);
                    return _contextExtractor.BuildRequestContext(
                        uiDoc, doc, level, CurrentContour, GenerationParams);
                });
                if (activeBackend == GenerationBackend.LmStudio)
                    requestContext.GenerationNonce = GenerationRequestDiagnostics.CreateGenerationNonce();

                requestContext.Prompt = PromptBuilder.Build(requestContext);
                var parameterFingerprint = GenerationRequestDiagnostics.BuildParameterFingerprint(requestContext);
                var promptFingerprint = GenerationRequestDiagnostics.BuildPromptFingerprint(requestContext.Prompt);
                PluginLogger.Info(
                    $"Сформирован запрос генерации {requestContext.RequestId}: " +
                    $"type={GenerationParams.GenerationType}, variants={GenerationParams.VariantCount}, " +
                    $"validation={GenerationParams.ValidationMode}, contour={CurrentContour.Id}, level={requestContext.ProjectContext.LevelName}, " +
                    $"backend={backendDescription}, parameter_fingerprint={parameterFingerprint}, " +
                    $"prompt_fingerprint={promptFingerprint}, prompt_length={requestContext.Prompt.Length}, " +
                    $"generation_nonce={(string.IsNullOrWhiteSpace(requestContext.GenerationNonce) ? "none" : requestContext.GenerationNonce)}.");

                var variants = await _apiClient!.GenerateLayoutsAsync(requestContext, _cts.Token);
                AttachGenerationDiagnostics(
                    variants,
                    requestContext,
                    activeBackend,
                    backendDescription,
                    parameterFingerprint,
                    promptFingerprint);

                GenerationProgress = 90;
                GenerationElapsed = $"Получено {variants.Count} вариантов, проверка результата…";

                var layoutValidation = _layoutValidator.Validate(variants, GenerationParams, CurrentContour);
                GenerationValidationResult = layoutValidation;
                if (!layoutValidation.IsValid)
                {
                    GenerationProgress = 0;
                    var message = DescribeLayoutValidationFailure(layoutValidation);
                    SetStatus(GenerationStatus.Error, message);
                    PluginLogger.Warn($"Результат AI Tunnel заблокирован валидацией: {GenerationValidationMessages}");
                    return;
                }

                GenerationElapsed = $"Получено {variants.Count} вариантов, генерация миниатюр…";

                // Генерация SVG-миниатюр для каталожного отображения
                ThumbnailGenerator.GenerateThumbnails(variants, CurrentContour);

                // Сохраняем в историю контура
                CurrentContour.AddGenerationResult(variants);

                // Обновляем текущие варианты
                Variants.Clear();
                foreach (var v in variants)
                    Variants.Add(v);

                // Обновляем общий список (все генерации по контуру)
                AllVariants.Clear();
                foreach (var v in CurrentContour.GetAllVariants())
                    AllVariants.Add(v);

                GenerationProgress = 100;
                sw.Stop();
                GenerationElapsed = $"Готово за {sw.Elapsed.TotalSeconds:F1} сек";

                if (Variants.Any())
                    SelectedVariant = Variants.First();

                OnPropertyChanged(nameof(GenerationHistoryInfo));
                OnPropertyChanged(nameof(DisplayedVariants));
                OnPropertyChanged(nameof(VariantNavigationInfo));

                SetStatus(GenerationStatus.Completed,
                    $"Получено {variants.Count} вариантов{backendStatusSuffix} за {sw.Elapsed.TotalSeconds:F1} сек. " +
                    $"Всего по контуру: {CurrentContour.TotalGeneratedVariants}.");
                EventAggregator.Instance.Publish(new GenerationCompletedEvent { Variants = variants });
            }
            catch (OperationCanceledException)
            {
                GenerationProgress = 0;
                SetStatus(GenerationStatus.Idle, "Генерация отменена.");
            }
            catch (PlanningApiException ex)
            {
                GenerationProgress = 0;
                var diagnostic = DescribeAiServiceFailure(ex);
                GenerationValidationResult = ValidationResult.Fail(diagnostic, ex.ErrorCode);
                SetStatus(GenerationStatus.Error, diagnostic);
                PluginLogger.Error("Ошибка API при генерации", ex);
            }
            catch (Exception ex) { SetStatus(GenerationStatus.Error, $"Ошибка генерации: {ex.Message}"); PluginLogger.Error("Ошибка генерации", ex); }
        }

        private static string DescribeAiServiceFailure(PlanningApiException ex)
        {
            var code = ex.ErrorCode ?? string.Empty;
            if (code.StartsWith("LM_STUDIO_NETWORK", StringComparison.OrdinalIgnoreCase)
                || code.StartsWith("LM_STUDIO_TIMEOUT", StringComparison.OrdinalIgnoreCase))
            {
                return "AI Tunnel недоступен или не ответил в заданный таймаут. Проверьте интернет, URL и API-ключ.";
            }

            if (string.Equals(code, "LM_STUDIO_MODEL_NOT_LOADED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "LM_STUDIO_MODEL_REQUIRED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "LM_STUDIO_MODELS_EMPTY", StringComparison.OrdinalIgnoreCase))
            {
                return ex.Message;
            }

            if (string.Equals(code, "LM_STUDIO_GENERATION_ERROR", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "GENERATION_ERROR", StringComparison.OrdinalIgnoreCase))
                return "ИИ-сервис не смог сформировать планировку по заданным ограничениям: " + ex.Message;

            if (string.Equals(code, "LM_STUDIO_JSON_NOT_FOUND", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "LM_STUDIO_JSON_NOT_CLOSED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "LM_STUDIO_INVALID_JSON", StringComparison.OrdinalIgnoreCase))
            {
                return "ИИ-сервис вернул невалидный JSON: " + ex.Message;
            }

            if (string.Equals(code, "INVALID_API_CONTRACT", StringComparison.OrdinalIgnoreCase))
            {
                return "ИИ-сервис вернул JSON, который нельзя применить к Revit: " + ex.Message;
            }

            return "ИИ-сервис: " + ex.Message;
        }

        private static string DescribeLayoutValidationFailure(ValidationResult result)
        {
            var firstError = result.Issues.FirstOrDefault(i => i.Severity == ValidationSeverity.Error);
            if (firstError == null)
                return "Результат генерации содержит предупреждения.";

            var code = firstError.Code ?? string.Empty;
            if (IsHallucinationCode(code))
            {
                return "Результат похож на галлюцинацию ИИ: геометрия помещения выходит за допустимый контур. Предпросмотр и запись в Revit заблокированы.";
            }

            return "Результат генерации не прошел строгую проверку: " + firstError.Message;
        }

        private void AttachGenerationDiagnostics(
            IEnumerable<LayoutVariant> variants,
            GenerationRequestContext requestContext,
            GenerationBackend backend,
            string backendDescription,
            string parameterFingerprint,
            string promptFingerprint)
        {
            foreach (var variant in variants)
            {
                variant.Metadata["generation_request_id"] = requestContext.RequestId;
                variant.Metadata["generation_backend"] = backend.ToString();
                variant.Metadata["generation_backend_detail"] = backendDescription;
                variant.Metadata["parameter_fingerprint"] = parameterFingerprint;
                variant.Metadata["prompt_fingerprint"] = promptFingerprint;
                if (!string.IsNullOrWhiteSpace(requestContext.GenerationNonce))
                    variant.Metadata["generation_nonce"] = requestContext.GenerationNonce;
            }
        }

        private static bool IsHallucinationCode(string code)
        {
            return string.Equals(code, "ROOM_OUTSIDE_CONTOUR", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(code, "ROOM_LABEL_POINT_OUTSIDE_CONTOUR", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(code, "ROOM_LABEL_POINT_OUTSIDE_ROOM", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(code, "ROOMS_OVERLAP", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(code, "ROOM_BOUNDARY_SELF_INTERSECTION", StringComparison.OrdinalIgnoreCase);
        }

        // ═══════════════════════════════════════════
        //  Методы: предпросмотр и применение
        // ═══════════════════════════════════════════

        private void PreviewVariant(LayoutVariant variant)
        {
            try
            {
                BuildPreview(variant);
                OnPropertyChanged(nameof(VariantNavigationInfo));
                StatusMessage = $"Выбран предпросмотр варианта #{SelectedVariantIndex}. Модель Revit не изменена.";
                EventAggregator.Instance.Publish(new VariantSelectedEvent { Variant = variant });
            }
            catch (Exception ex)
            {
                PluginLogger.Error("Ошибка предпросмотра", ex);
                StatusMessage = $"Ошибка предпросмотра: {ex.Message}";
            }
        }

        private void BuildPreview(LayoutVariant variant)
        {
            PreviewRooms.Clear();
            PreviewInnerContourPoints.Clear();
            PreviewContourPoints = new PointCollection();

            var allPoints = new List<Point2D>();
            if (CurrentContour != null)
                allPoints.AddRange(CurrentContour.GetOuterVertices());
            allPoints.AddRange(variant.Rooms.SelectMany(room => room.Boundary.SelectMany(seg => new[] { seg.Start, seg.End })));

            if (allPoints.Count == 0)
            {
                OnPropertyChanged(nameof(HasPreview));
                return;
            }

            var minX = allPoints.Min(p => p.X);
            var maxX = allPoints.Max(p => p.X);
            var minY = allPoints.Min(p => p.Y);
            var maxY = allPoints.Max(p => p.Y);
            var dataW = Math.Max(0.01, maxX - minX);
            var dataH = Math.Max(0.01, maxY - minY);
            const double padding = 12;
            var scale = Math.Min((PreviewCanvasWidth - padding * 2) / dataW,
                (PreviewCanvasHeight - padding * 2) / dataH);

            System.Windows.Point Transform(Point2D point)
            {
                return new System.Windows.Point(
                    padding + (point.X - minX) * scale,
                    padding + (maxY - point.Y) * scale);
            }

            if (CurrentContour != null)
            {
                PreviewContourPoints = ToPointCollection(CurrentContour.GetOuterVertices(), Transform);
                foreach (var inner in CurrentContour.GetInnerVertices())
                {
                    if (inner.Count >= 3)
                        PreviewInnerContourPoints.Add(ToPointCollection(inner, Transform));
                }
            }

            foreach (var room in variant.Rooms)
            {
                var vertices = room.Boundary.Select(s => s.Start).ToList();
                if (vertices.Count < 3) continue;

                PreviewRooms.Add(new PreviewRoomItem
                {
                    Points = ToPointCollection(vertices, Transform),
                    Fill = RoomFill(room.Type),
                    Stroke = Brushes.DimGray,
                    Label = BuildPreviewRoomTooltip(room, variant)
                });
            }

            OnPropertyChanged(nameof(HasPreview));
        }

        private void ClearPreview()
        {
            PreviewRooms.Clear();
            PreviewInnerContourPoints.Clear();
            PreviewContourPoints = new PointCollection();
            OnPropertyChanged(nameof(HasPreview));
        }

        private static PointCollection ToPointCollection(
            IEnumerable<Point2D> points,
            Func<Point2D, System.Windows.Point> transform)
        {
            var collection = new PointCollection();
            foreach (var point in points)
                collection.Add(transform(point));
            return collection;
        }

        private static string BuildPreviewRoomTooltip(RoomLayout room, LayoutVariant variant)
        {
            var lines = new List<string>
            {
                $"{room.Name} | {DisplayRoomType(room.Type)} | {room.Area:F1} м²"
            };

            if (!room.Properties.TryGetValue("apartment_id", out var apartmentId)
                || string.IsNullOrWhiteSpace(apartmentId))
            {
                return string.Join(Environment.NewLine, lines);
            }

            var apartmentRooms = variant.Rooms
                .Where(candidate => candidate.Properties.TryGetValue("apartment_id", out var id)
                                    && string.Equals(id, apartmentId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var apartmentType = room.Properties.TryGetValue("apartment_type", out var type)
                ? type
                : apartmentRooms
                    .Select(candidate => candidate.Properties.TryGetValue("apartment_type", out var candidateType)
                        ? candidateType
                        : string.Empty)
                    .FirstOrDefault(candidateType => !string.IsNullOrWhiteSpace(candidateType)) ?? string.Empty;

            lines.Add($"Квартира: {apartmentId}");
            if (!string.IsNullOrWhiteSpace(apartmentType))
            {
                lines.Add($"Тип: {DisplayApartmentType(apartmentType)}");
                lines.Add($"Количество комнат: {DisplayApartmentRoomCount(apartmentType, apartmentRooms)}");
            }
            else
            {
                lines.Add($"Количество комнат: {DisplayApartmentRoomCount(apartmentType, apartmentRooms)}");
            }

            if (apartmentRooms.Count > 1)
            {
                var composition = apartmentRooms
                    .OrderBy(candidate => candidate.Name)
                    .Select(candidate => $"{candidate.Name} ({DisplayRoomType(candidate.Type)})")
                    .ToList();
                lines.Add($"Состав: {string.Join(", ", composition)}");
            }
            else if (IsApartmentShellRoom(room))
            {
                lines.Add("Состав: квартира-оболочка");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string DisplayApartmentRoomCount(string apartmentType, IReadOnlyCollection<RoomLayout> apartmentRooms)
        {
            var countByType = ApartmentRoomCountFromType(apartmentType);
            if (countByType.HasValue)
                return countByType.Value == 0 ? "0 комнат (студия)" : FormatRoomCount(countByType.Value);

            var residentialRooms = apartmentRooms.Count(room =>
                room.Type == RoomType.LivingRoom || room.Type == RoomType.Bedroom);
            if (residentialRooms > 0)
                return FormatRoomCount(residentialRooms);

            return $"{apartmentRooms.Count} помещ.";
        }

        private static int? ApartmentRoomCountFromType(string apartmentType)
        {
            return apartmentType switch
            {
                "Studio" => 0,
                "OneRoom" => 1,
                "TwoRoom" => 2,
                "ThreeRoom" => 3,
                "FourRoom" => 4,
                _ => null
            };
        }

        private static string FormatRoomCount(int count)
        {
            var lastTwo = count % 100;
            if (lastTwo >= 11 && lastTwo <= 14)
                return $"{count} комнат";

            return (count % 10) switch
            {
                1 => $"{count} комната",
                2 or 3 or 4 => $"{count} комнаты",
                _ => $"{count} комнат"
            };
        }

        private static Brush RoomFill(RoomType type)
        {
            return type switch
            {
                RoomType.CommonArea or RoomType.Corridor or RoomType.Lobby or RoomType.Elevator or RoomType.Staircase
                    => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC8, 0xE6, 0xC9)),
                RoomType.LivingRoom or RoomType.Bedroom or RoomType.Kitchen
                    => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xBB, 0xDE, 0xFB)),
                RoomType.Bathroom
                    => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB2, 0xEB, 0xF2)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEE, 0xEE, 0xEE))
            };
        }

        private async Task ApplySelectedVariantAsync()
        {
            if (SelectedVariant == null) return;
            try
            {
                if (!ValidateSelectedVariantForApply())
                    return;

                var confirm = MessageBox.Show(
                    $"Создать черновые Room Separation Lines и Room для варианта '{SelectedVariant.Name}'?\n\n" +
                    "Операция будет выполнена в транзакции Revit. Ранее созданный этим плагином черновик будет заменен.",
                    "Подтверждение записи в Revit",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                {
                    SetStatus(GenerationStatus.Idle, "Применение отменено пользователем. Модель Revit не изменена.");
                    PluginLogger.Info($"Пользователь отменил применение варианта '{SelectedVariant.Name}'.");
                    return;
                }

                var variantToApply = SelectedVariant;
                await _revitRunner.RunAsync(app =>
                {
                    var doc = app.ActiveUIDocument.Document;
                    var level = GetActiveLevel(doc);
                    PluginLogger.Info($"Пользователь подтвердил применение варианта '{variantToApply.Name}' на уровне '{level.Name}'.");
                    _elementCreator.ApplyLayout(doc, variantToApply, level);
                });
                SetStatus(GenerationStatus.Completed,
                    $"Вариант #{SelectedVariantIndex} применён (разделители + помещения).");
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка применения: {ex.Message}");
                PluginLogger.Error("Ошибка применения варианта", ex);
            }
        }

        private bool ValidateSelectedVariantForApply()
        {
            if (SelectedVariant == null || CurrentContour == null)
                return false;

            var strictParameters = CreateStrictValidationParameters();
            var result = _layoutValidator.Validate(new[] { SelectedVariant }, strictParameters, CurrentContour);
            GenerationValidationResult = result;

            if (!result.IsValid)
            {
                SetStatus(GenerationStatus.Error,
                    "Выбранный вариант не прошел обязательную проверку. Запись в Revit заблокирована.");
                PluginLogger.Warn($"Применение варианта '{SelectedVariant.Name}' заблокировано: {GenerationValidationMessages}");
                return false;
            }

            PluginLogger.Info($"Вариант '{SelectedVariant.Name}' прошел обязательную проверку перед записью.");
            return true;
        }

        private GenerationParameters CreateStrictValidationParameters()
        {
            return new GenerationParameters
            {
                VariantCount = 1,
                GenerationType = GenerationParams.GenerationType,
                PlanningDetailMode = GenerationParams.PlanningDetailMode,
                ValidationMode = ValidationMode.Strict,
                TextPrompt = GenerationParams.TextPrompt,
                StudioCount = GenerationParams.StudioCount,
                OneRoomCount = GenerationParams.OneRoomCount,
                TwoRoomCount = GenerationParams.TwoRoomCount,
                ThreeRoomCount = GenerationParams.ThreeRoomCount,
                FourRoomCount = GenerationParams.FourRoomCount,
                MinApartmentArea = GenerationParams.MinApartmentArea,
                MaxApartmentArea = GenerationParams.MaxApartmentArea,
                StudioMaxApartmentArea = GenerationParams.StudioMaxApartmentArea,
                OneRoomMaxApartmentArea = GenerationParams.OneRoomMaxApartmentArea,
                TwoRoomMaxApartmentArea = GenerationParams.TwoRoomMaxApartmentArea,
                ThreeRoomMaxApartmentArea = GenerationParams.ThreeRoomMaxApartmentArea,
                FourRoomMaxApartmentArea = GenerationParams.FourRoomMaxApartmentArea,
                MopAreaTarget = GenerationParams.MopAreaTarget,
                MinCorridorWidth = GenerationParams.MinCorridorWidth,
                RequiredRoomTypes = GenerationParams.RequiredRoomTypes.ToList(),
                MinRoomArea = GenerationParams.MinRoomArea,
                MaxRoomArea = GenerationParams.MaxRoomArea,
                OptimizationPriority = GenerationParams.OptimizationPriority,
                CustomParameters = new Dictionary<string, string>(GenerationParams.CustomParameters)
            };
        }

        // ═══════════════════════════════════════════
        //  Методы: настройки и управление
        // ═══════════════════════════════════════════

        private void SaveSettings()
        {
            try
            {
                _configService.Save(Settings);
                OnPropertyChanged(nameof(Settings));
                InitializeApiClient();
                SetStatus(GenerationStatus.Idle, "Настройки сохранены.");
            }
            catch (InvalidOperationException ex)
            {
                SetStatus(GenerationStatus.Error, ex.Message);
                PluginLogger.Error("Ошибка сохранения настроек", ex);
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка сохранения настроек: {ex.Message}");
                PluginLogger.Error("Ошибка сохранения настроек", ex);
            }
        }

        private void CancelOperation()
        {
            _cts?.Cancel();
            GenerationProgress = 0;
            SetStatus(GenerationStatus.Idle, "Операция отменена.");
        }

        private void SetStatus(GenerationStatus status, string message)
        {
            Status = status;
            StatusMessage = message;
            EventAggregator.Instance.Publish(new StatusChangedEvent { Status = status, Message = message });
        }

        private Level GetActiveLevel(Document doc)
        {
            if (SelectedLevel != null)
            {
                var selected = doc.GetElement(new ElementId(SelectedLevel.Id)) as Level;
                if (selected != null)
                    return selected;
            }

            return _contextExtractor.GetActiveLevel(doc);
        }
    }
}
