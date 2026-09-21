using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using OsmToShapefile.Overpass;

namespace OsmToShapefile.Geo.Vegetation;


public static class VegetationClassifier
{
    public static string? Classify(IReadOnlyDictionary<string, string> tags)
    {
        if (tags.TryGetValue("landuse", out var lu))
        {
            return lu switch
            {
                "forest" => "forest",
                "meadow" or "grass" => "meadow",
                "farmland" or "farmyard" or "field" => "farmland",
                "vineyard" or "orchard" => "orchard",
                "reservoir" => null, // это к гидрографии
                "cemetery" => "cemetery",
                _ => null,
            };
        }

        if (tags.TryGetValue("natural", out var nat))
        {
            return nat switch
            {
                "wood" => "forest",
                "scrub" => "shrub",
                "grassland" => "meadow",
                "sand" => "sand",
                "bare_rock" or "rock" or "cliff" => "rocks",
                "beach" => "sand",
                _ => null,
            };
        }

        return null;
    }
}

public static class VegetationBuilder
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    public static Dictionary<string, List<Feature>> Build(OverpassResponse response)
    {
        var byClass = new Dictionary<string, List<Feature>>();

        foreach (var el in response.Elements)
        {
            if (el.Type != "way" || el.Geometry is null || el.Geometry.Count < 2)
                continue;

            var coords = el.Geometry
                .Select(p => new Coordinate(p.Lon, p.Lat))
                .ToArray();

            var tags = el.Tags ?? new Dictionary<string, string>();
            var className = VegetationClassifier.Classify(tags);
            if (className is null)
                continue;

            var isClosed = coords.Length >= 4 && coords[0].Equals2D(coords[^1]);
            Geometry g = isClosed
                ? Factory.CreatePolygon(coords)
                : Factory.CreateLineString(coords);

            var attrs = new AttributesTable
            {
                { "osm_id", el.Id },
                { "name", tags.TryGetValue("name", out var n) ? n : string.Empty },
                { "class", className },
            };

            if (!byClass.TryGetValue(className, out var list))
            {
                list = new List<Feature>();
                byClass[className] = list;
            }
            list.Add(new Feature(g, attrs));
        }

        return byClass;
    }
}