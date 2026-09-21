using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Linemerge;
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
            var tags = el.Tags ?? new Dictionary<string, string>();
            if (el.Type == "way")
            {
                var polygon = BuildWay(el.Geometry);
                if (polygon is not null)
                    features.Add(BuildFeature(polygon, el.Id, tags));
            }
            else if (el.Type == "relation")
            {
                foreach (var polygon in BuildMultipolygonRelation(el.Members))
                    features.Add(BuildFeature(polygon, el.Id, tags));
            }
        }

        return features;
    }

    private static Polygon? BuildWay(List<GeoPoint>? geometry)
    {
        if (geometry is null || geometry.Count < 4)
            return null;

        var coords = geometry.Select(p => new Coordinate(p.Lon, p.Lat)).ToArray();
        return coords[0].Equals2D(coords[^1]) ? Factory.CreatePolygon(coords) : null;
    }

    private static IEnumerable<Polygon> BuildMultipolygonRelation(List<OverpassMember>? members)
    {
        if (members is null)
            yield break;

        var outerRings = MergeMemberRings(members, "inner", includeMatchingRole: false);
        var innerRings = MergeMemberRings(members, "inner", includeMatchingRole: true);

        foreach (var shell in outerRings)
        {
            var shellPolygon = Factory.CreatePolygon(shell);
            var holes = innerRings
                .Where(hole => shellPolygon.Covers(Factory.CreatePoint(hole.Coordinates[0])))
                .ToArray();
            yield return Factory.CreatePolygon(shell, holes);
        }
    }

    private static List<LinearRing> MergeMemberRings(
        IEnumerable<OverpassMember> members, string role, bool includeMatchingRole)
    {
        var merger = new LineMerger();
        foreach (var member in members)
        {
            var hasRequestedRole = string.Equals(member.Role, role, StringComparison.OrdinalIgnoreCase);
            if (member.Type != "way" || hasRequestedRole != includeMatchingRole ||
                member.Geometry is not { Count: >= 2 })
            {
                continue;
            }

            var coords = member.Geometry.Select(p => new Coordinate(p.Lon, p.Lat)).ToArray();
            merger.Add(Factory.CreateLineString(coords));
        }

        var rings = new List<LinearRing>();
        foreach (var merged in merger.GetMergedLineStrings().OfType<LineString>())
        {
            if (merged.NumPoints >= 4 && merged.IsClosed)
                rings.Add(Factory.CreateLinearRing(merged.Coordinates));
        }

        return rings;
    }

    private static Feature BuildFeature(Polygon geometry, long id, IReadOnlyDictionary<string, string> tags)
    {
        var attrs = new AttributesTable
        {
            { "osm_id", id },
            { "name", tags.TryGetValue("name", out var n) ? n : string.Empty },
            { "type", tags.TryGetValue("building", out var b) ? b : "yes" },
        };
        return new Feature(geometry, attrs);
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
