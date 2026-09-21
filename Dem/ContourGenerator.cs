using NetTopologySuite.Features;
using NetTopologySuite.Geometries;

namespace OsmToShapefile.Dem;

public static class ContourGenerator
{
    public sealed record ContourSet(List<Feature> Lines);

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
        var zEnd   = Math.Ceiling(maxH / intervalMeters) * intervalMeters;

        var cellCoords = new List<Coordinate>();

        for (var z = zStart; z <= zEnd + 1e-6; z += intervalMeters)
        {
            cellCoords.Clear();
            for (var row = 0; row < grid.Height - 1; row++)
            {
                for (var col = 0; col < grid.Width - 1; col++)
                {
                    var h00 = grid.HeightAt(row, col);
                    var h10 = grid.HeightAt(row, col + 1);
                    var h11 = grid.HeightAt(row + 1, col + 1);
                    var h01 = grid.HeightAt(row + 1, col);
                    if (double.IsNaN(h00) || double.IsNaN(h10) || double.IsNaN(h11) || double.IsNaN(h01))
                        continue;

                    var lon0 = grid.Lons[col];
                    var lon1 = grid.Lons[col + 1];
                    var lat0 = grid.Lats[row];
                    var lat1 = grid.Lats[row + 1];

                    var pts = new List<Coordinate>();
                    AddIfCross(pts, lon0, lat0, lon1, lat0, h00, h10, z); // верхняя
                    AddIfCross(pts, lon1, lat0, lon1, lat1, h10, h11, z); // правая
                    AddIfCross(pts, lon0, lat1, lon1, lat1, h01, h11, z); // нижняя
                    AddIfCross(pts, lon0, lat0, lon0, lat1, h00, h01, z); // левая

                    if (pts.Count == 2)
                    {
                        var line = new Coordinate[] { pts[0], pts[1] };
                        var geom = new LineString(line);
                        var attrs = new AttributesTable
                        {
                            { "elevation", (int)Math.Round(z) },
                            { "kind", IsIndex(z, intervalMeters) ? "index" : "regular" },
                        };
                        features.Add(new Feature(geom, attrs));
                    }
                    else if (pts.Count > 2)
                    {
                        for (var i = 0; i < pts.Count; i += 2)
                        {
                            if (i + 1 >= pts.Count) break;
                            var line = new Coordinate[] { pts[i], pts[i + 1] };
                            var attrs = new AttributesTable
                            {
                                { "elevation", (int)Math.Round(z) },
                                { "kind", IsIndex(z, intervalMeters) ? "index" : "regular" },
                            };
                            features.Add(new Feature(new LineString(line), attrs));
                        }
                    }
                }
            }
        }

        return new ContourSet(features);
    }

    private static bool IsIndex(double z, double interval)
        => Math.Abs(z / (interval * 5) - Math.Round(z / (interval * 5))) < 1e-6;

    private static void AddIfCross(List<Coordinate> pts,
        double x1, double y1, double x2, double y2,
        double h1, double h2, double z)
    {
        if (double.IsNaN(h1) || double.IsNaN(h2)) return;
        if ((h1 <= z && h2 <= z) || (h1 >= z && h2 >= z)) return;
        if (Math.Abs(h2 - h1) < 1e-9) return;
        var t = (z - h1) / (h2 - h1);
        var x = x1 + t * (x2 - x1);
        var y = y1 + t * (y2 - y1);
        pts.Add(new Coordinate(x, y));
    }
}