using OsmToShapefile.Geo.Classifier;
using OsmToShapefile.Scale;

namespace OsmToShapefile.Style;

public static class SldStyleFactory
{
    public static SldStyle Roads(ScaleProfile profile)
    {
        var mult = ScaleMultiplier(profile);
        var rules = new List<SldRule>
        {
            new("main",      FilterEq("class", "main"),      GostPalette.RoadMain,       0.60 * mult, null, null, null, false, "name", 7 * mult, "#000"),
            new("primary",   FilterEq("class", "primary"),   GostPalette.RoadPrimary,    0.45 * mult, null, null, null, false, "name", 7 * mult, "#000"),
            new("secondary", FilterEq("class", "secondary"), GostPalette.RoadSecondary,  0.30 * mult, null, null, null, false, "name", 6 * mult, "#000"),
            new("tertiary",  FilterEq("class", "tertiary"),  GostPalette.RoadTertiary,   0.20 * mult, null, null, null, false),
            new("local",     FilterEq("class", "local"),     GostPalette.RoadLocal,      0.15 * mult, null, null, null, false),
            new("service",   FilterEq("class", "service"),   GostPalette.RoadService,    0.10 * mult, "2 2", null, null, false),
        };
        return new SldStyle(rules);
    }

    public static SldStyle WaterPolygons(ScaleProfile profile) => new(new[]
    {
        new SldRule("water_polygon", null, GostPalette.WaterStroke, 0.20, null, GostPalette.WaterFill, 0.85, true, "name", 8, "#08305C"),
    });

    public static SldStyle WaterLines(ScaleProfile profile) => new(new[]
    {
        new SldRule("water_line", null, GostPalette.WaterStroke, 0.40, null, null, null, false),
    });

    public static SldStyle Wetlands(ScaleProfile profile) => new(new[]
    {
        new SldRule("wetland", null, GostPalette.WaterStroke, 0.20, null, GostPalette.WetlandFill, 0.85, true),
    });

    public static SldStyle Forest() => new(new[]
    {
        new SldRule("forest", null, GostPalette.ForestFill, 0.10, null, GostPalette.ForestFill, 0.85, true),
    });

    public static SldStyle VegetationOther() => new(new[]
    {
        new SldRule("shrub",    null, GostPalette.ShrubFill,    0.10, null, GostPalette.ShrubFill,    0.7, true),
        new SldRule("meadow",   null, GostPalette.MeadowFill,   0.10, null, GostPalette.MeadowFill,   0.7, true),
        new SldRule("sand",     null, GostPalette.SandFill,     0.10, null, GostPalette.SandFill,     0.7, true),
        new SldRule("rocks",    null, GostPalette.RocksFill,    0.10, null, GostPalette.RocksFill,    0.7, true),
        new SldRule("orchard",  null, GostPalette.OrchardFill,  0.10, null, GostPalette.OrchardFill,  0.7, true),
        new SldRule("farmland", null, GostPalette.FarmlandFill, 0.10, null, GostPalette.FarmlandFill, 0.6, true),
        new SldRule("cemetery", null, GostPalette.CemeteryFill, 0.10, null, GostPalette.CemeteryFill, 0.7, true),
    });

    public static SldStyle Buildings() => new(new[]
    {
        new SldRule("building", null, GostPalette.BuildingStroke, 0.10, null, GostPalette.BuildingFill, 0.95, true),
    });

    public static SldStyle Settlements() => new(new[]
    {
        new SldRule("settlement", null, "#000000", 0.20, null, "#000000", 1.0, false, "name", 10, "#000"),
    });

    public static SldStyle Contours(bool index = false) => new(new[]
    {
        new SldRule("contour",
            index ? FilterEq("kind", "index") : FilterEq("kind", "regular"),
            index ? GostPalette.ContourIndexStroke : GostPalette.ContourStroke,
            index ? 0.30 : 0.15,
            null, null, null, false),
    });

    private static string FilterEq(string field, string value) =>
        $@"<ogc:PropertyIsEqualTo><ogc:PropertyName>{field}</ogc:PropertyName><ogc:Literal>{value}</ogc:Literal></ogc:PropertyIsEqualTo>";

    private static double ScaleMultiplier(ScaleProfile profile) => profile.ScaleDenominator switch
    {
        <= 10000  => 1.20,
        <= 25000  => 1.00,
        <= 50000  => 0.85,
        <= 100000 => 0.70,
        _        => 0.55,
    };
}