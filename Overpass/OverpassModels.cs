using System.Text.Json.Serialization;

namespace OsmToShapefile.Overpass;

/// <summary>
/// Корневой объект ответа Overpass API (при запросе с "out geom;").
/// </summary>
public sealed class OverpassResponse
{
    [JsonPropertyName("elements")]
    public List<OverpassElement> Elements { get; set; } = new();

    // Overpass пишет сюда причину, если запрос не выполнился как ожидалось
    // (таймаут, превышен лимит), но при этом отвечает статусом 200 OK —
    // без проверки этого поля такие случаи выглядят как "просто нет данных".
    [JsonPropertyName("remark")]
    public string? Remark { get; set; }
}

/// <summary>
/// Один элемент OSM (node/way/relation) с геометрией, уже развёрнутой Overpass'ом
/// благодаря "out geom;" — не нужно отдельно резолвить node -> координаты.
/// Для relation мультиполигонов geometry уже развёрнут в rings Overpass'ом (с out geom).
/// </summary>
public sealed class OverpassElement
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "node" | "way" | "relation"

    [JsonPropertyName("id")]
    public long Id { get; set; }

    // Для type == "node"
    [JsonPropertyName("lat")]
    public double? Lat { get; set; }

    [JsonPropertyName("lon")]
    public double? Lon { get; set; }

    // Для type == "way" (список точек вдоль линии/полигона)
    [JsonPropertyName("geometry")]
    public List<GeoPoint>? Geometry { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class GeoPoint
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }
}
