namespace OsmToShapefile.Geo.Hydrology;


public enum WaterKind
{
    RiverWide,        // waterway=river или natural=water, ширина >= порога масштаба
    RiverNarrow,      // waterway=river, узкая — линия
    Stream,           // waterway=stream
    Canal,            // waterway=canal
    Waterbody,        // natural=water (озеро, пруд, водохранилище)
    Wetland,          // natural=wetland
}

public sealed record WaterFeatureInfo(WaterKind Kind, string Name);