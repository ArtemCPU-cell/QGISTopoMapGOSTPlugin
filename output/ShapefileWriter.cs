using System.Globalization;
using System.Text;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace OsmToShapefile.Output;

/// <summary>
/// Пишет шейпфайл (.shp/.shx/.dbf) плюс sidecar:
///   - .prj — WKT целевой метрической SRS
///   - .cpg — UTF-8 (QGIS читает и переключает кодировку DBF)
///
/// Кириллица в DBF: NTS 2.1.0 фиксирует кодировку внутри ShapefileDataWriter на
/// Latin1. Обход — запись SHP+SHX через ShapefileDataWriter (геометрия), DBF через
/// DbaseFileWriter(string, Encoding) напрямую с UTF-8.
/// </summary>
public static class ShapefileWriter
{
    public static void WriteLayer(string outputDir, string layerName, List<Feature> features,
        string targetWkt)
    {
        if (features.Count == 0)
        {
            Console.WriteLine($"[{layerName}] нет объектов — файл не создаётся.");
            return;
        }

        Directory.CreateDirectory(outputDir);
        var shpPath = Path.Combine(outputDir, $"{layerName}.shp");
        var dbfPath = Path.Combine(outputDir, $"{layerName}.dbf");
        var prjPath = Path.Combine(outputDir, $"{layerName}.prj");
        var cpgPath = Path.Combine(outputDir, $"{layerName}.cpg");

        var culture = CultureInfo.InvariantCulture;

        // 1) Строим header DBF по схеме атрибутов первой фичи.
        var header = BuildDbaseHeader(features[0], Encoding.UTF8);

        // 2) Пишем SHP+SHX через NTS — геометрия и индекс.
        var shpHeader = ShapefileDataWriter.GetHeader(features[0], features.Count);
        var shpWriter = new ShapefileDataWriter(shpPath, GeometryFactory.Default) { Header = shpHeader };
        shpWriter.Write(features.Cast<IFeature>().ToList());

        // 3) Пишем DBF через DbaseFileWriter с UTF-8 — кириллица читается в QGIS.
        using (var dbfWriter = new DbaseFileWriter(dbfPath, Encoding.UTF8))
        {
            dbfWriter.Write(header);
            foreach (var f in features)
                dbfWriter.Write(BuildDbaseRecord(f, header));
        }

        File.WriteAllText(prjPath, targetWkt, new UTF8Encoding(false));
        File.WriteAllText(cpgPath, "UTF-8", new UTF8Encoding(false));

        Console.WriteLine($"[{layerName}] записано {features.Count} объектов -> {shpPath} (+.prj/.cpg, DBF=UTF-8)");
    }

    private static DbaseFileHeader BuildDbaseHeader(Feature template, Encoding encoding)
    {
        var header = new DbaseFileHeader { Encoding = encoding };
        var table = (AttributesTable)template.Attributes;
        foreach (var key in table.GetNames())
        {
            var name = Trunc(key, 10);
            var type = InferDbaseType(table[key]);
            var length = type switch { 'N' => 18, _ => 254 };
            var dec = type == 'N' ? 6 : 0;
            header.AddColumn(name, type, length, dec);
        }
        return header;
    }

    private static object[] BuildDbaseRecord(Feature f, DbaseFileHeader header)
    {
        var table = (AttributesTable)f.Attributes;
        var row = new object[header.NumFields];
        for (var i = 0; i < header.NumFields; i++)
        {
            var field = header.Fields[i];
            var key = field.Name;
            if (!table.Exists(key))
            {
                row[i] = field.DbaseType == 'N' ? (object)0d : string.Empty;
                continue;
            }
            var v = table[key];
            row[i] = field.DbaseType == 'N' && v is not null
                ? Convert.ToDouble(v, CultureInfo.InvariantCulture)
                : v?.ToString() ?? string.Empty;
        }
        return row;
    }

    private static char InferDbaseType(object? value)
    {
        if (value is null) return 'C';
        return value switch
        {
            int or long or short or byte or double or float or decimal => 'N',
            _ => 'C',
        };
    }

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : s.Substring(0, max);
}