# RevitPlanningPlugin — Плагин генерации планировок для Autodesk Revit

## Обзор

Revit-плагин для получения архитектурных контуров здания из внешнего API,
AI-генерации вариантов размещения **квартир и МОПов** внутри пятна здания
и применения выбранного варианта в BIM-модель — без выхода из среды Revit.

Версия **2.0** — с поддержкой квартирографии, МОПов (мест общего пользования),
повторной перегенерации по корректировкам и Mock-режима для разработки.

---

## Что нового в версии 2.0

### МОПы (места общего пользования)
- Добавлен тип помещения `CommonArea` (МОП) — лифтовые холлы, общие коридоры, помещения общего назначения.
- В метриках каждого варианта отдельно отображается **суммарная площадь МОПов** (`MopArea`).
- В параметрах генерации можно задать **целевую площадь МОПов** и **минимальную ширину коридора МОП**.
- В списке помещений МОП-комнаты выделены зелёным цветом для визуального различия.

### Квартирография (состав квартир)
- В параметрах генерации задаётся **состав квартир по типам**:
  студии, 1-комнатные, 2-комнатные, 3-комнатные, 4-комнатные.
- В метриках варианта отображается **количество квартир** и
  **распределение по типам** (например: «1К: 4 | 2К: 6 | 3К: 2»).
- В API-запросе передаётся `apartment_types` (словарь тип → количество),
  `min_apartment_area`, `max_apartment_area`.

### Mock-режим (режим без реального API)
- В настройках добавлен флаг **«Использовать Mock-клиент»**.
- Mock генерирует реалистичные планировки с квартирами и МОПами:
  центральная полоса МОП (лифтовый холл + коридоры), квартиры по обе стороны.
- Удобно для разработки и демонстрации без внешнего сервера.

### Привязка PasswordBox
- Поля **API Key** и **Bearer Token** теперь корректно читают и сохраняют значения
  через `PasswordChanged` в code-behind (стандартная WPF-практика для безопасного ввода).

### Обновлённые метрики в галерее вариантов
Для каждого варианта отображаются все метрики, требуемые по ТЗ:

| Метрика                       | Где отображается                     |
|-------------------------------|--------------------------------------|
| Общая площадь                 | Детали варианта                      |
| Полезная площадь              | Детали варианта                      |
| Количество квартир            | Карточка + Детали варианта           |
| Распределение по типам квартир| Детали варианта (строка «Состав»)    |
| Суммарная площадь МОПов       | Карточка (чип) + Детали варианта     |
| Площадь коридоров             | Детали варианта                      |
| Коэффициент эффективности     | Карточка (полоса) + Score-бейдж      |

---

## Архитектура

```
┌─────────────────────────────────────────────────────────────┐
│  UI Layer  (WPF / MVVM)                                     │
│  MainWindow, ViewModels, Converters, Ribbon commands        │
├─────────────────────────────────────────────────────────────┤
│  Application Layer  (оркестрация сценариев)                  │
│  MainViewModel: load → validate → generate → preview → apply│
├─────────────────────────────────────────────────────────────┤
│  Domain Layer  (модели, правила, валидация)                  │
│  BuildingContour, LayoutVariant (+ MopArea, ApartmentCount, │
│  ApartmentTypeDistribution), GenerationParameters           │
│  (+ MopAreaTarget, StudioCount, OneRoomCount, ...)          │
├─────────────────────────────────────────────────────────────┤
│  Infrastructure Layer                                        │
│  PlanningApiClient, MockPlanningApiClient, DtoMapper,       │
│  ConfigurationService (+ UseMockApi), PluginLogger          │
├─────────────────────────────────────────────────────────────┤
│  Revit Adapter Layer                                         │
│  RevitCurveBuilder, RevitElementCreator,                    │
│  SafeTransaction, CreatedElementsTracker                    │
└─────────────────────────────────────────────────────────────┘
```

## Структура проекта

```
src/RevitPlanningPlugin/
├── App.cs                              # IExternalApplication — точка входа
├── Commands/
│   └── OpenPlanningGeneratorCommand.cs
├── Models/
│   ├── Api/
│   │   └── ApiDtos.cs                  # DTO для REST API (+ MOP/apartment поля)
│   ├── Domain/
│   │   ├── BuildingContour.cs
│   │   ├── GenerationParameters.cs     # + MopAreaTarget, StudioCount..FourRoomCount
│   │   ├── LayoutVariant.cs            # + MopArea, ApartmentCount, ApartmentTypeDistribution
│   │   ├── RoomLayout.cs
│   │   └── ContourSegment.cs
│   └── Enums/
│       ├── RoomType.cs                 # + CommonArea (МОП)
│       ├── ApiEnvironment.cs
│       ├── GenerationStatus.cs
│       ├── SegmentType.cs
│       └── ValidationSeverity.cs
├── Services/
│   ├── Api/
│   │   ├── PlanningApiClient.cs        # REST-клиент с retry и авторизацией
│   │   ├── MockPlanningApiClient.cs    # Mock с квартирами и МОП-зонами
│   │   └── DtoMapper.cs               # DTO ↔ Domain (+ новые поля)
│   ├── Configuration/
│   │   └── ConfigurationService.cs    # + UseMockApi в PluginSettings
│   ├── Geometry/
│   │   ├── ContourValidator.cs
│   │   ├── UnitConverter.cs
│   │   └── ThumbnailGenerator.cs
│   └── Logging/
│       └── PluginLogger.cs
├── Revit/
│   ├── Geometry/  RevitCurveBuilder
│   ├── Elements/  RevitElementCreator, CreatedElementsTracker
│   └── Transactions/ SafeTransaction
├── UI/
│   ├── Views/
│   │   ├── MainWindow.xaml             # + вкладки МОП, квартирография
│   │   └── MainWindow.xaml.cs         # + PasswordBox.PasswordChanged binding
│   ├── ViewModels/
│   │   └── MainViewModel.cs           # + UseMockApi toggle, apartment info
│   └── Converters/
│       └── Converters.cs
├── Infrastructure/
│   ├── MvvmBase.cs
│   └── EventAggregator.cs
└── Resources/  icon_16.png, icon_32.png
```

---

## Требования

- **Autodesk Revit 2024** (или новее)
- **.NET Framework 4.8**
- **Visual Studio 2022** (17.x)
- NuGet: `Newtonsoft.Json 13.x`, `NLog 5.x`

---

## Установка

### Для разработки

1. Откройте `RevitPlanningPlugin.sln` в Visual Studio.
2. Убедитесь, что пути к `RevitAPI.dll` / `RevitAPIUI.dll` в `.csproj` указывают на вашу установку.
3. Соберите проект (`Ctrl+Shift+B`).

### Для использования

1. Скопируйте в папку Revit Add-ins:
   ```
   %AppData%\Autodesk\Revit\Addins\2024\
   ```
   Файлы: `RevitPlanningPlugin.dll`, `RevitPlanningPlugin.addin`,
   `Newtonsoft.Json.dll`, `NLog.dll`, папка `Resources/`.

2. Перезапустите Revit.
3. В Ribbon появится вкладка **«Планировки»** → кнопка **«Генератор планировок»**.

---

## Настройка API

Вкладка **«1. Подключение»**:

| Параметр         | Описание                                                      |
|------------------|---------------------------------------------------------------|
| Base URL         | Базовый URL внешнего API                                      |
| Окружение        | dev / stage / prod                                            |
| API Key          | Ключ доступа (хранится зашифрованно через DPAPI)              |
| Bearer Token     | OAuth-токен (хранится зашифрованно)                           |
| Таймаут (сек)    | Таймаут HTTP-запросов (по умолчанию 30 с)                     |
| Mock-режим       | Работа без реального API — для разработки и тестирования      |

Настройки: `%AppData%\RevitPlanningPlugin\settings.json`

---

## Пользовательский сценарий

### Базовый сценарий
1. **Подключение** — настройте доступ к API (или включите Mock-режим) и проверьте соединение.
2. **Контур** — загрузите список, выберите контур, он отобразится на активном уровне Revit.
3. **Генерация** — задайте:
   - Количество вариантов (1–20)
   - **Состав квартир**: студии, 1К, 2К, 3К, 4К и их количество
   - Мин./макс. площадь квартиры
   - **Целевую площадь МОПов** (0 = автоматически)
   - Минимальную ширину коридора МОП
   - Приоритет оптимизации
4. **Результаты** — галерея вариантов с метриками квартир и МОПов.
   Переключение стрелками ← → или в списке. Предпросмотр поверх контура.
5. **Применение** — концептуальный режим (разделители + помещения) или со стенами.

### Сценарий корректировки (ТЗ 3.2)
- После первичной генерации измените параметры (площадь МОПов, состав квартир и т.д.).
- Нажмите **«Сгенерировать»** повторно — новые варианты заменят предыдущие.

---

## API-контракт

REST/JSON по HTTPS:

| Метод | Путь            | Описание                    |
|-------|-----------------|-----------------------------|
| GET   | /health         | Проверка доступности        |
| GET   | /contours       | Список контуров             |
| GET   | /contours/{id}  | Геометрия контура           |
| POST  | /generate       | Запуск AI-генерации         |

### Параметры запроса генерации (`POST /generate`)

```json
{
  "contour_id": "building-1",
  "variant_count": 3,
  "apartment_types": { "Studio": 2, "OneRoom": 4, "TwoRoom": 6, "ThreeRoom": 2 },
  "min_apartment_area": 25.0,
  "max_apartment_area": 120.0,
  "mop_area_target": 80.0,
  "min_corridor_width": 1.4,
  "optimization_priority": "efficiency"
}
```

### Ответ генерации (на каждый вариант)

```json
{
  "id": "v1",
  "variant_index": 1,
  "total_area": 600.0,
  "usable_area": 480.0,
  "mop_area": 72.0,
  "corridor_area": 40.0,
  "apartment_count": 14,
  "apartment_type_distribution": { "OneRoom": 4, "TwoRoom": 6, "ThreeRoom": 4 },
  "efficiency_score": 82,
  "rooms": [...],
  "partitions": [...]
}
```

Подробные DTO: `Models/Api/ApiDtos.cs`

---

## Безопасность

- API-ключи и токены шифруются через **Windows DPAPI** (`DataProtectionScope.CurrentUser`).
- Секреты маскируются в логах.
- Весь трафик — по HTTPS.
- PasswordBox не использует binding (WPF security) — значения передаются через `PasswordChanged`.

---

## Логирование

```
%AppData%\RevitPlanningPlugin\Logs\plugin_YYYY-MM-DD.log
```
Ротация: 30 дней. Логируются API-вызовы, ответы, ошибки парсинга и Revit API.

---

## Ограничения MVP

- Один уровень/этаж (поддержка нескольких — следующие итерации).
- Концептуальное моделирование: разделители и Room-элементы, опционально стены.
- Ядра, шахты, лестничные клетки — следующие итерации.
- API-контракт может измениться: слой DTO (`DtoMapper`) изолирует доменную логику.
