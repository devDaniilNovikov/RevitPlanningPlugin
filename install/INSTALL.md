# Инструкция по установке RevitPlanningPlugin

## Предварительные требования

- Autodesk Revit 2022
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

Скопируйте `RevitPlanningPlugin.addin` и все файлы сборки в одну папку:
```
%AppData%\Autodesk\Revit\Addins\2022\
```

Файл `.addin` содержит относительный путь `<Assembly>RevitPlanningPlugin.dll</Assembly>`,
поэтому DLL должна лежать рядом с `.addin`.

### Вариант B: Для всех пользователей

Скопируйте `RevitPlanningPlugin.addin` и все файлы сборки в одну папку:
```
C:\ProgramData\Autodesk\Revit\Addins\2022\
```

Если вы хотите хранить DLL в подпапке `RevitPlanningPlugin`, обновите путь
`<Assembly>` в файле `.addin`:

```xml
<Assembly>C:\ProgramData\Autodesk\Revit\Addins\2022\RevitPlanningPlugin\RevitPlanningPlugin.dll</Assembly>
```

## Шаг 3: Запуск Revit

1. Запустите Revit 2022.
2. При появлении диалога «Безопасность загрузки надстроек» нажмите **«Всегда загружать»**.
3. В Ribbon появится вкладка **«Планировки»** → панель **«Генератор»** → кнопка **«Генератор планировок»**.

## Шаг 4: Первоначальная настройка

1. Нажмите кнопку «Генератор планировок».
2. Перейдите на вкладку «Подключение».
3. Для production-режима выберите Backend = `LmStudio` (в UI отображается как AI Tunnel).
4. Укажите AI Tunnel URL: `https://api.aitunnel.ru/v1`.
5. Укажите модель AI Tunnel, по умолчанию `gemma-4-31b-it`.
6. Введите API key в поле «Ключ API». Ключ хранится локально через DPAPI и не должен попадать в репозиторий.
7. Нажмите «Сохранить», затем «Проверить соединение».

Если используется отдельный REST-сервис с эндпоинтами `/health`, `/contours` и `/generate`, выберите Backend = `ExternalApi`, укажите External API URL и при необходимости API Key или Bearer Token.

## Удаление

1. Удалите файл `.addin` из папки Add-ins.
2. Удалите `RevitPlanningPlugin.dll`, зависимости и папку `Resources` из папки Add-ins.
3. (Опционально) Удалите настройки: `%AppData%\RevitPlanningPlugin\`

## Поддержка нескольких версий Revit

Для другой версии Revit создайте копию `.addin` в соответствующей папке:
```
%AppData%\Autodesk\Revit\Addins\<версия>\
```

При этом нужна пересборка с ссылками на API этой версии, например:
```
msbuild RevitPlanningPlugin.sln /p:Configuration=Release /p:RevitVersion=2024
```
