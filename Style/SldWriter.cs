using System.Globalization;
using System.Text;

namespace OsmToShapefile.Style;

public static class SldWriter
{
    public static void Write(string outputDir, string layerName, SldStyle style)
    {
        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, $"{layerName}.sld");
        File.WriteAllText(path, Render(layerName, style), new UTF8Encoding(false));
    }

    public static string Render(string layerName, SldStyle style)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<sld:StyledLayerDescriptor xmlns:sld=\"http://www.opengis.net/sld/1.1/0\"");
        sb.AppendLine("                       xmlns:ogc=\"http://www.opengis.net/ogc\"");
        sb.AppendLine("                       version=\"1.1.0\">");
        sb.AppendLine("  <sld:NamedLayer>");
        sb.AppendLine($"    <sld:Name>{Esc(layerName)}</sld:Name>");
        sb.AppendLine("    <sld:UserStyle>");
        sb.AppendLine("      <sld:Name>default</sld:Name>");
        sb.AppendLine("      <sld:FeatureTypeStyle>");
        foreach (var rule in style.Rules)
        {
            AppendRule(sb, rule);
        }
        sb.AppendLine("      </sld:FeatureTypeStyle>");
        sb.AppendLine("    </sld:UserStyle>");
        sb.AppendLine("  </sld:NamedLayer>");
        sb.AppendLine("</sld:StyledLayerDescriptor>");
        return sb.ToString();
    }

    private static void AppendRule(StringBuilder sb, SldRule rule)
    {
        sb.AppendLine("        <sld:Rule>");
        if (!string.IsNullOrEmpty(rule.Filter))
        {
            sb.AppendLine($"          <ogc:Filter>{rule.Filter}</ogc:Filter>");
        }
        sb.AppendLine($"          <sld:Name>{Esc(rule.Name)}</sld:Name>");

        if (rule.StrokeWidthMm is not null || rule.StrokeColor is not null || rule.FillColor is not null)
        {
            sb.AppendLine("          <sld:LineSymbolizer>");
            sb.AppendLine("            <sld:Stroke>");
            if (rule.StrokeColor is not null)
                sb.AppendLine($"              <sld:CssParameter name=\"stroke\">{Esc(rule.StrokeColor)}</sld:CssParameter>");
            if (rule.StrokeWidthMm is not null)
                sb.AppendLine($"              <sld:CssParameter name=\"stroke-width\">{Format(rule.StrokeWidthMm.Value)}</sld:CssParameter>");
            if (rule.StrokeDash is not null)
                sb.AppendLine($"              <sld:CssParameter name=\"stroke-dasharray\">{Esc(rule.StrokeDash)}</sld:CssParameter>");
            sb.AppendLine("            </sld:Stroke>");
            sb.AppendLine("          </sld:LineSymbolizer>");
        }

        if (rule.FillColor is not null && rule.IsPolygon)
        {
            sb.AppendLine("          <sld:PolygonSymbolizer>");
            sb.AppendLine("            <sld:Fill>");
            sb.AppendLine($"              <sld:CssParameter name=\"fill\">{Esc(rule.FillColor)}</sld:CssParameter>");
            sb.AppendLine($"              <sld:CssParameter name=\"fill-opacity\">{(rule.FillOpacity ?? 0.7).ToString(CultureInfo.InvariantCulture)}</sld:CssParameter>");
            sb.AppendLine("            </sld:Fill>");
            if (rule.StrokeColor is not null)
            {
                sb.AppendLine("            <sld:Stroke>");
                sb.AppendLine($"              <sld:CssParameter name=\"stroke\">{Esc(rule.StrokeColor)}</sld:CssParameter>");
                sb.AppendLine($"              <sld:CssParameter name=\"stroke-width\">{(rule.StrokeWidthMm ?? 0.1).ToString(CultureInfo.InvariantCulture)}</sld:CssParameter>");
                sb.AppendLine("            </sld:Stroke>");
            }
            sb.AppendLine("          </sld:PolygonSymbolizer>");
        }

        if (rule.LabelField is not null)
        {
            sb.AppendLine("          <sld:TextSymbolizer>");
            sb.AppendLine("            <sld:Label>");
            sb.AppendLine($"              <ogc:PropertyName>{Esc(rule.LabelField)}</ogc:PropertyName>");
            sb.AppendLine("            </sld:Label>");
            sb.AppendLine("            <sld:Font>");
            sb.AppendLine("              <sld:CssParameter name=\"font-family\">DejaVu Sans</sld:CssParameter>");
            sb.AppendLine($"              <sld:CssParameter name=\"font-size\">{Format(rule.LabelFontSize ?? 8)}</sld:CssParameter>");
            sb.AppendLine("            </sld:Font>");
            sb.AppendLine("            <sld:Fill>");
            sb.AppendLine($"              <sld:CssParameter name=\"fill\">{Esc(rule.LabelColor ?? "#000000")}</sld:CssParameter>");
            sb.AppendLine("            </sld:Fill>");
            sb.AppendLine("          </sld:TextSymbolizer>");
        }
        sb.AppendLine("        </sld:Rule>");
    }

    private static string Format(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    private static string Esc(string s) => System.Security.SecurityElement.Escape(s);
}

public sealed record SldStyle(IReadOnlyList<SldRule> Rules);

public sealed record SldRule(
    string Name,
    string? Filter,
    string? StrokeColor,
    double? StrokeWidthMm,
    string? StrokeDash,
    string? FillColor,
    double? FillOpacity,
    bool IsPolygon,
    string? LabelField = null,
    double? LabelFontSize = null,
    string? LabelColor = null);