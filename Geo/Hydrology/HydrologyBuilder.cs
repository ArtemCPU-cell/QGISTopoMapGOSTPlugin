using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using OsmToShapefile.Overpass;
using OsmToShapefile.Scale;

namespace OsmToShapefile.Geo.Hydrology;

public static class HydrologyBuilder
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    public sealed record HydrologyLayers(
        List<Feature> Polygons,
        List<Feature> Lines,
        List<Feature> Wetlands);

    public static HydrologyLayers Build(OverpassResponse response, ScaleProfile profile)
    {
        var polys = new List<Feature>();
        var lines = new List<Feature>();
        var wetlands = new List<Feature>();

        foreach (var el in response.Elements)
        {
            if (el.Type != "way" || el.Geometry is null || el.Geometry.Count < 2)
                continue;

            var coords = el.Geometry
                .Select(p => new Coordinate(p.Lon, p.Lat))
                .ToArray();

            var tags = el.Tags ?? new Dictionary<string, string>();
            var isClosed = coords.Length >= 4 && coords[0].Equals2D(coords[^1]);

            if (tags.TryGetValue("natural", out var naturalVal) && naturalVal == "wetland" && isClosed)
            {
                wetlands.Add(BuildFeature(Factory.CreatePolygon(coords), tags, el.Id, "wetland", profile));
                continue;
            }

            if (naturalVal == "water" && isClosed)
            {
                polys.Add(BuildFeature(Factory.CreatePolygon(coords), tags, el.Id, "waterbody", profile));
                continue;
            }
            if (tags.TryGetValue("waterway", out var waterwayVal))
            {
                switch (waterwayVal)
                {
                    case "river":
                    case "riverbank":
                        var width = ParseWidth(tags);
                        if (isClosed && width.HasValue && width.Value >= profile.MinRiverPolygonWidthMeters)
                        {
                            polys.Add(BuildFeature(Factory.CreatePolygon(coords), tags, el.Id, "river_wide", profile));
                        }
                        else
                        {
                            lines.Add(BuildFeature(Factory.CreateLineString(coords), tags, el.Id, "river_narrow", profile));
                        }
                        break;

                    case "stream":
                        lines.Add(BuildFeature(Factory.CreateLineString(coords), tags, el.Id, "stream", profile));
                        break;

                    case "canal":
                        lines.Add(BuildFeature(Factory.CreateLineString(coords), tags, el.Id, "canal", profile));
                        break;
                }
            }
        }

        return new HydrologyLayers(polys, lines, wetlands);
    }

    private static double? ParseWidth(Dictionary<string, string> tags)
    {

        if (!tags.TryGetValue("width", out var raw))
            return null;

        var s = raw.Trim().Split(' ', 2)[0].Replace(',', '.');
        return double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var w) ? w : null;
    }

    private static Feature BuildFeature(Geometry g, Dictionary<string, string> tags,
        long osmId, string className, ScaleProfile profile)
    {
        var attrs = new AttributesTable
        {
            { "osm_id", osmId },
            { "name", tags.TryGetValue("name", out var n) ? n : string.Empty },
            { "class", className },
            { "waterway", tags.TryGetValue("waterway", out var w) ? w : string.Empty },
            { "natural", tags.TryGetValue("natural", out var nat) ? nat : string.Empty },
        };
        return new Feature(g, attrs);
    }
}