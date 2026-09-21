using OsmToShapefile.Overpass;

namespace OsmToShapefile.Tiling;

/// <summary>
/// Режет большой bbox на квадраты side×side, чтобы не упереться в лимит Overpass
/// (~0.25° по короткой стороне для тяжёлых bbox) и в тайм-аут ответа.
/// Параллельность обработки тайлов — на стороне вызывающего.
/// </summary>
public static class BboxTiler
{
    public const double MaxOverpassSideDegrees = 0.20; // чуть меньше лимита, чтобы был запас
    public const double TileOverlapDegrees = 0.005;

    /// <summary>Возвращает тайлы bbox, покрывающие исходный, в порядке с юго-запада на северо-восток.</summary>
    public static IReadOnlyList<BoundingBox> Tile(BoundingBox bbox)
    {
        var dLat = bbox.North - bbox.South;
        var dLon = bbox.East - bbox.West;

        if (dLat <= MaxOverpassSideDegrees && dLon <= MaxOverpassSideDegrees)
            return new[] { bbox };

        var stepLat = MaxOverpassSideDegrees;
        var stepLon = MaxOverpassSideDegrees;
        var tiles = new List<BoundingBox>();

        for (var south = bbox.South; south < bbox.North; south += stepLat)
        {
            for (var west = bbox.West; west < bbox.East; west += stepLon)
            {
                var north = Math.Min(south + stepLat, bbox.North);
                var east = Math.Min(west + stepLon, bbox.East);
                var sO = Math.Max(south - TileOverlapDegrees, bbox.South);
                var wO = Math.Max(west - TileOverlapDegrees, bbox.West);
                var nO = Math.Min(north + TileOverlapDegrees, bbox.North);
                var eO = Math.Min(east + TileOverlapDegrees, bbox.East);
                tiles.Add(new BoundingBox(sO, wO, nO, eO));
            }
        }

        return tiles;
    }
}