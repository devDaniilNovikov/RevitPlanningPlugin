using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitPlanningPlugin.Infrastructure;
using RevitPlanningPlugin.Models.Api;
using RevitPlanningPlugin.Models.Domain;
using RevitPlanningPlugin.Models.Enums;
using RevitPlanningPlugin.Revit.Elements;
using RevitPlanningPlugin.Services.Api;
using RevitPlanningPlugin.Services.Configuration;
using RevitPlanningPlugin.Services.Geometry;
using RevitPlanningPlugin.Services.Logging;

namespace RevitPlanningPlugin.UI.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        // ——— Зависимости ———
        private readonly ExternalCommandData _commandData;
        private readonly ConfigurationService _configService;
        private IPlanningApiClient? _apiClient;
        private readonly ContourValidator _validator = new();
        private readonly RevitElementCreator _elementCreator = new();

        // ——— Состояние ———
        private GenerationStatus _status = GenerationStatus.Idle;
        private string _statusMessage = "Готов к работе";
        private BuildingContour? _currentContour;
        private LayoutVariant? _selectedVariant;
        private CancellationTokenSource? _cts;

        public MainViewModel(ExternalCommandData commandData)
        {
            _commandData = commandData;
            _configService = new ConfigurationService();
            Settings = _configService.Load();

            InitializeApiClient();
            InitializeCommands();
        }

        // ——— Свойства ———

        public PluginSettings Settings { get; private set; }

        public GenerationStatus Status
        {
            get => _status;
            set
            {
                SetProperty(ref _status, value);
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(IsBusy));
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsIdle => Status == GenerationStatus.Idle || Status == GenerationStatus.Completed;
        public bool IsBusy => !IsIdle;

        // Контуры
        public ObservableCollection<ApiContourSummaryDto> AvailableContours { get; } = new();

        private ApiContourSummaryDto? _selectedContourSummary;
        public ApiContourSummaryDto? SelectedContourSummary
        {
            get => _selectedContourSummary;
            set
            {
                SetProperty(ref _selectedContourSummary, value);
                OnPropertyChanged(nameof(CanLoadContour));
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
            }
        }

        public bool HasContour => _currentContour != null;
        public bool CanLoadContour => SelectedContourSummary != null && IsIdle;

        public string ContourInfo => _currentContour != null
            ? $"{_currentContour.Name} | {_currentContour.ApproximateArea:F1} м² | " +
              $"{_currentContour.OuterLoop.Count} сегментов"
            : "Контур не загружен";

        // Параметры генерации
        public GenerationParameters GenerationParams { get; set; } = new();

        // Варианты
        public ObservableCollection<LayoutVariant> Variants { get; } = new();

        public LayoutVariant? SelectedVariant
        {
            get => _selectedVariant;
            set
            {
                if (SetProperty(ref _selectedVariant, value) && value != null)
                    PreviewVariant(value);
                OnPropertyChanged(nameof(HasSelectedVariant));
                OnPropertyChanged(nameof(VariantInfo));
            }
        }

        public bool HasSelectedVariant => _selectedVariant != null;

        public string VariantInfo => _selectedVariant != null
            ? $"Вариант {_selectedVariant.VariantIndex + 1}: " +
              $"S={_selectedVariant.TotalArea:F1}м², полез.={_selectedVariant.UsableArea:F1}м², " +
              $"помещ.={_selectedVariant.RoomCount}, score={_selectedVariant.EfficiencyScore:F0}"
            : string.Empty;

        // Валидация
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
            ? string.Join("\n", _validationResult.Issues.Select(i => $"[{i.Severity}] {i.Message}"))
            : string.Empty;

        // ——— Команды ———

        public ICommand TestConnectionCommand { get; private set; } = null!;
        public ICommand LoadContoursCommand { get; private set; } = null!;
        public ICommand LoadSelectedContourCommand { get; private set; } = null!;
        public ICommand GenerateCommand { get; private set; } = null!;
        public ICommand ApplyVariantCommand { get; private set; } = null!;
        public ICommand ApplyWithWallsCommand { get; private set; } = null!;
        public ICommand SaveSettingsCommand { get; private set; } = null!;
        public ICommand CancelCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => IsIdle);
            LoadContoursCommand = new AsyncRelayCommand(LoadContoursAsync, () => IsIdle);
            LoadSelectedContourCommand = new AsyncRelayCommand(LoadSelectedContourAsync, () => CanLoadContour);
            GenerateCommand = new AsyncRelayCommand(GenerateAsync, () => HasContour && IsIdle);
            ApplyVariantCommand = new RelayCommand(ApplySelectedVariant, () => HasSelectedVariant && IsIdle);
            ApplyWithWallsCommand = new RelayCommand(ApplySelectedVariantWithWalls, () => HasSelectedVariant && IsIdle);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            CancelCommand = new RelayCommand(CancelOperation, () => IsBusy);
        }

        // ——— Методы ———

        private void InitializeApiClient()
        {
            _apiClient?.Dispose();
            _apiClient = new PlanningApiClient(Settings);
        }

        private void Dispose()
        {
            (_apiClient as IDisposable)?.Dispose();
        }

        private async Task TestConnectionAsync()
        {
            try
            {
                SetStatus(GenerationStatus.Loading, "Проверка соединения…");
                var ok = await _apiClient!.TestConnectionAsync();
                SetStatus(GenerationStatus.Idle,
                    ok ? "Соединение установлено ✓" : "Не удалось подключиться к API ✗");
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка: {ex.Message}");
            }
        }

        private async Task LoadContoursAsync()
        {
            try
            {
                SetStatus(GenerationStatus.Loading, "Загрузка списка контуров…");
                _cts = new CancellationTokenSource();

                var contours = await _apiClient!.GetContourListAsync(_cts.Token);

                AvailableContours.Clear();
                foreach (var c in contours)
                    AvailableContours.Add(c);

                SetStatus(GenerationStatus.Idle, $"Загружено {contours.Count} контуров.");
            }
            catch (OperationCanceledException)
            {
                SetStatus(GenerationStatus.Idle, "Отменено.");
            }
            catch (PlanningApiException ex)
            {
                SetStatus(GenerationStatus.Error, $"API: {ex.Message}");
                PluginLogger.Error(ex.Message, ex);
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка: {ex.Message}");
                PluginLogger.Error("Ошибка загрузки контуров", ex);
            }
        }

        private async Task LoadSelectedContourAsync()
        {
            if (SelectedContourSummary == null) return;

            try
            {
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
                    SetStatus(GenerationStatus.Error, "Контур не прошёл валидацию.");
                    return;
                }

                CurrentContour = contour;

                // Отрисовка в Revit
                var doc = _commandData.Application.ActiveUIDocument.Document;
                var view = doc.ActiveView;
                var level = GetActiveLevel(doc);

                _elementCreator.DrawContour(doc, view, contour, level);

                SetStatus(GenerationStatus.Idle, $"Контур '{contour.Name}' загружен и отображён.");
                EventAggregator.Instance.Publish(new ContourLoadedEvent { Contour = contour });
            }
            catch (OperationCanceledException)
            {
                SetStatus(GenerationStatus.Idle, "Отменено.");
            }
            catch (PlanningApiException ex)
            {
                SetStatus(GenerationStatus.Error, $"API: {ex.Message}");
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка: {ex.Message}");
                PluginLogger.Error("Ошибка загрузки контура", ex);
            }
        }

        private async Task GenerateAsync()
        {
            if (CurrentContour == null) return;

            try
            {
                SetStatus(GenerationStatus.Generating,
                    $"Генерация {GenerationParams.VariantCount} вариантов…");
                _cts = new CancellationTokenSource();

                var variants = await _apiClient!.GenerateLayoutsAsync(
                    CurrentContour.Id, GenerationParams, _cts.Token);

                Variants.Clear();
                foreach (var v in variants)
                    Variants.Add(v);

                if (Variants.Any())
                    SelectedVariant = Variants.First();

                SetStatus(GenerationStatus.Completed,
                    $"Получено {variants.Count} вариантов.");
                EventAggregator.Instance.Publish(new GenerationCompletedEvent { Variants = variants });
            }
            catch (OperationCanceledException)
            {
                SetStatus(GenerationStatus.Idle, "Генерация отменена.");
            }
            catch (PlanningApiException ex)
            {
                SetStatus(GenerationStatus.Error, $"API: {ex.Message}");
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка генерации: {ex.Message}");
                PluginLogger.Error("Ошибка генерации", ex);
            }
        }

        private void PreviewVariant(LayoutVariant variant)
        {
            try
            {
                var doc = _commandData.Application.ActiveUIDocument.Document;
                var view = doc.ActiveView;
                var level = GetActiveLevel(doc);
                _elementCreator.DrawLayoutPreview(doc, view, variant, level);
                EventAggregator.Instance.Publish(new VariantSelectedEvent { Variant = variant });
            }
            catch (Exception ex)
            {
                PluginLogger.Error("Ошибка предпросмотра", ex);
                StatusMessage = $"Ошибка предпросмотра: {ex.Message}";
            }
        }

        private void ApplySelectedVariant()
        {
            if (SelectedVariant == null) return;
            try
            {
                var doc = _commandData.Application.ActiveUIDocument.Document;
                var level = GetActiveLevel(doc);
                _elementCreator.ApplyLayout(doc, SelectedVariant, level);
                SetStatus(GenerationStatus.Completed,
                    $"Вариант '{SelectedVariant.Name}' применён (разделители + помещения).");
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка применения: {ex.Message}");
                PluginLogger.Error("Ошибка применения варианта", ex);
            }
        }

        private void ApplySelectedVariantWithWalls()
        {
            if (SelectedVariant == null) return;
            try
            {
                var doc = _commandData.Application.ActiveUIDocument.Document;
                var level = GetActiveLevel(doc);

                // Берём первый доступный тип стен
                var wallType = new FilteredElementCollector(doc)
                    .OfClass(typeof(WallType))
                    .Cast<WallType>()
                    .FirstOrDefault();

                if (wallType == null)
                {
                    SetStatus(GenerationStatus.Error, "Не найден тип стен в проекте.");
                    return;
                }

                _elementCreator.ApplyLayoutWithWalls(doc, SelectedVariant, level, wallType);
                SetStatus(GenerationStatus.Completed,
                    $"Вариант '{SelectedVariant.Name}' применён со стенами.");
            }
            catch (Exception ex)
            {
                SetStatus(GenerationStatus.Error, $"Ошибка: {ex.Message}");
                PluginLogger.Error("Ошибка применения со стенами", ex);
            }
        }

        private void SaveSettings()
        {
            _configService.Save(Settings);
            InitializeApiClient();
            StatusMessage = "Настройки сохранены.";
        }

        private void CancelOperation()
        {
            _cts?.Cancel();
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
            // Пытаемся взять уровень из активного вида
            if (doc.ActiveView.GenLevel != null)
                return doc.ActiveView.GenLevel;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .First();
        }
    }
}
