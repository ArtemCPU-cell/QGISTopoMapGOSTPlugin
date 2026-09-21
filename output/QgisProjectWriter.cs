using System.Globalization;
using System.Text;

namespace OsmToShapefile.Output;

/// <summary>
/// Пишет минимальный QGIS 3.x проект .qgs. Содержит project CRS (с полным WKT),
/// canvas с extent в метрах целевой SRS, и все слои со ссылками на .shp + дерево
/// слоёв. .sld-стили пользователь подключает вручную через Layer Properties →
/// Symbology → Load Style.
///
/// Внимание: формат .qgs — негласный, версионно-зависимый, и легко разъезжается
/// при ручной генерации. На длинной дистанции разумнее либо не генерировать .qgs
/// вовсе (открывать shapefile напрямую в QGIS), либо собирать его через PyQGIS
/// (QgsProject + QgsVectorLayer) — там QGIS сам пишет корректный XML.
/// </summary>
public static class QgisProjectWriter
{
    private const string MaplayerAttrs =
        "type=\"vector\" hasScaleBasedVisibilityFlag=\"0\" styleCategories=\"AllStyleCategories\" " +
        "refreshOnNotifyEnabled=\"0\" autoRefreshEnabled=\"0\" symbologyReferenceScale=\"-1\" " +
        "simplifyDrawingTol=\"1\" simplifyMaxScale=\"1\" simplifyDrawingHints=\"0\" " +
        "simplifyLocal=\"1\" readOnly=\"0\" legendPlaceholderImage=\"\"";

    public static void Write(string outputDir, string projectName, string targetSrs, string targetWkt,
        (double Xmin, double Ymin, double Xmax, double Ymax)? projectedExtent = null)
    {
        var path = Path.Combine(outputDir, $"{projectName}.qgs");
        var zone = ZoneFromEpsg(targetSrs);
        var (srsid, srid) = EpsgIdPair(targetSrs);

        // extent уже в метрах целевой SRS (после репроекции WGS84 → UTM в Program.cs).
        // Без bbox — нули, QGIS откроет, но слои могут оказаться "за краем".
        string xmin, ymin, xmax, ymax;
        if (projectedExtent.HasValue)
        {
            xmin = projectedExtent.Value.Xmin.ToString(Culture);
            ymin = projectedExtent.Value.Ymin.ToString(Culture);
            xmax = projectedExtent.Value.Xmax.ToString(Culture);
            ymax = projectedExtent.Value.Ymax.ToString(Culture);
        }
        else
        {
            xmin = ymin = xmax = ymax = "0";
        }

        // Собираем инфу о слоях один раз — нужна и для <maplayer>, и для <layer-tree-layer>
        // (id должен совпадать, чтобы QGIS связал дерево с проектом).
        var layers = Directory.EnumerateFiles(outputDir, "*.shp").OrderBy(p => p).Select(shp =>
        {
            var name = Path.GetFileNameWithoutExtension(shp);
            return new LayerInfo(
                Id: $"{name}_{Guid.NewGuid():N}",
                Name: name,
                File: Path.GetFileName(shp),
                WkbType: ReadShpWkbType(shp));
        }).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<qgis projectname=\"" + Esc(projectName) + "\" version=\"3.44.0-Solothurn\">");
        sb.AppendLine("  <homePath path=\"./\"/>");
        sb.AppendLine($"  <title>{Esc(projectName)}</title>");
        sb.AppendLine("  <autotransaction active=\"0\"/>");
        sb.AppendLine("  <evaluateDefaultValues active=\"0\"/>");
        sb.AppendLine("  <trust layerMetadata=\"0\"/>");
        sb.AppendLine("  <projectCrs>");
        sb.AppendLine("    <spatialrefsys>");
        sb.AppendLine($"      <wkt>{Esc(targetWkt)}</wkt>");
        sb.AppendLine($"      <proj4>+proj=utm +zone={zone} +datum=WGS84 +units=m +no_defs</proj4>");
        sb.AppendLine($"      <srsid>{srsid}</srsid>");
        sb.AppendLine($"      <srid>{srid}</srid>");
        sb.AppendLine($"      <authid>{Esc(targetSrs)}</authid>");
        sb.AppendLine("    </spatialrefsys>");
        sb.AppendLine("  </projectCrs>");
        sb.AppendLine("  <layer-tree-group>");
        sb.AppendLine("    <custom-order enabled=\"0\"/>");
        foreach (var l in layers)
        {
            sb.AppendLine($"    <layer-tree-layer id=\"{l.Id}\" name=\"{Esc(l.Name)}\" checked=\"Qt::Checked\" expanded=\"1\" providerKey=\"ogr\" source=\"./{Esc(l.File)}\"/>");
        }
        sb.AppendLine("  </layer-tree-group>");
        sb.AppendLine("  <snapping-settings>");
        sb.AppendLine("    <individual-layer-settings/>");
        sb.AppendLine("    <type>0</type>");
        sb.AppendLine("    <mode>0</mode>");
        sb.AppendLine("    <tolerance>0</tolerance>");
        sb.AppendLine("    <unit>0</unit>");
        sb.AppendLine("  </snapping-settings>");
        sb.AppendLine("  <relations/>");
        sb.AppendLine("  <mapcanvas>");
        sb.AppendLine("    <extent>");
        sb.AppendLine($"      <xmin>{xmin}</xmin>");
        sb.AppendLine($"      <ymin>{ymin}</ymin>");
        sb.AppendLine($"      <xmax>{xmax}</xmax>");
        sb.AppendLine($"      <ymax>{ymax}</ymax>");
        sb.AppendLine("    </extent>");
        sb.AppendLine("  </mapcanvas>");
        sb.AppendLine("  <projectModels/>");
        sb.AppendLine("  <legend/>");
        sb.AppendLine("  <mapViewDocks/>");
        sb.AppendLine("  <mapViewDocks3D/>");
        sb.AppendLine("  <projectlayers>");

        foreach (var l in layers)
        {
            sb.AppendLine($"    <maplayer {MaplayerAttrs}>");
            sb.AppendLine($"      <id>{l.Id}</id>");
            sb.AppendLine($"      <datasource>./{Esc(l.File)}</datasource>");
            sb.AppendLine($"      <layername>{Esc(l.Name)}</layername>");
            sb.AppendLine("      <srs>");
            sb.AppendLine("        <spatialrefsys>");
            sb.AppendLine($"          <wkt>{Esc(targetWkt)}</wkt>");
            sb.AppendLine($"          <proj4>+proj=utm +zone={zone} +datum=WGS84 +units=m +no_defs</proj4>");
            sb.AppendLine($"          <srsid>{srsid}</srsid>");
            sb.AppendLine($"          <srid>{srid}</srid>");
            sb.AppendLine($"          <authid>{Esc(targetSrs)}</authid>");
            sb.AppendLine("        </spatialrefsys>");
            sb.AppendLine("      </srs>");
            sb.AppendLine($"      <wkbType>{l.WkbType}</wkbType>");
            sb.AppendLine("      <resource_type>layer</resource_type>");
            sb.AppendLine("      <provider encoding=\"System\">ogr</provider>");
            sb.AppendLine("      <vectorjoins/>");
            sb.AppendLine("      <layerDependencies/>");
            sb.AppendLine("      <dataDependencies/>");
            sb.AppendLine("      <expressionfields/>");
            sb.AppendLine("      <map-layer-style-manager>");
            sb.AppendLine("        <map-layer-style name=\"default\"/>");
            sb.AppendLine("      </map-layer-style-manager>");
            sb.AppendLine("      <blendMode>0</blendMode>");
            sb.AppendLine("      <opacity>1</opacity>");
            sb.AppendLine("    </maplayer>");
        }
        sb.AppendLine("  </projectlayers>");
        sb.AppendLine("  <layerorder/>");
        sb.AppendLine("  <properties>");
        sb.AppendLine("    <Paths>");
        sb.AppendLine("      <Absolute type=\"bool\">false</Absolute>");
        sb.AppendLine("    </Paths>");
        sb.AppendLine("  </properties>");
        sb.AppendLine("</qgis>");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"QGIS-проект -> {path}");

        var sldCount = Directory.EnumerateFiles(outputDir, "*.sld").Count();
        if (sldCount > 0)
            Console.WriteLine($"Подсказка: загрузите .sld вручную через Layer Properties → Symbology → Load Style для каждого из {sldCount} слоёв.");
    }

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Читает shape type из заголовка .shp (LE int32 по смещению 32) и маппит в QGIS wkbType.
    /// ESRI types: 1=Point, 3=PolyLine, 5=Polygon → QGIS: 1, 2, 3. Иное → 0 (unknown, QGIS автоопределит).
    /// </summary>
    private static int ReadShpWkbType(string shpPath)
    {
        try
        {
            using var fs = File.OpenRead(shpPath);
            if (fs.Length < 36) return 0;
            fs.Seek(32, SeekOrigin.Begin);
            var buf = new byte[4];
            if (fs.Read(buf, 0, 4) < 4) return 0;
            var shapeType = BitConverter.ToInt32(buf, 0);
            return shapeType switch
            {
                1 => 1,
                3 => 2,
                5 => 3,
                _ => 0,
            };
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// srsid в QGIS — autoincrement-ID из внутренней SQLite-БД (srs.db), который зависит
    /// от установки и недостоверно известен без QGIS под рукой. Ставим оба поля равными
    /// EPSG-коду: QGIS 3.44 при несовпадении srsid тихо создаст запись в user-CRS,
    /// но проект откроется без диалога "выберите CRS".
    /// </summary>
    private static (int Srsid, int Srid) EpsgIdPair(string targetSrs)
    {
        var s = targetSrs.ToUpperInvariant().Replace("EPSG:", "");
        if (int.TryParse(s, out var code))
            return (code, code);
        return (0, 0);
    }

    private static int ZoneFromEpsg(string epsg)
    {
        var s = epsg.ToUpperInvariant().Replace("EPSG:", "");
        if (!int.TryParse(s, out var code)) return 37;
        if (code >= 32601 && code <= 32660) return code - 32600;
        if (code >= 32701 && code <= 32760) return code - 32700;
        return 37;
    }

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s);

    private sealed record LayerInfo(string Id, string Name, string File, int WkbType);
}