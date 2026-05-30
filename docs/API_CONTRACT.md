# API-контракт для интеграции с RevitPlanningPlugin

## Общие сведения

| Параметр | Значение |
|----------|----------|
| Протокол | HTTPS |
| Формат | REST / JSON |
| Аутентификация | API Key (заголовок `X-API-Key`) или Bearer Token (`Authorization: Bearer <token>`) |
| Content-Type | `application/json` |

## Production-режим LM Studio

Для локальной LLM используется backend `LmStudio`. Он обращается к OpenAI-compatible API LM Studio:

| Параметр | Значение по умолчанию |
|----------|------------------------|
| Base URL | `http://localhost:1234/v1` |
| Endpoint | `POST /chat/completions` |
| Model | `google/gemma-4-e4b` или фактическое имя загруженной модели в LM Studio |
| Temperature | `0.2` |
| JSON mode | `response_format.type = json_object` |

Плагин отправляет в LM Studio `GenerationRequestContext`: Revit-контекст, контур, параметры генерации и полный `llm_prompt`.
LM Studio должна вернуть в `choices[0].message.content` один JSON-объект в том же конверте результата, что описан ниже:

```json
{
  "success": true,
  "data": {
    "request_id": "gen_456",
    "status": "completed",
    "variants": []
  },
  "error": null
}
```

Если локальная модель возвращает markdown-блок или reasoning-префикс, клиент извлекает первый JSON-объект, затем строго валидирует его через DTO-контракт. Ответ без JSON, с невалидным JSON или с нарушением схемы не применяется к Revit-модели.

Backend `ExternalApi` использует REST-эндпоинты `/health`, `/contours`, `/contours/{id}`, `/generate`. Backend `Mock` оставлен только для тестов и демонстраций.

## Конверт ответа

Все ответы оборачиваются в единый конверт:

```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```

При ошибке:

```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "AUTH_ERROR",
    "message": "Невалидный API-ключ."
  }
}
```

---

## Эндпоинты

### 1. GET /health

Проверка доступности сервиса.

**Ответ:** HTTP 200 (тело не обязательно).

---

### 2. GET /contours

Получение списка доступных контуров.

**Ответ:**

```json
{
  "success": true,
  "data": {
    "contours": [
      {
        "id": "abc123",
        "name": "Этаж 1 — корпус A",
        "area": 620.5,
        "description": "Типовой этаж офисного здания"
      }
    ],
    "total": 1
  }
}
```

---

### 3. GET /contours/{id}

Получение геометрии контура по идентификатору.

**Ответ:**

```json
{
  "success": true,
  "data": {
    "id": "abc123",
    "name": "Этаж 1 — корпус A",
    "description": "...",
    "unit": "m",
    "outer_loop": [
      {
        "type": "line",
        "start": { "x": 0.0, "y": 0.0 },
        "end": { "x": 30.0, "y": 0.0 }
      },
      {
        "type": "arc",
        "start": { "x": 30.0, "y": 0.0 },
        "end": { "x": 30.0, "y": 20.0 },
        "center": { "x": 30.0, "y": 10.0 },
        "radius": 10.0,
        "clockwise": false
      }
    ],
    "inner_loops": [
      [
        {
          "type": "line",
          "start": { "x": 10.0, "y": 8.0 },
          "end": { "x": 14.0, "y": 8.0 }
        }
      ]
    ],
    "metadata": {
      "floor": "1",
      "building": "A"
    }
  }
}
```

**Типы сегментов:**

| type | Описание | Дополнительные поля |
|------|----------|---------------------|
| `line` | Отрезок | `start`, `end` |
| `arc` | Дуга | `start`, `end`, `center`, `radius`, `clockwise` |
| `spline` | Сплайн | `start`, `end`, `control_points[]` |
| `ellipse` | Эллиптический сегмент | `start`, `end` |
| `nurbs_spline` | NURBS-сплайн | `start`, `end`, `control_points[]` |

**Единицы (`unit`):** `mm`, `cm`, `m`, `ft`, `in`

---

### 4. POST /generate

Запуск генерации планировочных решений.

**Тело запроса:**

```json
{
  "request_id": "gen_req_20260519_001",
  "contour_id": "abc123",
  "variant_count": 3,
  "generation_type": "Residential",
  "validation_mode": "Advisory",
  "text_prompt": "Сделать компактные МОП и сохранить хорошую инсоляцию квартир.",
  "llm_prompt": "Сгенерируй варианты планировочного решения для Revit...",
  "apartment_types": {
    "Studio": 2,
    "OneRoom": 4,
    "TwoRoom": 6,
    "ThreeRoom": 2
  },
  "min_apartment_area": 25.0,
  "max_apartment_area": 120.0,
  "mop_area_target": 80.0,
  "min_corridor_width": 1.2,
  "optimization_priority": "efficiency",
  "room_types": ["LivingRoom", "Bedroom", "Kitchen", "Bathroom", "CommonArea", "Lobby", "Elevator"],
  "min_room_area": 8.0,
  "max_room_area": 80.0,
  "custom_parameters": {},
  "context": {
    "contour": {
      "id": "abc123",
      "name": "Этаж 1 — корпус A",
      "unit": "m",
      "outer_loop": [
        { "type": "line", "start": { "x": 0.0, "y": 0.0 }, "end": { "x": 30.0, "y": 0.0 } }
      ],
      "inner_loops": [],
      "metadata": {
        "source": "revit_selection"
      }
    },
    "revit_context": {
      "document_title": "Project.rvt",
      "active_view_name": "Level 1",
      "active_view_type": "FloorPlan",
      "level_id": "311",
      "level_name": "Level 1",
      "level_elevation_meters": 0.0,
      "contour_source": "revit_selection",
      "project_parameters": {
        "project_name": "Residential building",
        "contour_area_m2": "600.00"
      },
      "existing_elements": [
        {
          "element_id": "5021",
          "category": "Walls",
          "name": "Basic Wall",
          "element_type": "Generic - 200mm",
          "level_name": "Level 1",
          "parameters": {
            "Length": "12000"
          }
        }
      ]
    }
  }
}
```

**Поля генерации:**

| Поле | Описание |
|------|----------|
| `request_id` | Идентификатор запроса, сформированный плагином для корреляции логов и ответа. |
| `contour_id` | Идентификатор контура. Сохраняется для обратной совместимости. |
| `variant_count` | Количество вариантов, 1-20. |
| `generation_type` | Тип сценария: `Residential`, `Office`, `MixedUse`, `Custom`. |
| `validation_mode` | Режим проверки: `Off`, `Advisory`, `Strict`. |
| `text_prompt` | Пользовательский текстовый промпт. |
| `llm_prompt` | Полный prompt, собранный плагином из параметров и Revit-контекста. Пользовательский текст внутри него трактуется как данные, а не как инструкция менять контракт ответа. |
| `apartment_types` | Квартирография: тип квартиры -> количество. |
| `mop_area_target` | Целевая площадь МОП, м². Если отсутствует, сервис выбирает автоматически. |
| `min_corridor_width` | Минимальная ширина коридоров МОП, м. |
| `context.contour` | Полная геометрия контура в метрах. |
| `context.revit_context` | Уровень, активный вид, параметры проекта и существующие элементы модели. |

**Ответ:**

```json
{
  "success": true,
  "data": {
    "request_id": "gen_456",
    "status": "completed",
    "variants": [
      {
        "id": "var_001",
        "name": "Вариант 1",
        "variant_index": 1,
        "rooms": [
          {
            "id": "room_001",
            "name": "Гостиная",
            "type": "LivingRoom",
            "area": 25.3,
            "boundary": [
              { "type": "line", "start": {"x":0,"y":0}, "end": {"x":6,"y":0} },
              { "type": "line", "start": {"x":6,"y":0}, "end": {"x":6,"y":4.2} },
              { "type": "line", "start": {"x":6,"y":4.2}, "end": {"x":0,"y":4.2} },
              { "type": "line", "start": {"x":0,"y":4.2}, "end": {"x":0,"y":0} }
            ],
            "label_point": { "x": 3.0, "y": 2.1 },
            "properties": {}
          }
        ],
        "partitions": [
          { "type": "line", "start": {"x":6,"y":0}, "end": {"x":6,"y":20} }
        ],
        "total_area": 600.0,
        "usable_area": 520.0,
        "mop_area": 72.0,
        "room_count": 8,
        "apartment_count": 6,
        "apartment_type_distribution": {
          "OneRoom": 2,
          "TwoRoom": 4
        },
        "corridor_area": 80.0,
        "efficiency_score": 87.0,
        "custom_metrics": {},
        "metadata": {}
      }
    ]
  }
}
```

Ответ `/generate` должен содержать ровно столько элементов `data.variants`, сколько было передано в `variant_count`.
Плагин до маппинга в доменную модель проверяет обязательные поля `request_id`, `status`, `variants`, `variant.id`,
`variant.name`, положительные площади, список помещений, `room.id`, `room.name`, `room.type`, `room.area`,
`room.boundary`, `room.label_point` и корректность числовых координат. Ответ с текстом вместо JSON, пустым телом,
невалидным JSON или нарушением этого контракта не применяется к Revit-модели.

**Типы помещений (`room_types`):**

`LivingRoom`, `Bedroom`, `Kitchen`, `Bathroom`, `Corridor`, `Storage`, `Office`, `MeetingRoom`, `OpenSpace`, `Lobby`, `Technical`, `Staircase`, `Elevator`, `Balcony`, `CommonArea`, `Other`

`CommonArea`, `Lobby`, `Elevator`, `Staircase` и общие коридоры трактуются как МОП (места общего пользования).

**Приоритеты оптимизации:**

| Значение | Описание |
|----------|----------|
| `efficiency` | Максимизация коэффициента полезной площади |
| `area` | Максимизация общей полезной площади |
| `rooms` | Максимизация количества помещений |

---

## Коды ошибок

| HTTP | code | Описание |
|------|------|----------|
| 401/403 | `AUTH_ERROR` | Неверные учётные данные |
| 404 | `NOT_FOUND` | Контур или ресурс не найден |
| 404 | `CONTOUR_NOT_FOUND` | Контур по ID не найден |
| 422 | `INVALID_CONTOUR` | Контур не прошёл валидацию |
| 429 | `RATE_LIMIT` | Превышен лимит запросов |
| 500 | `INTERNAL_ERROR` | Внутренняя ошибка сервера |
| 500 | `GENERATION_ERROR` | Ошибка процесса генерации |
| — | `EMPTY_RESPONSE` | API вернул пустое тело ответа |
| — | `INVALID_JSON` | API вернул невалидный JSON |
| — | `INVALID_API_CONTRACT` | JSON не соответствует обязательной DTO-схеме плагина |

---

## Примечания

- Координаты всех точек — в плоскости XY (2D).
- Контур `outer_loop` должен быть замкнутым: `end` последнего сегмента совпадает со `start` первого.
- Все `boundary` помещений также замкнуты.
- `label_point` — точка для размещения подписи (должна находиться внутри контура помещения).
- `efficiency_score` — нормализованный балл 0–100.
- Координаты и площади передаются в метрах.
- Результат LLM применяется только после строгого парсинга, валидации геометрии, UI-предпросмотра и подтверждения пользователя.
