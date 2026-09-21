using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OsmToShapefile.Overpass;

public readonly record struct BoundingBox(double South, double West, double North, double East)
{
    public string ToOverpassBbox() =>
        FormattableString.Invariant($"{South},{West},{North},{East}");
}

public sealed record LayerDefinition(
    string Name,
    string Key,
    string? Value,
    GeometryKind ExpectedKind,
    string[]? ExtraValues = null,
    bool IncludeRelations = false);

public enum GeometryKind
{
    Line,
    Polygon,
    Point,
}

public sealed class OverpassClient
{
    private const int RetryAttemptsPerMirror = 3;
    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);

    private static readonly string[] MirrorUrls =
    {
        "https://overpass-api.de/api/interpreter",
        "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
        "https://overpass.kumi.systems/api/interpreter",
    };

    private readonly HttpClient _httpClient;

    public OverpassClient(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            var handler = new HttpClientHandler
            {
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                               | System.Security.Authentication.SslProtocols.Tls13,
                AutomaticDecompression = DecompressionMethods.GZip
                                         | DecompressionMethods.Deflate
                                         | DecompressionMethods.Brotli
            };

            httpClient = new HttpClient(handler);
        }

        _httpClient = httpClient;
        _httpClient.Timeout = RequestTimeout;

        // Overpass accepts a regular form POST. Do not imitate a browser: the previous
        // claim that browser-only headers changed server-side query execution was not
        // backed by a reproducible capture. Keep only stable API negotiation headers.
        var headers = _httpClient.DefaultRequestHeaders;
        if (!headers.Contains("User-Agent"))
            headers.TryAddWithoutValidation("User-Agent", "OsmToShapefile/1.0 (+https://github.com/ArtemCPU-cell/QGISTopoMapGOSTPlugin)");
        headers.Accept.Clear();
        headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json", 1.0));
        headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));
    }

    public static async Task<OverpassResponse> LoadFromFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var parsed = await JsonSerializer.DeserializeAsync<OverpassResponse>(stream, cancellationToken: ct);
        return parsed ?? new OverpassResponse();
    }

    public async Task<OverpassResponse> FetchLayerAsync(BoundingBox bbox, LayerDefinition layer, CancellationToken ct = default)
    {
        var query = BuildQuery(bbox, layer);
        Exception? lastError = null;

        foreach (var url in MirrorUrls)
        {
            for (var attempt = 1; attempt <= RetryAttemptsPerMirror; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new FormUrlEncodedContent(
                            new[] { new KeyValuePair<string, string>("data", query) })
                    };

                    var stopwatch = Stopwatch.StartNew();
                    using var response = await _httpClient.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, ct);
                    stopwatch.Stop();

                    Console.WriteLine(
                        $"  {url}: HTTP {(int)response.StatusCode} {response.ReasonPhrase} in {stopwatch.Elapsed.TotalSeconds:F1}s (attempt {attempt}/{RetryAttemptsPerMirror})");

                    if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                    {
                        lastError = new HttpRequestException($"{url}: {response.StatusCode}");
                        if (attempt == RetryAttemptsPerMirror)
                            break;

                        var delay = GetRetryDelay(response);
                        Console.WriteLine($"  {url}: retrying this mirror after {delay.TotalSeconds:F0}s.");
                        await Task.Delay(delay, ct);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var details = await ReadResponseSnippetAsync(response, ct);
                        throw new HttpRequestException($"{url}: {response.StatusCode}. {details}".TrimEnd());
                    }

                    await using var json = await response.Content.ReadAsStreamAsync(ct);
                    var parsed = await JsonSerializer.DeserializeAsync<OverpassResponse>(json, cancellationToken: ct)
                                 ?? new OverpassResponse();

                    if (!string.IsNullOrWhiteSpace(parsed.Remark))
                        throw new InvalidOperationException($"Overpass remark: {parsed.Remark}");

                    Console.WriteLine($"  {url}: received {parsed.Elements.Count} elements.");
                    return parsed;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    lastError = ex;
                    Console.WriteLine($"  mirror {url} failed ({ex.Message}).");
                    break;
                }
            }

            Console.WriteLine($"  switching from {url} to the next mirror.");
        }

        throw new InvalidOperationException("All Overpass mirrors are unavailable.", lastError);
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var until = date - DateTimeOffset.UtcNow;
            if (until > TimeSpan.Zero)
                return until;
        }

        return DefaultRetryDelay;
    }

    private static async Task<string> ReadResponseSnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        body = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return body.Length <= 500 ? body : $"{body[..500]}...";
    }

    private static string BuildQuery(BoundingBox bbox, LayerDefinition layer)
    {
        var bboxStr = bbox.ToOverpassBbox();
        string tagFilter;
        if (!string.IsNullOrEmpty(layer.Value))
        {
            tagFilter = $"[\"{layer.Key}\"=\"{layer.Value}\"]";
        }
        else if (layer.ExtraValues is { Length: > 0 })
        {
            var joined = string.Join("|", layer.ExtraValues);
            tagFilter = $"[\"{layer.Key}\"~\"^({joined})$\"]";
        }
        else
        {
            tagFilter = $"[\"{layer.Key}\"]";
        }

        var elementType = layer.ExpectedKind == GeometryKind.Point ? "node" : "way";

        var union = layer.IncludeRelations
            ? $"""
              (
                {elementType}{tagFilter}({bboxStr});
                relation{tagFilter}({bboxStr});
              );
              out geom;
              """
            : $"""
              (
                {elementType}{tagFilter}({bboxStr});
              );
              out geom;
              """;

        return $"""
                [out:json][timeout:90];
                {union}
                """;
    }
}
