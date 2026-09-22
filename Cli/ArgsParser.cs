using OsmToShapefile.Overpass;
using OsmToShapefile.Scale;
using OsmToShapefile.Contracts;

namespace OsmToShapefile.Cli;

public static class ArgsParser
{
    public static RunOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Any(a => a is "--help" or "-h" or "/?"))
        {
            Console.WriteLine(RunOptions.Usage);
            Environment.Exit(0);
        }

        string? bbox = null;
        string? scale = null;
        string output = Path.Combine(Directory.GetCurrentDirectory(), "output");
        string srs = "EPSG:32637";
        bool offline = false;
        bool noDem = false;
        bool qgis = false;
        string? requestPath = null;
        string? responsePath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--bbox":
                    bbox = Require(args, ref i, "--bbox");
                    break;
                case "--scale":
                    scale = Require(args, ref i, "--scale");
                    break;
                case "--output":
                    output = Require(args, ref i, "--output");
                    break;
                case "--srs":
                    srs = Require(args, ref i, "--srs");
                    break;
                case "--offline":
                    offline = true;
                    break;
                case "--no-dem":
                    noDem = true;
                    break;
                case "--qgis-project":
                    qgis = true;
                    break;
                case "--request":
                    requestPath = Require(args, ref i, "--request");
                    break;
                case "--response":
                    responsePath = Require(args, ref i, "--response");
                    break;
                default:
                    throw new ArgumentException($"Неизвестный аргумент: {args[i]}\n{RunOptions.Usage}");
            }
        }

        if (requestPath is not null)
        {
            if (args.Any(a => a is "--bbox" or "--scale" or "--output" or "--srs" or
                              "--offline" or "--no-dem" or "--qgis-project"))
            {
                throw new ArgumentException("--request cannot be combined with map generation options.");
            }

            return GenerateTopographicMapRequest.Load(requestPath).ToRunOptions(responsePath);
        }

        if (bbox is null)
            throw new ArgumentException($"Не задан --bbox.\n{RunOptions.Usage}");
        if (scale is null)
            throw new ArgumentException($"Не задан --scale.\n{RunOptions.Usage}");

        var parts = bbox.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
            throw new ArgumentException($"--bbox ожидает четыре числа через запятую (S,W,N,E), получил: '{bbox}'");

        var bb = new BoundingBox(
            South: double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            West:  double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
            North: double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
            East:  double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture));

        if (bb.South >= bb.North)
            throw new ArgumentException($"South ({bb.South}) должна быть меньше North ({bb.North})");
        if (bb.West >= bb.East)
            throw new ArgumentException($"West ({bb.West}) должна быть меньше East ({bb.East})");

        return new RunOptions(bb, ScaleProfile.Parse(scale), output, srs, offline, noDem, qgis, responsePath);
    }

    private static string Require(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"После {flag} ожидается значение");
        return args[++i];
    }
}
