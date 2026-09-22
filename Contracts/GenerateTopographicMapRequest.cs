using System.Text.Json;
using OsmToShapefile.Cli;
using OsmToShapefile.Overpass;
using OsmToShapefile.Scale;

namespace OsmToShapefile.Contracts;

/// <summary>
/// Versioned file contract used by the future QGIS Python adapter to start the
/// .NET cartographic engine without relying on command-line argument ordering.
/// </summary>
public sealed class GenerateTopographicMapRequest
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;
    public string Operation { get; init; } = "generate-topographic-map";
    public double[] Bbox { get; init; } = Array.Empty<double>();
    public string Scale { get; init; } = "25k";
    public string TargetSrs { get; init; } = "EPSG:32637";
    public string OutputDirectory { get; init; } = "output";
    public bool OfflineOsm { get; init; }
    public bool NoDem { get; init; }
    public string? DemFile { get; init; }
    public bool QgisProject { get; init; } = true;

    public static GenerateTopographicMapRequest Load(string path)
    {
        using var stream = File.OpenRead(path);
        var request = JsonSerializer.Deserialize<GenerateTopographicMapRequest>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return request ?? throw new InvalidDataException($"Request JSON is empty: {path}");
    }

    public RunOptions ToRunOptions(string? responsePath)
    {
        if (Version != CurrentVersion)
            throw new ArgumentException($"Unsupported request contract version {Version}. Expected {CurrentVersion}.");
        if (!string.Equals(Operation, "generate-topographic-map", StringComparison.Ordinal))
            throw new ArgumentException($"Unsupported operation '{Operation}'.");
        if (Bbox.Length != 4)
            throw new ArgumentException("Request property 'bbox' must be [south, west, north, east].");
        if (NoDem && !string.IsNullOrWhiteSpace(DemFile))
            throw new ArgumentException("'noDem' cannot be combined with 'demFile'.");

        var bbox = new BoundingBox(Bbox[0], Bbox[1], Bbox[2], Bbox[3]);
        if (bbox.South >= bbox.North || bbox.West >= bbox.East)
            throw new ArgumentException("Request bbox must have south < north and west < east.");

        return new RunOptions(
            bbox,
            ScaleProfile.Parse(Scale),
            OutputDirectory,
            TargetSrs,
            OfflineOsm,
            NoDem,
            QgisProject,
            DemFile,
            responsePath,
            Version);
    }
}
