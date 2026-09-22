# Target architecture

## Product

`OsmToShapefile` is evolving into the C# engine for a QGIS plugin that generates a
GOST-oriented topographic map. It is not a claim that arbitrary OSM data already
constitutes a certified topographic map.

The product has two quality modes:

- **Preview**: OSM + global/fallback elevation data. Useful for exploration and
  development; the result carries explicit limitations.
- **Production candidate**: user/organisation-supplied authoritative vector data
  and a documented terrain DTM. The engine validates the metadata and emits a
  quality report before the result can be treated as a production input.

## Process boundary

```text
QGIS Python plugin
  -> request.json
  -> bundled TopoMap.Engine.exe (self-contained .NET, child process)
  -> GeoPackage / Shapefile / QGIS project + response.json
  -> QGIS Python plugin loads the result
```

There is no localhost server and no long-running backend process. The engine is
independent of QGIS and Python.

## Current implementation slice

The existing console entry point now supports the versioned contract:

```powershell
dotnet run -- --request request.json --response response.json
```

The request contract is documented in `docs/contracts/generate-topographic-map.v1.json`.
The response contains:

- `success` and contract `version`;
- generated layers and feature counts;
- selected map specification;
- data-source provenance;
- warnings and limitations.

The legacy command-line form remains supported for development.

## Planned .NET solution split

The current repository is still one executable project. The next refactoring
should split it without moving business logic into Python:

- `TopoMap.Contracts` — request/response DTOs and versioning;
- `TopoMap.Domain` — map specification, feature classes, quality rules;
- `TopoMap.Application` — generation pipeline and use cases;
- `TopoMap.Infrastructure` — OSM, DEM, raster/vector readers and writers;
- `TopoMap.Engine` — process entry point and IPC;
- `TopoMap.Tests` — deterministic unit/integration tests.

## Planned plugin layout

```text
qgis-plugin/
  __init__.py
  metadata.txt
  plugin.py
  qgis_adapter/
    process_runner.py
    layer_loader.py
    dialogs.py
  bin/win-x64/TopoMap.Engine/
    TopoMap.Engine.exe
    *.dll
    .NET runtime files
```

The publish artifact is built with:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

The Python side should only manage QGIS UI/API, process execution, temporary
files and loading the result. It must not classify OSM tags or generate
cartographic geometry.
