using System.Text.Json.Serialization;

namespace OsmToShapefile.Overpass;

public sealed class OverpassResponse
{
    [JsonPropertyName("elements")]
    public List<OverpassElement> Elements { get; set; } = new();

    [JsonPropertyName("remark")]
    public string? Remark { get; set; }
}
public sealed class OverpassElement
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty; // "node" | "way" | "relation"

    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("lat")]
    public double? Lat { get; set; }

    [JsonPropertyName("lon")]
    public double? Lon { get; set; }

    [JsonPropertyName("geometry")]
    public List<GeoPoint>? Geometry { get; set; }

    [JsonPropertyName("members")]
    public List<OverpassMember>? Members { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class OverpassMember
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("ref")]
    public long Ref { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("geometry")]
    public List<GeoPoint>? Geometry { get; set; }
}

public sealed class GeoPoint
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }
}
