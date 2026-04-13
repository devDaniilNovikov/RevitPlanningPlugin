# API-контракт для интеграции с RevitPlanningPlugin

## Общие сведения

| Параметр | Значение |
|----------|----------|
| Протокол | HTTPS |
| Формат | REST / JSON |
| Аутентификация | API Key (заголовок `X-API-Key`) или Bearer Token (`Authorization: Bearer <token>`) |
| Content-Type | `application/json` |

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

**Единицы (`unit`):** `mm`, `cm`, `m`, `ft`, `in`

---

### 4. POST /generate

Запуск генерации планировочных решений.

**Тело запроса:**

```json
{
  "contour_id": "abc123",
  "variant_count": 3,
  "room_types": ["LivingRoom", "Bedroom", "Kitchen", "Bathroom", "Corridor"],
  "min_room_area": 8.0,
  "max_room_area": 80.0,
  "min_corridor_width": 1.2,
  "optimization_priority": "efficiency",
  "custom_parameters": {}
}
```

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
        "variant_index": 0,
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
        "room_count": 8,
        "corridor_area": 80.0,
        "efficiency_score": 87.0,
        "custom_metrics": {},
        "metadata": {}
      }
    ]
  }
}
```

**Типы помещений (`room_types`):**

`LivingRoom`, `Bedroom`, `Kitchen`, `Bathroom`, `Corridor`, `Storage`, `Office`, `MeetingRoom`, `OpenSpace`, `Lobby`, `Technical`, `Staircase`, `Elevator`, `Balcony`, `Other`

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

---

## Примечания

- Координаты всех точек — в плоскости XY (2D).
- Контур `outer_loop` должен быть замкнутым: `end` последнего сегмента совпадает со `start` первого.
- Все `boundary` помещений также замкнуты.
- `label_point` — точка для размещения подписи (должна находиться внутри контура помещения).
- `efficiency_score` — нормализованный балл 0–100.
