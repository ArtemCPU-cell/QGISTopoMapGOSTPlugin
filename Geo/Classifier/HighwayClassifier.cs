namespace OsmToShapefile.Geo.Classifier;

/// <summary>
/// Класс дороги по мотивам ГОСТ: толщина линии в SLD считается по классу,
/// от самого крупного (main) до самого тонкого (service).
/// </summary>
public enum HighwayClass
{
    Main,        // motorway, motorway_link, trunk, trunk_link
    Primary,     // primary, primary_link
    Secondary,   // secondary, secondary_link
    Tertiary,    // tertiary
    Local,       // residential, unclassified, living_street
    Service,     // service, track, path, footway, pedestrian, steps, bus_stop
    Unknown,
}

/// <summary>
/// Классификация дорог по значению тега highway=*.
/// </summary>
public static class HighwayClassifier
{
    public static HighwayClass Classify(string? highwayValue)
    {
        if (string.IsNullOrEmpty(highwayValue))
            return HighwayClass.Unknown;

        // Link-дороги — продолжения основных классов. Идёт до общего,
        // потому что *_link в ОСМ обычно явно выставляется отдельным значением.
        if (highwayValue is "motorway" or "motorway_link" or "trunk" or "trunk_link")
            return HighwayClass.Main;
        if (highwayValue is "primary" or "primary_link")
            return HighwayClass.Primary;
        if (highwayValue is "secondary" or "secondary_link")
            return HighwayClass.Secondary;
        if (highwayValue == "tertiary")
            return HighwayClass.Tertiary;
        if (highwayValue is "residential" or "unclassified" or "living_street")
            return HighwayClass.Local;
        if (highwayValue is "service" or "track" or "path" or "footway"
                       or "pedestrian" or "steps" or "bus_stop" or "cycleway")
            return HighwayClass.Service;

        return HighwayClass.Unknown;
    }
}