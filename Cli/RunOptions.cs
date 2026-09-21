namespace OsmToShapefile.Cli;

public sealed record RunOptions(
    OsmToShapefile.Overpass.BoundingBox BBox,
    OsmToShapefile.Scale.MapScale Scale,
    string OutputDir,
    string TargetSrs,
    bool Offline,
    bool NoDem,
    bool QgisProject)
{
    public static string Usage => """
        OsmToShapeFile — топокарта по OSM данным.

        Использование:
          dotnet run -- --bbox <S,W,N,E> --scale <масштаб> [опции]

        Обязательные аргументы:
          --bbox S,W,N,E       ограничивающий прямоугольник в WGS84 (градусы)
          --scale 10k|25k|50k|100k|200k
          --output <путь>      директория для шейпфайлов и SLD (по умолчанию ./output)

        Опции:
          --srs <epsg>         целевая метрическая SRS (по умолчанию EPSG:32637)
          --offline            не обращаться к Overpass, читать sample-data/*.json
          --no-dem             пропустить загрузку SRTM и построение горизонталей
          --qgis-project       сгенерировать .qgs со ссылками на слои

        Пример:
          dotnet run -- --bbox 55.75,37.60,55.77,37.64 --scale 25k --output ./out/moscow
        """;
}