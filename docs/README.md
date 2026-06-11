# RevitPlanningPlugin — Плагин генерации планировок для Autodesk Revit

## Обзор

Revit-плагин для получения архитектурных контуров здания из внешнего API
или текущей Revit-модели, AI-генерации вариантов размещения **квартир и МОПов**
внутри пятна здания и применения выбранного варианта в BIM-модель — без выхода
из среды Revit.

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

### Production-интеграция с AI Tunnel
- Основной backend генерации — OpenAI-compatible API AI Tunnel.
- Значения по умолчанию: `https://api.aitunnel.ru/v1`, модель `gemma-4-31b-it`.
- Production-дефолты: `temperature = 0.1`, `max_tokens = 12000`, таймаут HTTP-запроса `180 с`.
- Плагин отправляет Revit-контекст в `/v1/chat/completions`, получает JSON, извлекает структурированный объект даже из ответа с markdown/reasoning-префиксом и блокирует результат, если DTO-контракт нарушен.

### Mock-режим (режим без реального API)
- В настройках добавлен флаг **«Использовать Mock-клиент»**.
- Mock генерирует реалистичные планировки с квартирами и МОПами:
  центральная полоса МОП (лифтовый холл + коридоры), квартиры по обе стороны.
- В настройках доступен **Mock-сценарий**:
  `HappyPath` для успешной генерации, `GenerationError` для воспроизводимой
  ошибки AI-сервиса, `Hallucination` для варианта с помещением вне контура.
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
│  MainViewModel: extract/load → validate → generate → preview → apply│
├─────────────────────────────────────────────────────────────┤
│  Domain Layer  (модели, правила, валидация)                  │
│  BuildingContour, LayoutVariant, RevitProjectContext,       │
│  GenerationRequestContext, GenerationParameters             │
│  (+ GenerationType, ValidationMode, TextPrompt, МОП поля)   │
├─────────────────────────────────────────────────────────────┤
│  Infrastructure Layer                                        │
│  PlanningApiClient, MockPlanningApiClient, DtoMapper,       │
│  PromptBuilder, GenerationInputValidator,                   │
│  ApiResponseContractValidator, LayoutVariantValidator,      │
│  ConfigurationService                                       │
├─────────────────────────────────────────────────────────────┤
│  Revit Adapter Layer                                         │
│  RevitContextExtractor, RevitCurveBuilder,                  │
│  RevitElementCreator, SafeTransaction, trackers             │
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
│   │   ├── GenerationParameters.cs     # + GenerationType, ValidationMode, TextPrompt, МОП/apartment fields
│   │   ├── GenerationRequestContext.cs # единый AI-запрос: Revit-контекст + контур + prompt
│   │   ├── RevitProjectContext.cs      # уровень, вид, параметры проекта, существующие элементы
│   │   ├── LayoutVariant.cs            # + MopArea, ApartmentCount, ApartmentTypeDistribution
│   │   ├── RoomLayout.cs
│   │   └── ContourSegment.cs
│   └── Enums/
│       ├── RoomType.cs                 # + CommonArea (МОП)
│       ├── ApiEnvironment.cs
│       ├── GenerationBackend.cs
│       ├── GenerationStatus.cs
│       ├── SegmentType.cs
│       └── ValidationSeverity.cs
├── Services/
│   ├── Api/
│   │   ├── PlanningApiClient.cs        # REST-клиент с retry и авторизацией
│   │   ├── LmStudioPlanningApiClient.cs # AI Tunnel/OpenAI-compatible /v1/chat/completions
│   │   ├── MockPlanningApiClient.cs    # Mock с квартирами и МОП-зонами
│   │   ├── ApiResponseContractValidator.cs # строгая проверка JSON-ответа
│   │   └── DtoMapper.cs               # DTO ↔ Domain (+ новые поля)
│   ├── Configuration/
│   │   └── ConfigurationService.cs    # backend, AI Tunnel, External API и Mock settings
│   ├── Geometry/
│   │   ├── ContourValidator.cs
│   │   ├── GenerationInputValidator.cs
│   │   ├── LayoutVariantValidator.cs
│   │   ├── UnitConverter.cs
│   │   └── ThumbnailGenerator.cs
│   ├── Prompt/
│   │   └── PromptBuilder.cs
│   └── Logging/
│       └── PluginLogger.cs
├── Revit/
│   ├── Extraction/ RevitContextExtractor
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

- **Autodesk Revit 2022**
- **.NET Framework 4.8**
- **Visual Studio 2022** (17.x)
- NuGet: `Newtonsoft.Json 13.x`, `NLog 5.x`

---

## Установка

### Для разработки

1. Откройте `RevitPlanningPlugin.sln` в Visual Studio.
2. По умолчанию проект ищет Revit API 2022 в `C:\Program Files\Autodesk\Revit 2022`.
   Для другой версии передайте MSBuild-свойство, например `/p:RevitVersion=2024`.
3. Соберите проект (`Ctrl+Shift+B`).

### Для использования

1. Скопируйте в папку Revit Add-ins:
   ```
   %AppData%\Autodesk\Revit\Addins\2022\
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
| Backend          | `LmStudio` (AI Tunnel), `ExternalApi` или `Mock`               |
| AI Tunnel URL    | OpenAI-compatible URL, обычно `https://api.aitunnel.ru/v1`     |
| AI model         | Имя модели AI Tunnel, по умолчанию `gemma-4-31b-it`     |
| External API URL | Базовый URL внешнего REST API                                 |
| Окружение        | dev / stage / prod                                            |
| API Key          | Ключ доступа (хранится зашифрованно через DPAPI)              |
| Bearer Token     | OAuth-токен (хранится зашифрованно)                           |
| Таймаут (сек)    | Таймаут HTTP-запросов (по умолчанию 180 с)                    |
| Mock-сценарий    | HappyPath / GenerationError / Hallucination                   |

Настройки: `%AppData%\RevitPlanningPlugin\settings.json`

---

## Пользовательский сценарий

### Базовый сценарий
1. **Подключение** — выберите backend `LmStudio` (в UI отображается как AI Tunnel), URL `https://api.aitunnel.ru/v1`, модель `gemma-4-31b-it` или другую доступную модель AI Tunnel, затем введите API key.
2. **Контур** — загрузите список из API или выделите замкнутые линии/стены в Revit и извлеките контур из модели.
3. **Генерация** — задайте:
   - Тип генерации
   - Количество вариантов (1–20)
   - **Состав квартир**: студии, 1К, 2К, 3К, 4К и их количество
   - Мин./макс. площадь квартиры
   - **Целевую площадь МОПов** (0 = автоматически)
   - Минимальную ширину коридора МОП
   - Текстовый промпт
   - Режим проверки результата
   - Приоритет оптимизации
4. **Результаты** — галерея вариантов с метриками квартир и МОПов.
   Переключение стрелками ← → или в списке. Предпросмотр выполняется в UI и миниатюрах, без создания постоянных элементов Revit.
5. **Применение** — после обязательной строгой валидации и отдельного подтверждения пользователя создаются только черновые Room Separation Lines и Room-элементы.

### Сценарий корректировки (ТЗ 3.2)
- После первичной генерации измените параметры (площадь МОПов, состав квартир и т.д.).
- Нажмите **«Сгенерировать»** повторно — новые варианты заменят предыдущие.

---

## API-контракт

Плагин поддерживает два production-контура интеграции:

- `LmStudio`: AI Tunnel/OpenAI-compatible `/v1/chat/completions`, где модель возвращает тот же JSON-конверт результата.
- `ExternalApi`: внешний REST/JSON сервис с эндпоинтами ниже.

REST/JSON ExternalApi:

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
  "max_apartment_area_by_type": { "Studio": 35.0, "OneRoom": 45.0, "TwoRoom": 70.0, "ThreeRoom": 95.0 },
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
- Пользовательский prompt передается как проектные требования и изолируется от инструкций, которые могут менять JSON-контракт или правила валидации.
- Ответ AI-сервиса не применяется напрямую: сначала извлекается JSON, проверяется DTO-контракт, затем доменная геометрия и состав помещений.

---

## Логирование

```
%AppData%\RevitPlanningPlugin\Logs\plugin_YYYY-MM-DD.log
```
Ротация: 30 дней. Логируются запуск генерации, контур/уровень, безопасная сводка параметров, API-вызовы, ошибки парсинга, ошибки валидации, подтверждение или отмена пользователем, транзакции Revit и rollback.

---

## Ограничения MVP

- Один уровень/этаж (поддержка нескольких — следующие итерации).
- Концептуальное моделирование: разделители и Room-элементы. Создание стен не входит в текущий приемочный сценарий.
- Ядра, шахты, лестничные клетки — следующие итерации.
- API-контракт фиксируется в `docs/API_CONTRACT.md`; слой DTO (`DtoMapper`) изолирует доменную логику, а `ApiResponseContractValidator` блокирует невалидный ответ.
