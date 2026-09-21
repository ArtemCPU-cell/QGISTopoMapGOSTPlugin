using System.Globalization;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;
using OsmToShapefile.Buildings;
using OsmToShapefile.Cli;
using OsmToShapefile.Dem;
using OsmToShapefile.Geo.Classifier;
using OsmToShapefile.Geo.Generalizer;
using OsmToShapefile.Geo.Hydrology;
using OsmToShapefile.Geo.Vegetation;
using OsmToShapefile.Output;
using OsmToShapefile.Overpass;
using OsmToShapefile.Reproject;
using OsmToShapefile.Scale;
using OsmToShapefile.Style;
using OsmToShapefile.Tiling;

RunOptions opts;
try
{
    opts = ArgsParser.Parse(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

Console.WriteLine(
    $"OsmToShapeFile: bbox={opts.BBox}, scale={opts.Scale}, srs={opts.TargetSrs}, output={opts.OutputDir}, offline={opts.Offline}");

var profile = ScaleProfile.For(opts.Scale);
var reprojector = new Reprojector(opts.TargetSrs);
Console.WriteLine($"Целевая SRS: {opts.TargetSrs}, WKT длина {reprojector.TargetWkt.Length} симв.");

var roadsLayer = new LayerDefinition("roads", "highway", null, GeometryKind.Line, ExtraValues: null, IncludeRelations: false);
var buildingsLayer = new LayerDefinition(
    "buildings", "building", null, GeometryKind.Polygon, IncludeRelations: true);
var waterLayer = new LayerDefinition("water", "natural", null!, GeometryKind.Polygon, ExtraValues: new[] { "water", "river", "stream", "canal" });
var vegetationLayer = new LayerDefinition("vegetation", "landuse", null!, GeometryKind.Polygon,
    ExtraValues: new[] { "forest", "wood", "scrub", "grassland", "meadow", "farmland", "sand", "beach", "rock", "cliff", "bare_rock", "vineyard", "orchard", "cemetery" },
    IncludeRelations: false);
var placesLayer = new LayerDefinition("places", "place", null, GeometryKind.Point,
    ExtraValues: new[] { "city", "town", "village", "hamlet", "suburb", "borough" });

var sampleDataDir = Path.Combine(AppContext.BaseDirectory, "sample-data");
if (opts.Offline)
    Console.WriteLine($"Офлайн-кэш: {sampleDataDir}");
Directory.CreateDirectory(opts.OutputDir);

var client = new OverpassClient();

{
    var features = await ProcessLayerAsync(roadsLayer, "roads", profile, opts, sampleDataDir, client,
        (response, p) =>
        {
            var out_ = new List<Feature>();
            foreach (var el in response.Elements)
            {
                if (el.Type != "way" || el.Geometry is null || el.Geometry.Count < 2) continue;
                var coords = el.Geometry.Select(g => new Coordinate(g.Lon, g.Lat)).ToArray();
                var tags = el.Tags ?? new Dictionary<string, string>();
                var hw = tags.TryGetValue("highway", out var hv) ? hv : null;
                var cls = HighwayClassifier.Classify(hw);
                if (cls == HighwayClass.Unknown) continue;
                var attrs = new AttributesTable
                {
                    { "osm_id", el.Id },
                    { "name", tags.TryGetValue("name", out var n) ? n : string.Empty },
                    { "class", cls.ToString().ToLowerInvariant() },
                    { "highway", hw ?? string.Empty },
                };
                out_.Add(new Feature(GeometryFactory.Default.CreateLineString(coords), attrs));
            }
            return out_;
        });
    WriteFeaturesWithStyle(opts.OutputDir, "roads", features,
        SldStyleFactory.Roads(profile));
}

{
    var features = await ProcessLayerAsync(buildingsLayer, "buildings", profile, opts, sampleDataDir, client,
        (response, p) => BuildingBuilder.Build(response));
    WriteFeaturesWithStyle(opts.OutputDir, "buildings", features,
        SldStyleFactory.Buildings());
}

{
    var features = await ProcessLayerAsync(waterLayer, "water", profile, opts, sampleDataDir, client,
        (response, p) =>
        {
            var hyd = HydrologyBuilder.Build(response, p);
            var all = new List<Feature>();
            all.AddRange(hyd.Polygons);
            all.AddRange(hyd.Lines);
            all.AddRange(hyd.Wetlands);
            return all;
        });
    WriteFeaturesWithStyle(opts.OutputDir, "water", features,
        SldStyleFactory.WaterPolygons(profile));
}

{
    var features = await ProcessLayerAsync(vegetationLayer, "vegetation", profile, opts, sampleDataDir, client,
        (response, p) =>
        {
            var byClass = VegetationBuilder.Build(response);
            return byClass.SelectMany(kv => kv.Value).ToList();
        });
    WriteFeaturesWithStyle(opts.OutputDir, "vegetation", features,
        SldStyleFactory.Forest());
}

{
    var features = await ProcessLayerAsync(placesLayer, "places", profile, opts, sampleDataDir, client,
        (response, p) => SettlementBuilder.Build(response));
    WriteFeaturesWithStyle(opts.OutputDir, "settlements", features,
        SldStyleFactory.Settlements());
}

if (!opts.NoDem)
{
    Console.WriteLine("DEM: запрашиваю SRTM 30м через opentopodata.org...");
    try
    {
        var src = new SrtmSource();
        // Шаг сетки ~ 0.0003° ≈ 33 м (примерный шаг SRTM).
        var stepDeg = 0.0003;
        var progress = new Progress<string>(m => Console.WriteLine($"  {m}"));
        var grid = await src.FetchGridAsync(opts.BBox, stepDeg, progress);
        var contours = ContourGenerator.Build(grid, profile.ContourIntervalMeters);
        WriteFeaturesWithStyle(opts.OutputDir, "contours", contours.Lines,
            SldStyleFactory.Contours(index: false));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"DEM: ошибка — {ex.Message}");
    }
}
else
{
    Console.WriteLine("DEM пропущен (--no-dem).");
}


if (opts.QgisProject)
{
    var name = Path.GetFileName(opts.OutputDir.TrimEnd('/', '\\'));

    var sw = reprojector.Transform(new Coordinate(opts.BBox.West, opts.BBox.South));
    var ne = reprojector.Transform(new Coordinate(opts.BBox.East, opts.BBox.North));
    var xmin = Math.Min(sw.X, ne.X);
    var ymin = Math.Min(sw.Y, ne.Y);
    var xmax = Math.Max(sw.X, ne.X);
    var ymax = Math.Max(sw.Y, ne.Y);

    QgisProjectWriter.Write(opts.OutputDir, name, $"EPSG:{reprojector.TargetSrid}",
        reprojector.TargetWkt,
        (Xmin: xmin, Ymin: ymin, Xmax: xmax, Ymax: ymax));
}

Console.WriteLine("Готово.");
return 0;


async Task<List<Feature>> ProcessLayerAsync(
    LayerDefinition layer, string key,
    ScaleProfile profile, RunOptions opts, string sampleDataDir, OverpassClient client,
    Func<OverpassResponse, ScaleProfile, List<Feature>> build)
{
    var all = new List<Feature>();
    var localFile = Path.Combine(sampleDataDir, $"{key}.json");
    var tiles = BboxTiler.Tile(opts.BBox);

    foreach (var tile in tiles)
    {
        try
        {
            OverpassResponse response;
            if (opts.Offline)
            {
                if (!File.Exists(localFile))
                {
                    throw new FileNotFoundException(
                        $"Офлайн-режим включён, но кэш слоя '{key}' не найден. " +
                        $"Ожидался файл: {localFile}");
                }

                if (all.Count == 0)
                    Console.WriteLine($"[{key}] офлайн: {localFile}");
                response = await OverpassClient.LoadFromFileAsync(localFile);
            }
            else
            {
                Console.WriteLine($"[{key}] Overpass {tile}...");
                var layerTile = layer with { };
                _ = layerTile;
                response = await client.FetchLayerAsync(tile, layer);
            }
            var built = build(response, profile);
            Console.WriteLine($"[{key}] тайл {tile}: +{built.Count} объектов");
            all.AddRange(built);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{key}] тайл {tile}: {ex.Message}");
        }
        if (tiles.Count > 1)
            await Task.Delay(TimeSpan.FromSeconds(30));
    }
    return all;
}

void WriteFeaturesWithStyle(string outputDir, string layerName, List<Feature> features,
    SldStyle style)
{
    foreach (var f in features)
        reprojector.TransformGeometry(f.Geometry);
    foreach (var f in features)
    {
        f.Geometry = f.Geometry is Polygon or MultiPolygon
            ? TopologyPreservingSimplifier.Simplify(f.Geometry, profile.DouglasPeuckerToleranceMeters)
            : OsmToShapefile.Geo.Generalizer.DouglasPeuckerSimplifier.Simplify(f.Geometry, profile.DouglasPeuckerToleranceMeters);
    }
    var before = features.Count;
    features = features.Where(f => f.Geometry is { IsEmpty: false } and { IsValid: true }).ToList();
    if (features.Count < before)
    {
        Console.WriteLine($"[{layerName}] отфильтровано {before - features.Count} объектов " +
                           "с пустой/невалидной геометрией после упрощения");
    }

    ShapefileWriter.WriteLayer(outputDir, layerName, features, reprojector.TargetWkt);
    SldWriter.Write(outputDir, layerName, style);
}
