# Инструкция по установке RevitPlanningPlugin

## Предварительные требования

- Autodesk Revit 2024 или новее
- Windows 10/11
- .NET Framework 4.8 (входит в состав Windows 10 1903+)

## Шаг 1: Получение файлов плагина

После сборки проекта скопируйте из папки `bin/Release` следующие файлы:

```
RevitPlanningPlugin.dll
Newtonsoft.Json.dll
NLog.dll
Resources/
  icon_16.png
  icon_32.png
```

## Шаг 2: Размещение файлов

### Вариант A: Для текущего пользователя

Скопируйте файлы в:
```
%AppData%\Autodesk\Revit\Addins\2024\RevitPlanningPlugin\
```

Скопируйте `RevitPlanningPlugin.addin` в:
```
%AppData%\Autodesk\Revit\Addins\2024\
```

### Вариант B: Для всех пользователей

Скопируйте файлы в:
```
C:\ProgramData\Autodesk\Revit\Addins\2024\RevitPlanningPlugin\
```

**Важно:** При варианте B обновите путь `<Assembly>` в файле `.addin`:

```xml
<Assembly>C:\ProgramData\Autodesk\Revit\Addins\2024\RevitPlanningPlugin\RevitPlanningPlugin.dll</Assembly>
```

## Шаг 3: Запуск Revit

1. Запустите Revit 2024.
2. При появлении диалога «Безопасность загрузки надстроек» нажмите **«Всегда загружать»**.
3. В Ribbon появится вкладка **«Планировки»** → панель **«Генератор»** → кнопка **«Генератор планировок»**.

## Шаг 4: Первоначальная настройка

1. Нажмите кнопку «Генератор планировок».
2. Перейдите на вкладку «Подключение».
3. Укажите Base URL вашего API.
4. Введите API Key или Bearer Token.
5. Нажмите «Сохранить», затем «Проверить соединение».

## Удаление

1. Удалите файл `.addin` из папки Add-ins.
2. Удалите папку `RevitPlanningPlugin` из папки Add-ins.
3. (Опционально) Удалите настройки: `%AppData%\RevitPlanningPlugin\`

## Поддержка нескольких версий Revit

Для Revit 2025 создайте копию `.addin` в папке:
```
%AppData%\Autodesk\Revit\Addins\2025\
```

При этом может потребоваться пересборка с ссылками на Revit API 2025.
