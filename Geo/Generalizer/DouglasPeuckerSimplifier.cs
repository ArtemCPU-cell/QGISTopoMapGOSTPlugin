using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;

namespace OsmToShapefile.Geo.Generalizer;

/// <summary>
/// Генерализация линий/полигонов через Douglas–Peucker. Tolerance — в единицах
/// целевой SRS (метры для UTM), задаётся в ScaleProfile.
/// </summary>
public static class DouglasPeuckerSimplifier
{
    public static Geometry Simplify(Geometry geometry, double tolerance)
    {
        if (tolerance <= 0)
            return geometry;

        return NetTopologySuite.Simplify.DouglasPeuckerSimplifier.Simplify(geometry, tolerance);
    }
}