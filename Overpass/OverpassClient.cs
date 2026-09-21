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
        _httpClient.Timeout = TimeSpan.FromSeconds(15);

        // Только User-Agent. Полный "браузерный" набор заголовков (Sec-Fetch-*, sec-ch-ua-*,
        // Accept-Language, Origin, Referer, Cache-Control) раньше ломал обработку на
        // overpass-api.de: запрос уходил без ошибок, но сервер через десятки секунд отвечал
        // remark "Query timed out" или "out of memory", хотя curl с тем же bbox отвечал 200 OK
        // за 2-4 секунды. Проверено: bare curl без заголовков вообще — 200, curl только с
        // User-Agent: curl/8.4.0 — тоже 200. Значит, лишние заголовки запускают на сервере
        // какой-то "досрочный/облегчённый" путь обработки.
        // Accept: application/json ПЕРВЫМ — Overpass отдаёт только application/json
        // и application/osm3s+xml. С "*/*" без преференции некоторые зеркала (особенно
        // mail.ru) отвечают 406 Not Acceptable. С явным application/json первым —
        // 200 OK, подтверждено.
        var headers = _httpClient.DefaultRequestHeaders;
        if (!headers.Contains("User-Agent"))
            headers.TryAddWithoutValidation("User-Agent", "curl/8.4.0");
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
            try
            {
                var formData = new List<KeyValuePair<string, string>>
                {
                    new("data", query)
                };
                using var content = new FormUrlEncodedContent(formData);

                using var response = await _httpClient.PostAsync(url, content, ct);
                if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    response.Dispose();
                    Console.WriteLine($"  {url}: {response.StatusCode}, жду 20с и повторяю...");
                    await Task.Delay(TimeSpan.FromSeconds(20), ct);
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.BadRequest ||
                    response.StatusCode == HttpStatusCode.NotAcceptable)
                {
                    response.Dispose();
                    throw new HttpRequestException(
                        $"{url}: {response.StatusCode} (запрос невалиден для зеркала)");
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStreamAsync(ct);
                var parsed = await JsonSerializer.DeserializeAsync<OverpassResponse>(json, cancellationToken: ct);
                parsed ??= new OverpassResponse();

                if (!string.IsNullOrEmpty(parsed.Remark))
                {
                    throw new InvalidOperationException($"Overpass вернул remark: {parsed.Remark}");
                }

                return parsed;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Console.WriteLine($"  зеркало {url} не ответило ({ex.Message}), пробую следующее...");
            }
        }

        throw new InvalidOperationException("Все зеркала Overpass недоступны.", lastError);
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

        var union = layer.IncludeRelations
            ? $"""
              (
                way{tagFilter}({bboxStr});
                relation{tagFilter}({bboxStr});
              );
              out geom;
              """
            : $"""
              (
                way{tagFilter}({bboxStr});
              );
              out geom;
              """;

        return $"""
                [out:json][timeout:90];
                {union}
                """;
    }
}
