using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OsmToShapefile.Overpass;

/// <summary>
/// Ограничивающий прямоугольник региона: юг, запад, север, восток (градусы, WGS84).
/// Именно в таком порядке Overpass ожидает bbox.
/// </summary>
public readonly record struct BoundingBox(double South, double West, double North, double East)
{
    // InvariantCulture: иначе на машинах с русской/немецкой локалью double.ToString()
    // выдаёт "55,7510" вместо "55.7510", и Overpass видит четыре числа с запятыми-разделителями
    // (55, 7510, 37, 6100) — синтаксически невалидный bbox → 400 Bad Request.
    public string ToOverpassBbox() =>
        FormattableString.Invariant($"{South},{West},{North},{East}");
}

/// <summary>
/// Описание одного тематического слоя, который мы хотим выгрузить с OSM.
/// Key/Value — тег OSM (например highway=* или natural=water).
/// Если Value пустой — берём все объекты с любым значением этого тега.
/// </summary>
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
    // overpass-api.de принимает запросы как application/x-www-form-urlencoded с полем "data".
    // Дополнительные "браузерные" заголовки (Sec-Fetch-*, sec-ch-ua-*, Origin, Referer) раньше
    // вызывали странные серверные ошибки "Query timed out / out of memory" через десятки секунд —
    // проверено, что минимальный curl с тем же bbox отдаёт 200 OK за 2-4 секунды. Оставляем только User-Agent.
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
                // Явно фиксируем TLS 1.2/1.3 — фикс для "SSL connection could not be established"
                // на машинах, где HTTPS-трафик перехватывается антивирусом.
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                | System.Security.Authentication.SslProtocols.Tls13,

                // Сервер может ответить gzip'ом (это подтвердилось в рабочем запросе,
                // Content-Encoding: gzip) — просим и сразу автоматически распаковываем,
                // вместо того чтобы вручную возиться с decompression.
                AutomaticDecompression = DecompressionMethods.GZip
                                          | DecompressionMethods.Deflate
                                          | DecompressionMethods.Brotli
            };

            httpClient = new HttpClient(handler);
        }

        _httpClient = httpClient;
        // 60с вместо 120с: реальные Overpass-запросы укладываются в 2-10с, серверный
        // [timeout:90] ограничивает сверху 90с. 120-секундный клиентский таймаут
        // превращал первую неудачу (например 406 анти-бот) в 2-минутный "hang":
        // пока следующее зеркало отвечает/падает, пользователь видел только
        // "пробую следующее..." без обратной связи.
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

    /// <summary>
    /// Читает уже сохранённый ответ Overpass из локального файла — для офлайн-тестирования
    /// пайплайна без обращения к живому (и часто нестабильному) серверу.
    /// Файл получить просто: выполнить запрос на overpass-turbo.eu, вкладка "Data" -> скопировать
    /// содержимое (это как раз тот же JSON, что отдаёт API), сохранить в sample-data/&lt;layer&gt;.json.
    /// </summary>
    public static async Task<OverpassResponse> LoadFromFileAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var parsed = await JsonSerializer.DeserializeAsync<OverpassResponse>(stream, cancellationToken: ct);
        return parsed ?? new OverpassResponse();
    }

    /// <summary>
    /// Скачивает один слой (например, все "way" с тегом highway) в заданном bbox.
    /// Пробует зеркала по очереди — публичный overpass-api.de нестабилен под нагрузкой.
    /// </summary>
    public async Task<OverpassResponse> FetchLayerAsync(BoundingBox bbox, LayerDefinition layer, CancellationToken ct = default)
    {
        var query = BuildQuery(bbox, layer);
        Exception? lastError = null;

        foreach (var url in MirrorUrls)
        {
            try
            {
                // Ключевое отличие от предыдущей версии: раньше слали сырой текст запроса как
                // text/plain — анти-бот фильтр overpass-api.de это резал 406-й. Настоящая HTML-форма
                // сайта отправляет данные как application/x-www-form-urlencoded с полем "data" —
                // именно так и нужно, подтверждено рабочим запросом через Invoke-WebRequest.
                var formData = new List<KeyValuePair<string, string>>
                {
                    new("data", query)
                };
                using var content = new FormUrlEncodedContent(formData);

                using var response = await _httpClient.PostAsync(url, content, ct);

                // 429 (Too Many Requests) и 503 (temp unavailable, please retry later) — это rate limit, не
                // сбой зеркала. Прыгать на следующее зеркало бесполезно (оно тоже упрётся в
                // лимит), лучше подождать и повторить тот же URL.
                if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.ServiceUnavailable)
                {
                    response.Dispose();
                    Console.WriteLine($"  {url}: {response.StatusCode}, жду 20с и повторяю...");
                    await Task.Delay(TimeSpan.FromSeconds(20), ct);
                    continue;
                }

                // 400/406 — наш запрос невалиден для этого зеркала (анти-бот, формат, etc.).
                // Повторять тот же URL бессмысленно, сразу к следующему зеркалу, иначе висим
                // по 60 секунд × N зеркал на каждом слое.
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
                    // Сервер ответил 200, но по сути отказал (таймаут/лимит) — это не "нет данных",
                    // а сбой конкретного запроса. Пробуем следующее зеркало.
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
            // Составной фильтр: key=val1|val2|... (Overpass QL регулярное выражение).
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

        // [out:json][timeout:90]; way — основная сущность для геометрии,
        // out geom сразу отдаёт координаты точек вдоль way (без отдельного запроса на ноды).
        return $"""
                [out:json][timeout:90];
                {union}
                """;
    }
}
