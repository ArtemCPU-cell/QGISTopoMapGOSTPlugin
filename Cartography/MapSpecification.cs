using System.Text.Json;
using System.Text.Json.Serialization;
using OsmToShapefile.Cli;
using OsmToShapefile.Scale;

namespace OsmToShapefile.Cartography;

public enum CartographicQualityMode
{
    Preview,
    ProductionCandidate,
}

public sealed record MapSpecification(
    int ContractVersion,
    string MapScale,
    string TargetSrs,
    IReadOnlyList<string> Layers,
    CartographicQualityMode QualityMode,
    IReadOnlyList<string> StandardsTargeted)
{
    public static MapSpecification From(RunOptions options) => new(
        options.ContractVersion,
        $"1:{ScaleProfile.For(options.Scale).ScaleDenominator}",
        options.TargetSrs,
        new[] { "roads", "buildings", "water", "vegetation", "settlements", "contours" },
        !options.NoDem && !string.IsNullOrWhiteSpace(options.DemFile)
            ? CartographicQualityMode.ProductionCandidate
            : CartographicQualityMode.Preview,
        new[]
        {
            "GOST R 51605-2023",
            "GOST R 51606-2024",
            "GOST R 51607-2024",
            "GOST R 51608-2024",
            "GOST R 70846.4-2023",
            "GOST R 70846.5-2023",
            "GOST R 70846.6-2023",
        });
}

public sealed record DataSourceProfile(
    string Osm,
    string Dem,
    double? DemResolutionMeters,
    CartographicQualityMode QualityMode,
    IReadOnlyList<string> Limitations)
{
    public static DataSourceProfile From(RunOptions options) => new(
        options.Offline ? "Bundled Overpass JSON cache" : "Public Overpass API mirrors",
        options.NoDem
            ? "Not requested"
            : string.IsNullOrWhiteSpace(options.DemFile)
                ? "OpenTopoData SRTM 30 m preview service"
                : $"Supplied GeoTIFF DTM: {Path.GetFullPath(options.DemFile)}",
        options.NoDem || !string.IsNullOrWhiteSpace(options.DemFile)
            ? null
            : 30d,
        !options.NoDem && !string.IsNullOrWhiteSpace(options.DemFile)
            ? CartographicQualityMode.ProductionCandidate
            : CartographicQualityMode.Preview,
        options.NoDem
            ? new[] { "Relief was disabled by --no-dem." }
            : string.IsNullOrWhiteSpace(options.DemFile)
                ? new[]
                {
                    "OSM and SRTM 30 m are preview data sources, not authoritative production survey data.",
                    "A supplied, documented terrain DTM is required before claiming production or regulatory conformance.",
                }
            : new[]
            {
                "The supplied DTM is checked for GeoTIFF structure, WGS84 CRS and bbox coverage.",
                "Vertical datum and survey accuracy must still be documented by the data owner.",
            });
}

public sealed record GeneratedLayer(string Name, string DataPath, string? StylePath, int FeatureCount);

public sealed record GenerateTopographicMapResult(
    int Version,
    bool Success,
    DateTimeOffset GeneratedAtUtc,
    MapSpecification Specification,
    DataSourceProfile DataSources,
    IReadOnlyList<GeneratedLayer> Layers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors);

public static class MapResultWriter
{
    public static string WriteSuccess(
        RunOptions options,
        MapSpecification specification,
        DataSourceProfile dataSources)
    {
        var layers = Directory.EnumerateFiles(options.OutputDir, "*.shp")
            .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
            .Select(path => new GeneratedLayer(
                Path.GetFileNameWithoutExtension(path),
                Path.GetFullPath(path),
                File.Exists(Path.ChangeExtension(path, ".sld")) ? Path.GetFullPath(Path.ChangeExtension(path, ".sld")) : null,
                ReadDbfRecordCount(Path.ChangeExtension(path, ".dbf"))))
            .ToList();

        var warnings = dataSources.Limitations.ToList();
        if (!layers.Any(layer => layer.Name == "vegetation"))
            warnings.Add("Vegetation layer was not written because the selected source returned no matching features.");

        var result = new GenerateTopographicMapResult(
            specification.ContractVersion,
            true,
            DateTimeOffset.UtcNow,
            specification,
            dataSources,
            layers,
            warnings,
            Array.Empty<string>());

        var path = options.ResponsePath ?? Path.Combine(options.OutputDir, "map-result.json");
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        }));
        return path;
    }

    private static int ReadDbfRecordCount(string path)
    {
        if (!File.Exists(path))
            return 0;

        using var stream = File.OpenRead(path);
        stream.Seek(4, SeekOrigin.Begin);
        Span<byte> count = stackalloc byte[4];
        if (stream.Read(count) != 4)
            return 0;
        return System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(count);
    }
}
