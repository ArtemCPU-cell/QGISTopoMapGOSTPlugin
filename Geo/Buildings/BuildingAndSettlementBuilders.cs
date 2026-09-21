using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using OsmToShapefile.Overpass;

namespace OsmToShapefile.Buildings;

public static class BuildingBuilder
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    public static List<Feature> Build(OverpassResponse response)
    {
        var features = new List<Feature>();
        foreach (var el in response.Elements)
        {
            if (el.Type != "way" || el.Geometry is null || el.Geometry.Count < 3)
                continue;

            var coords = el.Geometry
                .Select(p => new Coordinate(p.Lon, p.Lat))
                .ToArray();

            var isClosed = coords.Length >= 4 && coords[0].Equals2D(coords[^1]);
            if (!isClosed)
                continue; 

            var tags = el.Tags ?? new Dictionary<string, string>();
            var attrs = new AttributesTable
            {
                { "osm_id", el.Id },
                { "name", tags.TryGetValue("name", out var n) ? n : string.Empty },
                { "type", tags.TryGetValue("building", out var b) ? b : "yes" },
            };
            features.Add(new Feature(Factory.CreatePolygon(coords), attrs));
        }
        return features;
    }
}

public static class SettlementBuilder
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    public static List<Feature> Build(OverpassResponse response)
    {
        var features = new List<Feature>();
        var allowed = new HashSet<string> { "city", "town", "village", "hamlet", "suburb", "borough" };

        foreach (var el in response.Elements)
        {
            if (el.Type != "node" || !el.Lat.HasValue || !el.Lon.HasValue)
                continue;
            var tags = el.Tags ?? new Dictionary<string, string>();
            if (!tags.TryGetValue("place", out var place) || !allowed.Contains(place))
                continue;

            var attrs = new AttributesTable
            {
                { "osm_id", el.Id },
                { "name", tags.TryGetValue("name", out var n) ? n : string.Empty },
                { "place", place },
                { "population", tags.TryGetValue("population", out var p) ? p : string.Empty },
            };
            features.Add(new Feature(Factory.CreatePoint(new Coordinate(el.Lon.Value, el.Lat.Value)), attrs));
        }
        return features;
    }
}