# RevitPlanningPlugin — Плагин генерации планировок для Autodesk Revit

## Обзор

Revit-плагин для получения архитектурных контуров из внешнего API, генерации вариантов планировочных решений и применения выбранного варианта в BIM-модель — без выхода из среды Revit.

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
│  BuildingContour, LayoutVariant, RoomLayout,                │
│  GenerationParameters, ValidationResult                     │
├─────────────────────────────────────────────────────────────┤
│  Infrastructure Layer                                        │
│  PlanningApiClient, DtoMapper, ConfigurationService,        │
│  ContourValidator, UnitConverter, PluginLogger              │
├─────────────────────────────────────────────────────────────┤
│  Revit Adapter Layer                                         │
│  RevitCurveBuilder, RevitElementCreator,                    │
│  SafeTransaction, CreatedElementsTracker                    │
└─────────────────────────────────────────────────────────────┘
```

## Структура проекта

```
src/RevitPlanningPlugin/
├── App.cs                          # IExternalApplication — точка входа
├── Commands/
│   └── OpenPlanningGeneratorCommand.cs
├── Models/
│   ├── Api/       ApiDtos.cs       # DTO для REST API
│   ├── Domain/    BuildingContour, LayoutVariant, RoomLayout, ...
│   └── Enums/     ApiEnvironment, RoomType, SegmentType, ...
├── Services/
│   ├── Api/       PlanningApiClient, DtoMapper
│   ├── Configuration/ ConfigurationService, PluginSettings
│   ├── Geometry/  ContourValidator, UnitConverter
│   └── Logging/   PluginLogger
├── Revit/
│   ├── Geometry/  RevitCurveBuilder
│   ├── Elements/  RevitElementCreator, CreatedElementsTracker
│   └── Transactions/ SafeTransaction
├── UI/
│   ├── Views/     MainWindow.xaml / .cs
│   ├── ViewModels/ MainViewModel, SettingsViewModel
│   └── Converters/ StatusToColorConverter, ...
├── Infrastructure/ ObservableObject, RelayCommand, EventAggregator
└── Resources/      Иконки (icon_16.png, icon_32.png)
```

## Требования

- **Autodesk Revit 2024** (или новее)
- **.NET Framework 4.8**
- **Visual Studio 2022** (17.x)
- NuGet-пакеты: `Newtonsoft.Json`, `NLog`

## Установка

### Для разработки

1. Откройте `RevitPlanningPlugin.sln` в Visual Studio.
2. Убедитесь, что пути к `RevitAPI.dll` и `RevitAPIUI.dll` в `.csproj` указывают на вашу установку Revit.
3. Соберите проект (`Ctrl+Shift+B`).

### Для использования

1. Скопируйте собранную DLL и файл `.addin` в папку Revit Add-ins:
   ```
   %AppData%\Autodesk\Revit\Addins\2024\
   ```
2. Скопируйте файлы:
   - `RevitPlanningPlugin.dll`
   - `RevitPlanningPlugin.addin`
   - `Newtonsoft.Json.dll`
   - `NLog.dll`
   - Папку `Resources/` (иконки)

3. Перезапустите Revit.
4. В Ribbon появится вкладка **«Планировки»** с кнопкой **«Генератор планировок»**.

## Настройка API

При первом запуске откройте вкладку **«Подключение»** и укажите:

| Параметр       | Описание                                          |
|----------------|---------------------------------------------------|
| Base URL       | Базовый URL внешнего API                           |
| Окружение      | dev / stage / prod                                 |
| API Key        | Ключ доступа (хранится зашифрованно через DPAPI)   |
| Bearer Token   | OAuth-токен (хранится зашифрованно)                |
| Таймаут        | Таймаут запросов в секундах (по умолчанию 30)      |

Настройки сохраняются в:
```
%AppData%\RevitPlanningPlugin\settings.json
```

## Пользовательский сценарий

1. **Подключение** — настройте доступ к API, проверьте соединение.
2. **Контуры** — загрузите список контуров, выберите нужный. Контур отобразится на активном уровне Revit.
3. **Генерация** — задайте параметры (количество вариантов, площади, ширина коридора, приоритет) и запустите генерацию.
4. **Результаты** — переключайтесь между вариантами в галерее. Для каждого отображаются метрики и список помещений. При выборе варианта — предпросмотр поверх контура.
5. **Применение** — примените вариант как:
   - **Разделители + помещения** (Room Separation Lines + Room элементы) — концептуальный режим.
   - **Стены + помещения** — со стенами из первого доступного типа в проекте.

При повторном применении предыдущие элементы автоматически удаляются (без дублирования).

## API-контракт

Плагин ожидает REST/JSON API по HTTPS со следующими эндпоинтами:

| Метод | Путь                    | Описание                     |
|-------|-------------------------|------------------------------|
| GET   | /health                 | Проверка доступности         |
| GET   | /contours               | Список контуров              |
| GET   | /contours/{id}          | Данные контура (геометрия)   |
| POST  | /generate               | Запуск генерации планировок  |

Подробные DTO описаны в `Models/Api/ApiDtos.cs`.

## Безопасность

- API-ключи и токены шифруются через **Windows DPAPI** (DataProtectionScope.CurrentUser).
- Секреты маскируются в логах (`***MASKED***`).
- Весь трафик передаётся по HTTPS.

## Логирование

Логи записываются в:
```
%AppData%\RevitPlanningPlugin\Logs\plugin_YYYY-MM-DD.log
```
Хранятся 30 дней, автоматическая ротация.

## Ограничения MVP

- Поддержка одного уровня/этажа.
- Концептуальное моделирование (разделители и Room-элементы, опционально стены).
- API-контракт может измениться — слой DTO изолирует доменную логику.
- Нет поддержки ядер, шахт, лестничных клеток (планируется в следующих итерациях).

## Расширение

Архитектура спроектирована для расширяемости:
- `IPlanningApiClient` — интерфейс для подключения альтернативных API.
- `DtoMapper` — изоляция от изменений API-контракта.
- `EventAggregator` — слабая связность между модулями.
- Модуль валидации легко расширить новыми правилами.
