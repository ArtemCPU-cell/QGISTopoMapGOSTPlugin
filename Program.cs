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

// --- Разбор CLI ---
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

Console.WriteLine($"OsmToShapeFile: bbox={opts.BBox}, scale={opts.Scale}, srs={opts.TargetSrs}, output={opts.OutputDir}");

var profile = ScaleProfile.For(opts.Scale);
var reprojector = new Reprojector(opts.TargetSrs);
Console.WriteLine($"Целевая SRS: {opts.TargetSrs}, WKT длина {reprojector.TargetWkt.Length} симв.");

// --- Список слоёв Overpass ---
var roadsLayer = new LayerDefinition("roads", "highway", null, GeometryKind.Line, ExtraValues: null, IncludeRelations: false);
var buildingsLayer = new LayerDefinition("buildings", "building", null, GeometryKind.Polygon);
var waterLayer = new LayerDefinition("water", "natural", null!, GeometryKind.Polygon, ExtraValues: new[] { "water", "river", "stream", "canal" });
var vegetationLayer = new LayerDefinition("vegetation", "landuse", null!, GeometryKind.Polygon,
    ExtraValues: new[] { "forest", "wood", "scrub", "grassland", "meadow", "farmland", "sand", "beach", "rock", "cliff", "bare_rock", "vineyard", "orchard", "cemetery" },
    IncludeRelations: false);
var placesLayer = new LayerDefinition("places", "place", null, GeometryKind.Point,
    ExtraValues: new[] { "city", "town", "village", "hamlet", "suburb", "borough" });

var sampleDataDir = Path.Combine(Directory.GetCurrentDirectory(), "sample-data");
Directory.CreateDirectory(opts.OutputDir);

var client = new OverpassClient();

// === ДОРОГИ ===
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

// === ЗДАНИЯ ===
{
    var features = await ProcessLayerAsync(buildingsLayer, "buildings", profile, opts, sampleDataDir, client,
        (response, p) => BuildingBuilder.Build(response));
    WriteFeaturesWithStyle(opts.OutputDir, "buildings", features,
        SldStyleFactory.Buildings());
}

// === ГИДРОГРАФИЯ ===
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

// === РАСТИТЕЛЬНОСТЬ/ГРУНТЫ ===
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

// === НАСЕЛЁННЫЕ ПУНКТЫ ===
{
    var features = await ProcessLayerAsync(placesLayer, "places", profile, opts, sampleDataDir, client,
        (response, p) => SettlementBuilder.Build(response));
    WriteFeaturesWithStyle(opts.OutputDir, "settlements", features,
        SldStyleFactory.Settlements());
}

// === DEM + ГОРИЗОНТАЛИ ===
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

// === QGIS-проект (опционально) ===
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

// === Локальные функции ===

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
            if (opts.Offline && File.Exists(localFile))
            {
                if (all.Count == 0)
                    Console.WriteLine($"[{key}] офлайн: {localFile}");
                response = await OverpassClient.LoadFromFileAsync(localFile);
            }
            else
            {
                Console.WriteLine($"[{key}] Overpass {tile}...");
                var layerTile = layer with { }; // копия для журнала, если нужно
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

        // 30 секунд между Overpass-запросами — на 2-м тяжёлом запросе main-зеркало
        // начинает возвращать 429 Too Many Requests, и этого хватает для остывания.
        if (tiles.Count > 1)
            await Task.Delay(TimeSpan.FromSeconds(30));
    }
    return all;
}

void WriteFeaturesWithStyle(string outputDir, string layerName, List<Feature> features,
    SldStyle style)
{
    // Репроекция WGS84 → целевая метрическая SRS.
    foreach (var f in features)
        reprojector.TransformGeometry(f.Geometry);

    // Генерализация на уровне масштаба.
    //
    // ВАЖНО: для полигонов используем TopologyPreservingSimplifier, а не обычный
    // DouglasPeuckerSimplifier. Обычный DP для маленьких полигонов (типичное здание —
    // прямоугольник из 4-5 точек) при достаточно грубом допуске может схлопнуть контур
    // до пустой/невалидной геометрии (< 4 точек в кольце), молча вернув IsEmpty==true
    // вместо ошибки. Такая пустая геометрия среди нормальных полигонов ломает запись
    // в shapefile — ShapefileDataWriter пишет её как некорректный Null Shape (shape_type=0)
    // с неверной длиной записи, из-за чего съезжают смещения всех последующих записей
    // и оставшаяся часть файла превращается в мусор (проверено на реальном примере:
    // buildings.shp обрывался на записи #1204 из тысяч именно так).
    // TopologyPreservingSimplifier гарантирует, что результат остаётся валидным
    // полигоном и не схлопывается ниже минимума точек. Для линий (дороги, горизонтали)
    // риска нет — там достаточно 2 точек, оставляем обычный Douglas-Peucker.
    foreach (var f in features)
    {
        f.Geometry = f.Geometry is Polygon or MultiPolygon
            ? TopologyPreservingSimplifier.Simplify(f.Geometry, profile.DouglasPeuckerToleranceMeters)
            : OsmToShapefile.Geo.Generalizer.DouglasPeuckerSimplifier.Simplify(f.Geometry, profile.DouglasPeuckerToleranceMeters);
    }

    // Страховка: даже с TopologyPreservingSimplifier где-то ниже по пайплайну
    // (например, при клиппинге по границе тайла в BboxTiler) может появиться
    // вырожденная геометрия. Одна такая запись повреждает весь остальной файл —
    // поэтому отфильтровываем пустые/невалидные геометрии перед записью, а не
    // передаём их в ShapefileWriter как есть.
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