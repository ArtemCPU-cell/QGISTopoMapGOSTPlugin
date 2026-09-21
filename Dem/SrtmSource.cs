using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using OsmToShapefile.Overpass;

namespace OsmToShapefile.Dem;


public sealed class SrtmSource
{
    private readonly HttpClient _http;

    public SrtmSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient
        {
            BaseAddress = new Uri("https://api.opentopodata.org"),
            Timeout = TimeSpan.FromSeconds(60),
        };
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "curl/8.4.0");
    }
    public async Task<DemGrid> FetchGridAsync(BoundingBox bbox, double stepDeg,
        IProgress<string>? log = null, CancellationToken ct = default)
    {
        var lats = new List<double>();
        var lons = new List<double>();

        for (var lat = bbox.South; lat <= bbox.North + 1e-9; lat += stepDeg)
            lats.Add(Math.Min(lat, bbox.North));
        for (var lon = bbox.West; lon <= bbox.East + 1e-9; lon += stepDeg)
            lons.Add(Math.Min(lon, bbox.East));

        var flat = new List<double>();
        var total = lats.Count * lons.Count;
        var batchSize = 100; 

        log?.Report($"DEM: сетка {lats.Count}x{lons.Count}={total} точек");

        for (var start = 0; start < total; start += batchSize)
        {
            var end = Math.Min(start + batchSize, total);
            var locs = new List<string>(end - start);
            for (var i = start; i < end; i++)
            {
                var latIdx = i / lons.Count;
                var lonIdx = i % lons.Count;
                locs.Add($"{lats[latIdx].ToString(CultureInfo.InvariantCulture)},{lons[lonIdx].ToString(CultureInfo.InvariantCulture)}");
            }
            var url = $"/v1/srtm30m?locations={string.Join("|", locs)}";
            log?.Report($"  запрос {start}..{end - 1} из {total}");
            var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadFromJsonAsync<OtdResponse>(cancellationToken: ct);

            foreach (var r in body?.Results ?? new())
                flat.Add(r.Elevation ?? double.NaN);

            await Task.Delay(1100, ct);
        }

        while (flat.Count < total) flat.Add(double.NaN);
        if (flat.Count > total) flat = flat.GetRange(0, total);

        return new DemGrid(lats.ToArray(), lons.ToArray(), flat.ToArray());
    }

    private sealed class OtdResponse
    {
        [JsonPropertyName("results")] public List<OtdResult>? Results { get; set; }
    }
    private sealed class OtdResult
    {
        [JsonPropertyName("elevation")] public double? Elevation { get; set; }
        [JsonPropertyName("location")] public OtdLocation? Location { get; set; }
    }
    private sealed class OtdLocation
    {
        [JsonPropertyName("lat")] public double Lat { get; set; }
        [JsonPropertyName("lng")] public double Lng { get; set; }
    }
}

public sealed record DemGrid(double[] Lats, double[] Lons, double[] Heights)
{
    public int Width => Lons.Length;
    public int Height => Lats.Length;
    public double HeightAt(int row, int col)
    {
        if (row < 0 || row >= Height || col < 0 || col >= Width) return double.NaN;
        return Heights[row * Width + col];
    }
}