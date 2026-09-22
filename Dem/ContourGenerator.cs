using NetTopologySuite.Features;
using NetTopologySuite.Geometries;

namespace OsmToShapefile.Dem;

public static class ContourGenerator
{
    public sealed record ContourSet(List<Feature> Lines);

    private sealed record Segment(Coordinate A, Coordinate B);

    private readonly record struct PointKey(long X, long Y);

    public static ContourSet Build(DemGrid grid, double intervalMeters)
    {
        var features = new List<Feature>();
        if (intervalMeters <= 0) return new ContourSet(features);

        var minH = double.PositiveInfinity;
        var maxH = double.NegativeInfinity;
        for (var i = 0; i < grid.Heights.Length; i++)
        {
            if (double.IsNaN(grid.Heights[i])) continue;
            if (grid.Heights[i] < minH) minH = grid.Heights[i];
            if (grid.Heights[i] > maxH) maxH = grid.Heights[i];
        }
        if (double.IsInfinity(minH) || double.IsInfinity(maxH))
            return new ContourSet(features);

        var zStart = Math.Floor(minH / intervalMeters) * intervalMeters;
        var zEnd = Math.Ceiling(maxH / intervalMeters) * intervalMeters;

        for (var z = zStart; z <= zEnd + 1e-6; z += intervalMeters)
        {
            var segments = new List<Segment>();
            // SRTM elevations are commonly integral metres, while the map uses
            // contour levels divisible by 5 m. Sampling exactly at z makes many
            // grid vertices lie exactly on the level. The old strict crossing
            // test then discarded both adjacent cell edges, breaking one contour
            // into short fragments. A sub-millimetre offset deterministically
            // resolves that marching-squares degeneracy; the output attribute
            // remains the nominal contour elevation z.
            var sampledLevel = z + 0.001;
            for (var row = 0; row < grid.Height - 1; row++)
            {
                for (var col = 0; col < grid.Width - 1; col++)
                {
                    var h00 = grid.HeightAt(row, col);
                    var h10 = grid.HeightAt(row, col + 1);
                    var h11 = grid.HeightAt(row + 1, col + 1);
                    var h01 = grid.HeightAt(row + 1, col);
                    if (double.IsNaN(h00) || double.IsNaN(h10) ||
                        double.IsNaN(h11) || double.IsNaN(h01))
                        continue;

                    var lon0 = grid.Lons[col];
                    var lon1 = grid.Lons[col + 1];
                    var lat0 = grid.Lats[row];
                    var lat1 = grid.Lats[row + 1];
                    var intersections = new List<Coordinate>(4);

                    AddIfCross(intersections, lon0, lat0, lon1, lat0, h00, h10, sampledLevel);
                    AddIfCross(intersections, lon1, lat0, lon1, lat1, h10, h11, sampledLevel);
                    AddIfCross(intersections, lon0, lat1, lon1, lat1, h01, h11, sampledLevel);
                    AddIfCross(intersections, lon0, lat0, lon0, lat1, h00, h01, sampledLevel);

                    // A regular marching-squares cell contributes one or two
                    // segments. They are stitched below; emitting one feature per
                    // cell was the reason every contour was visibly fragmented.
                    for (var i = 0; i + 1 < intersections.Count; i += 2)
                    {
                        if (!intersections[i].Equals2D(intersections[i + 1]))
                            segments.Add(new Segment(intersections[i], intersections[i + 1]));
                    }
                }
            }

            var kind = IsIndex(z, intervalMeters) ? "index" : "regular";
            foreach (var line in Stitch(segments))
            {
                if (line.Count < 2)
                    continue;

                var coordinates = line.ToArray();
                var attributes = new AttributesTable
                {
                    { "elevation", (int)Math.Round(z) },
                    { "kind", kind },
                    { "closed", coordinates[0].Equals2D(coordinates[^1]) ? "yes" : "no" },
                };
                features.Add(new Feature(new LineString(coordinates), attributes));
            }
        }

        return new ContourSet(features);
    }

    private static List<List<Coordinate>> Stitch(List<Segment> segments)
    {
        var lines = new List<List<Coordinate>>();
        if (segments.Count == 0)
            return lines;

        // Intersections calculated on opposite sides of neighbouring cells can
        // differ by a few floating-point ulps. Quantization makes those endpoints
        // share one graph vertex without changing the coordinates written out.
        var endpointToSegments = new Dictionary<PointKey, List<int>>();
        for (var i = 0; i < segments.Count; i++)
        {
            AddEndpoint(endpointToSegments, Key(segments[i].A), i);
            AddEndpoint(endpointToSegments, Key(segments[i].B), i);
        }

        var used = new bool[segments.Count];
        for (var start = 0; start < segments.Count; start++)
        {
            if (used[start])
                continue;

            var first = segments[start];
            var line = new List<Coordinate> { first.A, first.B };
            used[start] = true;
            Extend(line, endpointToSegments, segments, used, fromStart: false);
            Extend(line, endpointToSegments, segments, used, fromStart: true);
            lines.Add(line);
        }

        return lines;
    }

    private static void Extend(
        List<Coordinate> line,
        Dictionary<PointKey, List<int>> endpointToSegments,
        List<Segment> segments,
        bool[] used,
        bool fromStart)
    {
        while (true)
        {
            var current = fromStart ? line[0] : line[^1];
            var key = Key(current);
            var next = endpointToSegments[key]
                .FirstOrDefault(index => !used[index], -1);
            if (next < 0)
                return;

            var segment = segments[next];
            var nextPoint = Key(segment.A) == key ? segment.B : segment.A;
            used[next] = true;
            if (fromStart)
                line.Insert(0, nextPoint);
            else
                line.Add(nextPoint);

            if (line.Count > 2 && Key(line[0]) == Key(line[^1]))
                return;
        }
    }

    private static void AddEndpoint(
        Dictionary<PointKey, List<int>> endpointToSegments, PointKey key, int index)
    {
        if (!endpointToSegments.TryGetValue(key, out var list))
        {
            list = new List<int>();
            endpointToSegments[key] = list;
        }
        list.Add(index);
    }

    private static PointKey Key(Coordinate coordinate)
        => new((long)Math.Round(coordinate.X * 1_000_000_000d),
               (long)Math.Round(coordinate.Y * 1_000_000_000d));

    private static bool IsIndex(double z, double interval)
        => Math.Abs(z / (interval * 5) - Math.Round(z / (interval * 5))) < 1e-6;

    private static void AddIfCross(List<Coordinate> points,
        double x1, double y1, double x2, double y2,
        double h1, double h2, double z)
    {
        if (double.IsNaN(h1) || double.IsNaN(h2)) return;
        if ((h1 <= z && h2 <= z) || (h1 >= z && h2 >= z)) return;
        if (Math.Abs(h2 - h1) < 1e-9) return;
        var t = (z - h1) / (h2 - h1);
        points.Add(new Coordinate(x1 + t * (x2 - x1), y1 + t * (y2 - y1)));
    }
}
