namespace OsmToShapefile.Scale;


public sealed record ScaleProfile(
    int ScaleDenominator,
    string SldTitle,
    double DouglasPeuckerToleranceMeters,
    double MinRiverPolygonWidthMeters,
    double MinBuildingAreaSquareMeters,
    double ContourIntervalMeters,
    double MinFeatureLengthMeters)
{
    public static ScaleProfile For(MapScale scale) => scale switch
    {
        MapScale.Scale10k => new ScaleProfile(
            ScaleDenominator: 10000,
            SldTitle: "1:10 000",
            DouglasPeuckerToleranceMeters: 0.5,
            MinRiverPolygonWidthMeters: 3.0,
            MinBuildingAreaSquareMeters: 6.0,
            ContourIntervalMeters: 2.5,
            MinFeatureLengthMeters: 5.0),

        MapScale.Scale25k => new ScaleProfile(
            ScaleDenominator: 25000,
            SldTitle: "1:25 000",
            DouglasPeuckerToleranceMeters: 1.25,
            MinRiverPolygonWidthMeters: 5.0,
            MinBuildingAreaSquareMeters: 12.0,
            ContourIntervalMeters: 5.0,
            MinFeatureLengthMeters: 10.0),

        MapScale.Scale50k => new ScaleProfile(
            ScaleDenominator: 50000,
            SldTitle: "1:50 000",
            DouglasPeuckerToleranceMeters: 2.5,
            MinRiverPolygonWidthMeters: 8.0,
            MinBuildingAreaSquareMeters: 25.0,
            ContourIntervalMeters: 10.0,
            MinFeatureLengthMeters: 20.0),

        MapScale.Scale100k => new ScaleProfile(
            ScaleDenominator: 100000,
            SldTitle: "1:100 000",
            DouglasPeuckerToleranceMeters: 5.0,
            MinRiverPolygonWidthMeters: 15.0,
            MinBuildingAreaSquareMeters: 60.0,
            ContourIntervalMeters: 20.0,
            MinFeatureLengthMeters: 40.0),

        MapScale.Scale200k => new ScaleProfile(
            ScaleDenominator: 200000,
            SldTitle: "1:200 000",
            DouglasPeuckerToleranceMeters: 10.0,
            MinRiverPolygonWidthMeters: 25.0,
            MinBuildingAreaSquareMeters: 150.0,
            ContourIntervalMeters: 40.0,
            MinFeatureLengthMeters: 80.0),

        _ => throw new ArgumentOutOfRangeException(nameof(scale), scale, "Неизвестный масштаб"),
    };

    public static MapScale Parse(string s)
    {
        var token = s.Trim().ToLowerInvariant().Replace("_", string.Empty);
        return token switch
        {
            "10k" or "1:10000" or "10000" => MapScale.Scale10k,
            "25k" or "1:25000" or "25000" => MapScale.Scale25k,
            "50k" or "1:50000" or "50000" => MapScale.Scale50k,
            "100k" or "1:100000" or "100000" => MapScale.Scale100k,
            "200k" or "1:200000" or "200000" => MapScale.Scale200k,
            _ => throw new ArgumentException($"Не удалось распознать масштаб: '{s}'. Допустимо: 10k|25k|50k|100k|200k"),
        };
    }
}