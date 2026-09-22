# Architecture decisions

Дата: **22 сентября 2026 года**

## ADR-001: Python is an adapter, not the engine

Принято. Python отвечает за QGIS API, UI, subprocess, временные файлы и загрузку
результатов. Классификация, генерализация, DEM, геометрия и качество находятся в
C#.

## ADR-002: Child process IPC, no localhost

Принято. QGIS запускает bundled self-contained .NET executable. Внутренний HTTP
server и Windows service не используются. Основной IPC — request/response JSON и
GIS output files.

## ADR-003: Offline fixtures are mandatory

Принято. Каждый важный pipeline должен иметь deterministic offline fixture. Live
Overpass/DEM не должен быть необходим для unit/integration tests.

## ADR-004: GeoPackage is the primary output

Принято как целевое решение. Shapefile остаётся export compatibility mode.

## ADR-005: DEM quality is explicit

Принято. SRTM/OpenTopoData и глобальные 30 m DEM — preview only. Production
candidate требует supplied/documented DTM и metadata validation.

## ADR-006: GOST claim boundary

Принято. Engine реализует GOST-oriented rules and quality checks. Формальное
соответствие конкретному ТЗ/комплекту нормативных документов требует отдельной
проверки и приёмки.

## ADR-007: First production profile

Принято: сначала стабилизировать профиль 1:25 000 для bbox, затем расширять
масштабы. Все новые слои должны иметь классификацию, правило отбора, стиль,
тестовый fixture и запись в quality report.
