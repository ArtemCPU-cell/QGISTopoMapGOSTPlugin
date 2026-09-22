# Data quality and DEM policy

## Current state

The current SRTM path uses OpenTopoData as a convenient preview source. It is
not sufficient evidence for a production 1:25 000 topographic map: the source,
vertical datum, effective resolution, void handling and accuracy metadata are
not controlled by the generated map.

Global 30 m DEM products are fallback/preview sources. A global DSM can include
buildings and vegetation, which is unsuitable as a terrain model for urban
contours. The production input should therefore be a documented terrain DTM,
preferably derived from local survey, LiDAR or official photogrammetry, with:

- horizontal and vertical reference systems;
- acquisition date and source organisation;
- raster resolution and vertical accuracy;
- void/no-data policy;
- coverage of the requested bbox;
- licence and redistribution permissions.

## Engine policy

1. Preview sources are labelled as `Preview` in `map-result.json`.
2. The engine must not claim GOST compliance solely because an SLD resembles a
   conventional map style.
3. The `supplied DTM` mode accepts a local raster and rejects missing
   CRS, missing vertical metadata, insufficient coverage or unsupported raster
   types before generating production contours.
4. The user may explicitly disable relief with `--no-dem`; the result report then
   contains a warning.
5. Contours crossing the bbox boundary remain open. Closed geometry is not a
   requirement for every contour; artificially closing clipped contours would
   create false terrain.

## Current supplied DTM implementation

The engine accepts `--dem-file <path>` or `demFile` in request v1. It currently
accepts a deliberately narrow, safe subset: a single-band, uncompressed,
north-up GeoTIFF encoded as EPSG:4326 with ModelPixelScale, ModelTiepoint and
GeoKeyDirectory tags. It verifies that the raster covers the requested bbox.

This narrow subset is intentional for the first implementation: a projected,
compressed or tiled GeoTIFF is rejected rather than being decoded incorrectly.
Support for common production GeoTIFF variants belongs to the next
Infrastructure iteration. A supplied file changes the quality label to
`productionCandidate`, but the report still states that vertical datum and
survey accuracy must be supplied by the data owner.

## Layer roadmap

The first production profile should cover:

- mathematical framework and map metadata;
- terrain contours, index contours and spot heights;
- hydrography and bridges;
- transport network and railway infrastructure;
- settlements, buildings and important structures;
- vegetation and land cover;
- boundaries and geographic names;
- legend, source dates and quality report.

OSM remains one source for the preview profile. For production, each layer needs
an agreed authoritative source or an explicit omission in the quality report.
