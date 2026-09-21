using System.Text;

namespace OsmToShapefile.Overpass;

/// <summary>
/// Снимок реального HTTP-запроса, который уходит на провод: URI, HTTP-версия,
/// все заголовки в порядке отправки и точное тело. Используется, чтобы
/// сравнить wire-байты C#-клиента и PowerShell Invoke-WebRequest.
/// </summary>
internal sealed class LoggingHandler : DelegatingHandler
{
    private readonly string _label;

    public LoggingHandler(string label, HttpMessageHandler inner) : base(inner)
    {
        _label = label;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"--- {_label} ---");
        sb.AppendLine($"{request.Method} {request.RequestUri} HTTP/{request.Version}");
        foreach (var h in request.Headers)
        {
            // Каждое значение коллекции — отдельный токен HTTP. Если запятые внутри значения,
            // они попадут на провод дословно, и сервер увидит невалидный заголовок.
            var raw = string.Join(",", h.Value);
            sb.AppendLine($"{h.Key}: [{string.Join(" | ", h.Value)}] (joined: {raw})");
        }
        if (request.Content is not null)
        {
            foreach (var h in request.Content.Headers)
                sb.AppendLine($"{h.Key}: [{string.Join(" | ", h.Value)}]");
            var body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            sb.AppendLine($"Body ({body.Length} bytes):");
            sb.AppendLine(Encoding.UTF8.GetString(body));
        }

        Console.WriteLine(sb.ToString());
        Console.WriteLine();

        return await base.SendAsync(request, cancellationToken);
    }
}