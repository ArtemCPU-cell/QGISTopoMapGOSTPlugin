using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

namespace OsmToShapefile.Geo.Generalizer;

public static class DouglasPeuckerSimplifier
{
    public static Geometry Simplify(Geometry geometry, double tolerance)
    {
        if (tolerance <= 0)
            return geometry;

        return NetTopologySuite.Simplify.DouglasPeuckerSimplifier.Simplify(geometry, tolerance);
    }
}