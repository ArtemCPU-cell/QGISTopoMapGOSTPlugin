# Roadmap: QGIS Topographic Map Plugin

Дата фиксации: **22 сентября 2026 года**

## Конечный результат

Самодостаточный QGIS Plugin для Windows, который:

1. принимает bbox/область, масштаб, CRS и параметры карты;
2. запускает bundled self-contained .NET engine как дочерний процесс;
3. получает от engine результат через versioned JSON contract;
4. создаёт готовый проект QGIS с картографическими слоями, стилями,
   легендой и отчётом качества;
5. работает без установленного .NET Runtime/SDK и без localhost/backend service.

Целевой результат называется **ГОСТ-ориентированной картой** до прохождения
отдельной экспертной/производственной приёмки. Нельзя заявлять официальное
соответствие ГОСТ только на основании похожей раскраски слоёв.

## Что уже сделано

- C#/.NET 8 CLI получает OSM через Overpass и создаёт shapefile/QGIS project.
- Есть offline Overpass JSON cache в `sample-data/`.
- Offline OSM-режим строгий: при `--offline` fallback в Overpass запрещён.
- Исправлена запись DBF: QGIS видит атрибутивные записи.
- Здания поддерживают `way` и multipolygon `relation`.
- Контуры DEM сшиваются; открытые линии на границе bbox не замыкаются искусственно.
- Есть версионируемый `request.json` v1 и `response.json` с результатами.
- Есть отчёт источников и ограничений качества.
- Есть архитектурная документация в `docs/architecture.md`.

## Правило работы по плану

Новый функциональный этап начинается только после того, как предыдущий имеет:

- реализованный код;
- автоматическую или воспроизводимую проверку;
- обновлённую документацию;
- критерии приёмки, выполненные в локальном запуске.

Нельзя переносить бизнес-логику в Python «временно»: Python остаётся только
QGIS adapter.

---

# Этап 0. Зафиксировать контракт продукта

**Статус: частично выполнен.**

## Сделать

- Зафиксировать основной сценарий: `generate-topographic-map`.
- Зафиксировать request/response contract v1.
- Добавить явные режимы качества:
  - `preview` — OSM + fallback DEM;
  - `production-candidate` — проверенные входные данные.
- Добавить идентификатор версии engine в response.
- Добавить schema validation для request и response.
- Документировать обратную совместимость contract v1.

## Критерий готовности

Один и тот же `request.json` даёт предсказуемый `response.json`, а ошибка
контракта не приводит к сетевому запросу и возвращает понятный код ошибки.

---

# Этап 1. Разделить .NET solution на слои

**Статус: не начат; сейчас один проект.**

Создать проекты:

```text
TopoMap.Contracts
TopoMap.Domain
TopoMap.Application
TopoMap.Infrastructure
TopoMap.Engine
TopoMap.Tests
```

## Ответственность

### `TopoMap.Contracts`

DTO request/response, enum версий, сериализация, ошибки IPC.

### `TopoMap.Domain`

Независимые от файлов и QGIS сущности:

- MapSpecification;
- CartographicLayer;
- FeatureClass;
- GeometryRole;
- ScaleRules;
- DataQualityStatus;
- ElevationMetadata.

### `TopoMap.Application`

Use case `GenerateTopographicMap`:

```text
validate request
  -> load sources
  -> classify features
  -> generalize
  -> generate relief
  -> validate output
  -> write artifacts
  -> produce report
```

### `TopoMap.Infrastructure`

Overpass, offline cache, DEM/GeoTIFF, GeoPackage, shapefile, SLD/QML,
filesystem and logging.

### `TopoMap.Engine`

Только process entry point, IPC и exit codes. Никаких картографических правил
в `Program.cs`.

## Критерий готовности

`TopoMap.Domain` и `TopoMap.Application` собираются и тестируются без QGIS,
Python и запуска Overpass.

---

# Этап 2. Нормализовать спецификацию карты и масштаб

**Статус: частично выполнен.**

## Сделать

Вместо hardcoded `LayerDefinition` в `Program.cs` создать профиль масштаба:

```text
MapProfile 1:25 000
  contour interval: 5 m
  index contour: every 5th
  building minimum area
  road selection rules
  label priorities
  line widths and symbol sizes
  generalization tolerances
```

Профили начать с `1:25 000`, затем добавить `1:10 000`, `1:50 000`,
`1:100 000`, `1:200 000`.

## Критерий готовности

Изменение масштаба меняет правила отбора/генерализации, а не только название
в консоли.

---

# Этап 3. Сделать источники данных заменяемыми

**Статус: частично выполнен.**

Ввести интерфейсы:

```text
IOsmSource
IElevationSource
IVectorSource
IMapArtifactWriter
```

## OSM

Реализации:

- `OverpassSource` — live preview;
- `JsonCacheOsmSource` — deterministic offline tests;
- позже `PbfOsmSource` — локальный planet/region extract без Overpass.

## DEM

Реализации:

- `PreviewSrtmSource` — текущий OpenTopoData/SRTM fallback;
- `GeoTiffDtmSource` — основной production-candidate источник;
- позже tile cache для Copernicus/GEDTM30 как fallback.

## Обязательные DEM metadata

- CRS горизонтали;
- vertical datum;
- resolution;
- horizontal/vertical accuracy;
- acquisition date;
- no-data policy;
- source and licence;
- bbox coverage.

Если metadata недостаточно, engine не должен маркировать карту как
`production-candidate`.

## Критерий готовности

Один и тот же pipeline работает с offline OSM cache и с локальным DTM-файлом,
а источник можно заменить без изменения генераторов слоёв.

---

# Этап 4. Подключить качественный DEM

**Статус: не начат.**

## Правило источников

- SRTM/OpenTopoData и глобальные 30 m DEM — только `preview`.
- Для production-candidate использовать supplied DTM: официальная ЦМР,
  LiDAR/фотограмметрия или другой источник с документированными точностью и
  вертикальной системой.
- Не использовать DSM с зданиями/растительностью как terrain DTM для городских
  горизонталей без корректного удаления объектов.

## Реализация

1. Поддержать GeoTIFF DTM с CRS.
2. Проверять bbox coverage.
3. Проверять no-data.
4. Привести высоты к выбранной системе.
5. Генерировать горизонтали с clipping по bbox.
6. Сохранять metadata в `response.json` и паспорте карты.
7. Добавить тестовый небольшой DTM в `test-data/`.

## Критерий готовности

На одном и том же DTM результат воспроизводим без сети. При неправильном CRS,
неполном покрытии или отсутствии вертикальных metadata engine завершается с
ошибкой в production-candidate режиме.

---

# Этап 5. Картографическая модель и классификация

**Статус: частично выполнен для roads/buildings/water/vegetation/places.**

Создать внутренние классы, не зависящие от OSM-тегов:

```text
TransportRoad
Railway
Building
Structure
WaterBody
Watercourse
Vegetation
Settlement
Boundary
ReliefFeature
PlaceName
```

## Сделать

- расширить классификацию дорог: значение, покрытие, доступность;
- добавить railway, bridges, tunnels, embankments;
- разделить здания и сооружения;
- добавить place names и приоритеты подписей;
- добавить administrative/boundary слой только при наличии надёжного источника;
- сохранять original source tags только как provenance, не как картографическое
  правило.

## Критерий готовности

Каждый итоговый объект имеет внутренний `featureClass`, source id и понятное
правило, по которому он попал на карту.

---

# Этап 6. Генерализация и ГОСТ-ориентированная визуализация

**Статус: начат, но требует систематизации.**

## Сделать

- вынести правила генерализации в `MapProfile`;
- применять разные tolerances к линиям, полигонам и подписям;
- убрать дубли и перекрытия;
- проверить topology после simplify;
- реализовать приоритеты подписей и collision avoidance;
- заменить ad-hoc SLD на versioned symbol library;
- добавить рамку, легенду, north arrow, scale bar, grid и metadata block;
- предусмотреть QGIS QML/SLD export.

## Критерий готовности

Для тестового bbox карта читается на масштабе 1:25 000, подписи не массово
перекрываются, линии не превращаются в шум, а каждый стиль связан с
внутренним feature class.

---

# Этап 7. Выходные форматы

**Статус: shapefile работает; GeoPackage не начат.**

## Приоритет

1. GeoPackage — основной выходной формат.
2. QGIS project `.qgz`/`.qgs` — готовый проект с подключёнными слоями.
3. Shapefile — совместимый экспорт, не основной внутренний формат.
4. SLD/QML — стили.
5. `map-result.json` — machine-readable result для Python adapter.

## Сделать

- добавить GeoPackage writer;
- сохранять CRS, field types, layer metadata;
- избежать ограничений DBF и имён полей shapefile;
- оставить текущий shapefile writer для legacy export;
- QGIS project должен предпочитать GeoPackage.

## Критерий готовности

Один запуск создаёт валидный GeoPackage, который открывается в QGIS без ручного
исправления источников и показывает непустые атрибуты.

---

# Этап 8. QA и воспроизводимость

**Статус: частично выполнен вручную.**

## Automated tests

- request contract validation;
- bbox and CRS validation;
- Overpass JSON parsing;
- relation/multipolygon building assembly;
- contour generation on synthetic DEM;
- contour stitching and bbox clipping;
- DBF/SHP header validation;
- GeoPackage schema validation;
- deterministic offline end-to-end run;
- response layer counts and warnings.

## Quality checks

- invalid geometries;
- empty geometries;
- dangling line segments;
- duplicate features;
- contours with impossible elevations;
- features outside bbox;
- missing mandatory metadata;
- source date and licence presence.

## Критерий готовности

CI/локальная команда проверяет сборку, тесты и offline fixture без сети.
Результат offline run имеет стабильные counts и hash/manifest output artifacts.

---

# Этап 9. QGIS Python adapter

**Статус: не начат.**

## Сделать

- создать стандартную структуру QGIS plugin;
- диалог bbox/extent, scale, CRS, DEM source и output mode;
- получить extent из текущего QGIS canvas/layer;
- сформировать `request.json`;
- запустить bundled engine через `subprocess`;
- скрыть консольное окно на Windows;
- читать stdout/stderr и `response.json`;
- показывать progress/error dialog;
- загрузить GeoPackage/project в QGIS;
- не дублировать C#-логику в Python.

## Критерий готовности

Пользователь устанавливает plugin, нажимает одну кнопку, получает проект и
слои в QGIS; ручной запуск EXE, PATH и .NET Runtime не нужны.

---

# Этап 10. Self-contained packaging и release

**Статус: не начат.**

## Сделать

- publish `win-x64` self-contained;
- определить plugin layout и build script;
- включить `sample-data` только для development/test, не обязательно в release;
- включить license/provenance/source manifest;
- проверить запуск на чистой Windows-машине без .NET SDK/runtime;
- проверить QGIS LTR;
- собрать zip plugin;
- добавить versioning plugin и engine;
- добавить rollback-compatible contract handling.

## Команда публикации

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

## Критерий готовности

Чистая машина с QGIS LTR устанавливает zip, запускает plugin и создаёт карту
без дополнительных установок и без сетевого backend service.

---

# Definition of Done для версии 1.0

Версия 1.0 готова, если выполнены все условия:

- [ ] QGIS plugin устанавливается из zip.
- [ ] .NET engine bundled и self-contained.
- [ ] Python не содержит картографической бизнес-логики.
- [ ] request/response contract versioned.
- [ ] offline end-to-end run работает без Overpass.
- [ ] поддерживается production-candidate supplied DTM.
- [ ] создаётся GeoPackage и QGIS project.
- [ ] есть дороги, здания, вода, растительность, населённые пункты,
      железные дороги/мосты по наличию источника, рельеф и подписи.
- [ ] есть source/quality report.
- [ ] есть automated tests для геометрии, контуров, контрактов и writers.
- [ ] проект не заявляет нормативную сертификацию без отдельной приёмки.

## Порядок ближайших задач

1. Разделить текущий проект на `Contracts/Domain/Application/Infrastructure/Engine`.
2. Добавить тестовый проект и synthetic DEM tests.
3. Реализовать GeoTIFF DTM source и metadata validation.
4. Вынести scale/profile/classification из `Program.cs`.
5. Добавить GeoPackage writer.
6. Расширить слои и генерализацию.
7. Написать Python QGIS adapter.
8. Собрать self-contained plugin и проверить на чистой машине.
