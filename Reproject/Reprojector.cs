using NetTopologySuite.Geometries;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace OsmToShapefile.Reproject;

public sealed class Reprojector
{
    private readonly ICoordinateTransformation _wgs84ToTarget;
    private readonly CoordinateSystem _target;

    public string TargetAuthority { get; }
    public int TargetSrid { get; }
    public string TargetWkt { get; }

    public Reprojector(string targetSrs)
    {
        var (auth, code) = ParseSrs(targetSrs);
        TargetAuthority = auth;
        TargetSrid = code;

        var wgs84 = GeographicCoordinateSystem.WGS84;
        _target = ProjectedCoordinateSystem.WGS84_UTM(code - 32600, true)
                  ?? throw new NotSupportedException(
                      $"Не удалось построить UTM-проекцию для EPSG:{code}. Поддержаны UTM-зоны 1..60 (EPSG:32601..32660, 32701..32760).");

        _wgs84ToTarget = new CoordinateTransformationFactory().CreateFromCoordinateSystems(wgs84, _target);
        TargetWkt = _target.WKT;
    }

    public (double X, double Y) Transform(double lon, double lat)
    {
        var p = _wgs84ToTarget.MathTransform.Transform(new[] { lon, lat });
        return (p[0], p[1]);
    }

    public Coordinate Transform(Coordinate c)
    {
        var (x, y) = Transform(c.X, c.Y);
        return new Coordinate(x, y);
    }

    public Geometry TransformGeometry(Geometry src)
    {
        src.Apply(new ReprojectFilter(this));
        src.SRID = TargetSrid;
        return src;
    }

    private static (string Auth, int Code) ParseSrs(string srs)
    {
        var s = srs.Trim();
        if (s.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase))
            s = s[5..];
        if (s.StartsWith("urn:ogc:def:crs:EPSG::", StringComparison.OrdinalIgnoreCase))
            s = s[22..];
        if (!int.TryParse(s, out var code))
            throw new ArgumentException($"Не удалось распознать SRS '{srs}'. Ожидается EPSG:<число>.");
        return ("EPSG", code);
    }

    private sealed class ReprojectFilter : ICoordinateFilter
    {
        private readonly Reprojector _r;
        public ReprojectFilter(Reprojector r) { _r = r; }
        public void Filter(Coordinate c)
        {
            var p = _r.Transform(c.X, c.Y);
            c.X = p.X;
            c.Y = p.Y;
        }
    }
}