# Offline Overpass sample data

These are raw JSON responses from Overpass for bbox `55.75,37.60,55.77,37.64` (central Moscow), downloaded on **21 September 2026**. They are used by `dotnet run -- --offline ...` so geometry/shapefile work can be tested without a public Overpass server.

| File | Query result |
| --- | ---: |
| `roads.json` | 10,432 ways |
| `buildings.json` | 1,905 ways + 1,051 multipolygon relations |
| `water.json` | 12 ways |
| `vegetation.json` | 0 ways |
| `places.json` | 2 nodes |

`vegetation.json` is deliberately an actual empty Overpass response for this bbox and the current `landuse` value set, not a placeholder.

The queries below are generated from `LayerDefinition` in `Program.cs`. A point layer is queried as `node`; line and polygon layers are queried as `way` (building layers also include `relation`).

## roads.json

```overpass
[out:json][timeout:90];
(
  way["highway"](55.75,37.60,55.77,37.64);
);
out geom;
```

## buildings.json

```overpass
[out:json][timeout:90];
(
  way["building"](55.75,37.60,55.77,37.64);
  relation["building"](55.75,37.60,55.77,37.64);
);
out geom;
```

## water.json

```overpass
[out:json][timeout:90];
(
  way["natural"~"^(water|river|stream|canal)$"](55.75,37.60,55.77,37.64);
);
out geom;
```

## vegetation.json

```overpass
[out:json][timeout:90];
(
  way["landuse"~"^(forest|wood|scrub|grassland|meadow|farmland|sand|beach|rock|cliff|bare_rock|vineyard|orchard|cemetery)$"](55.75,37.60,55.77,37.64);
);
out geom;
```

## places.json

```overpass
[out:json][timeout:90];
(
  node["place"~"^(city|town|village|hamlet|suburb|borough)$"](55.75,37.60,55.77,37.64);
);
out geom;
```
