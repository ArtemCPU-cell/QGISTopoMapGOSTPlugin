using System.Buffers.Binary;
using OsmToShapefile.Overpass;

namespace OsmToShapefile.Dem;

/// <summary>
/// Minimal, dependency-free reader for a single-band, uncompressed GeoTIFF DTM.
/// It intentionally accepts only EPSG:4326 terrain rasters so a source with an
/// ambiguous or unsupported coordinate system cannot silently create bad contours.
/// Projected GeoTIFF and compressed/tiled rasters are a planned Infrastructure
/// extension, not an implicit fallback.
/// </summary>
public static class GeoTiffDtmSource
{
    private const ushort TagImageWidth = 256;
    private const ushort TagImageLength = 257;
    private const ushort TagBitsPerSample = 258;
    private const ushort TagCompression = 259;
    private const ushort TagStripOffsets = 273;
    private const ushort TagSamplesPerPixel = 277;
    private const ushort TagRowsPerStrip = 278;
    private const ushort TagStripByteCounts = 279;
    private const ushort TagSampleFormat = 339;
    private const ushort TagModelPixelScale = 33550;
    private const ushort TagModelTiePoint = 33922;
    private const ushort TagGeoKeyDirectory = 34735;

    public static DemGrid Load(string path, BoundingBox requestedBbox)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("GeoTIFF DTM was not found.", path);

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        var byteOrder = ReadByteOrder(reader);
        var firstIfdOffset = ReadUInt32(reader, byteOrder);
        if (firstIfdOffset == 0)
            throw new InvalidDataException("GeoTIFF has no image file directory.");

        var tags = ReadTags(reader, byteOrder, firstIfdOffset, path);
        var width = GetUInt(tags, TagImageWidth, byteOrder);
        var height = GetUInt(tags, TagImageLength, byteOrder);
        var bitsPerSample = GetUInt(tags, TagBitsPerSample, byteOrder);
        var compression = GetUInt(tags, TagCompression, byteOrder, 1);
        var samplesPerPixel = GetUInt(tags, TagSamplesPerPixel, byteOrder, 1);
        var sampleFormat = GetUInt(tags, TagSampleFormat, byteOrder, 1);
        var rowsPerStrip = GetUInt(tags, TagRowsPerStrip, byteOrder, height);

        if (width < 2 || height < 2)
            throw new InvalidDataException("GeoTIFF DTM must have at least 2 rows and 2 columns.");
        if (compression != 1)
            throw new NotSupportedException("Only uncompressed GeoTIFF DTM is currently supported.");
        if (samplesPerPixel != 1)
            throw new NotSupportedException("Only single-band GeoTIFF DTM is currently supported.");
        if (bitsPerSample is not (16 or 32 or 64))
            throw new NotSupportedException($"Unsupported GeoTIFF bits per sample: {bitsPerSample}.");
        if (sampleFormat is < 1 or > 3)
            throw new NotSupportedException($"Unsupported GeoTIFF sample format: {sampleFormat}.");

        var epsg = ReadEpsg(tags, byteOrder);
        if (epsg != 4326)
            throw new NotSupportedException(
                $"GeoTIFF CRS must be EPSG:4326 in this engine version; found EPSG:{epsg?.ToString() ?? "unknown"}.");

        var scale = GetDoubles(tags, TagModelPixelScale, byteOrder);
        var tiePoint = GetDoubles(tags, TagModelTiePoint, byteOrder);
        if (scale.Length < 2 || tiePoint.Length < 6 || scale[0] <= 0 || scale[1] <= 0)
            throw new InvalidDataException("GeoTIFF must contain valid ModelPixelScale and ModelTiepoint tags.");

        var lons = Enumerable.Range(0, checked((int)width))
            .Select(column => tiePoint[3] + column * scale[0])
            .ToArray();
        var sourceLats = Enumerable.Range(0, checked((int)height))
            .Select(row => tiePoint[4] - row * scale[1])
            .ToArray();

        ValidateCoverage(requestedBbox, lons, sourceLats);
        var sourceHeights = ReadRaster(reader, byteOrder, tags, width, height, rowsPerStrip, bitsPerSample, sampleFormat);

        // GeoTIFF north-up rows descend in latitude. DemGrid requires rows ordered
        // from south to north because ContourGenerator walks consecutive rows.
        var lats = sourceLats.Reverse().ToArray();
        var heights = new double[sourceHeights.Length];
        for (var sourceRow = 0; sourceRow < height; sourceRow++)
        {
            var targetRow = checked((int)height - 1 - sourceRow);
            Array.Copy(sourceHeights, checked((int)(sourceRow * width)), heights,
                checked((int)(targetRow * width)), checked((int)width));
        }

        return new DemGrid(lats, lons, heights);
    }

    private static double[] ReadRaster(
        BinaryReader reader,
        bool littleEndian,
        IReadOnlyDictionary<ushort, TiffField> tags,
        uint width,
        uint height,
        uint rowsPerStrip,
        uint bitsPerSample,
        uint sampleFormat)
    {
        var stripOffsets = GetUInts(tags, TagStripOffsets, littleEndian);
        var stripByteCounts = GetUInts(tags, TagStripByteCounts, littleEndian);
        if (stripOffsets.Length == 0 || stripOffsets.Length != stripByteCounts.Length)
            throw new InvalidDataException("GeoTIFF strip offsets/byte counts are missing or inconsistent.");

        var bytesPerSample = checked((int)(bitsPerSample / 8));
        var values = new double[checked((int)(width * height))];
        uint row = 0;
        for (var strip = 0; strip < stripOffsets.Length && row < height; strip++)
        {
            reader.BaseStream.Seek(stripOffsets[strip], SeekOrigin.Begin);
            var data = reader.ReadBytes(checked((int)stripByteCounts[strip]));
            var stripRows = Math.Min(rowsPerStrip, height - row);
            var requiredBytes = checked((int)(stripRows * width * (uint)bytesPerSample));
            if (data.Length < requiredBytes)
                throw new InvalidDataException("GeoTIFF strip is shorter than the declared raster dimensions.");

            for (var index = 0; index < stripRows * width; index++)
            {
                var offset = index * bytesPerSample;
                values[checked((int)(row * width) + index)] = ReadSample(data.AsSpan(offset, bytesPerSample), littleEndian,
                    bitsPerSample, sampleFormat);
            }
            row += stripRows;
        }

        if (row != height)
            throw new InvalidDataException("GeoTIFF does not contain every declared raster row.");
        return values;
    }

    private static double ReadSample(ReadOnlySpan<byte> bytes, bool littleEndian, uint bits, uint format)
    {
        if (bits == 16)
        {
            var value = littleEndian
                ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
                : BinaryPrimitives.ReadUInt16BigEndian(bytes);
            return format == 2 ? unchecked((short)value) : value;
        }

        if (bits == 32)
        {
            var value = littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
                : BinaryPrimitives.ReadUInt32BigEndian(bytes);
            return format switch
            {
                2 => unchecked((int)value),
                3 => BitConverter.Int32BitsToSingle(unchecked((int)value)),
                _ => value,
            };
        }

        var raw = littleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt64BigEndian(bytes);
        return format == 3 ? BitConverter.Int64BitsToDouble(unchecked((long)raw)) : raw;
    }

    private static void ValidateCoverage(BoundingBox bbox, double[] lons, double[] lats)
    {
        var west = lons.Min();
        var east = lons.Max();
        var south = lats.Min();
        var north = lats.Max();
        const double tolerance = 1e-10;
        if (bbox.West < west - tolerance || bbox.East > east + tolerance ||
            bbox.South < south - tolerance || bbox.North > north + tolerance)
        {
            throw new ArgumentException(
                $"GeoTIFF coverage ({south:R},{west:R},{north:R},{east:R}) does not cover requested bbox ({bbox.ToOverpassBbox()}).");
        }
    }

    private static int? ReadEpsg(IReadOnlyDictionary<ushort, TiffField> tags, bool littleEndian)
    {
        var keys = GetUShorts(tags, TagGeoKeyDirectory, littleEndian);
        if (keys.Length < 4)
            return null;

        var keyCount = keys[3];
        for (var i = 0; i < keyCount; i++)
        {
            var offset = 4 + i * 4;
            if (offset + 3 >= keys.Length)
                break;
            var keyId = keys[offset];
            var location = keys[offset + 1];
            var count = keys[offset + 2];
            var value = keys[offset + 3];
            if (count == 1 && location == 0 && keyId is 2048 or 3072)
                return value;
        }
        return null;
    }

    private static bool ReadByteOrder(BinaryReader reader)
    {
        var order = reader.ReadBytes(2);
        if (order.SequenceEqual("II"u8.ToArray()))
        {
            if (ReadUInt16(reader, true) != 42)
                throw new InvalidDataException("Invalid TIFF magic number.");
            return true;
        }
        if (order.SequenceEqual("MM"u8.ToArray()))
        {
            if (ReadUInt16(reader, false) != 42)
                throw new InvalidDataException("Invalid TIFF magic number.");
            return false;
        }
        throw new InvalidDataException("File is not a TIFF stream.");
    }

    private static Dictionary<ushort, TiffField> ReadTags(
        BinaryReader reader, bool littleEndian, uint offset, string sourcePath)
    {
        reader.BaseStream.Seek(offset, SeekOrigin.Begin);
        var count = ReadUInt16(reader, littleEndian);
        var result = new Dictionary<ushort, TiffField>();
        for (var i = 0; i < count; i++)
        {
            var tag = ReadUInt16(reader, littleEndian);
            var type = ReadUInt16(reader, littleEndian);
            var valueCount = ReadUInt32(reader, littleEndian);
            var rawValue = reader.ReadBytes(4);
            result[tag] = new TiffField(type, valueCount, rawValue) { SourcePath = sourcePath };
        }
        return result;
    }

    private static uint GetUInt(IReadOnlyDictionary<ushort, TiffField> tags, ushort tag, bool littleEndian, uint? defaultValue = null)
    {
        if (!tags.TryGetValue(tag, out var field))
        {
            if (defaultValue.HasValue) return defaultValue.Value;
            throw new InvalidDataException($"GeoTIFF required tag {tag} is missing.");
        }
        return GetUInts(tags, tag, littleEndian)[0];
    }

    private static uint[] GetUInts(IReadOnlyDictionary<ushort, TiffField> tags, ushort tag, bool littleEndian)
    {
        var bytes = GetFieldBytes(tags, tag, littleEndian);
        var field = tags[tag];
        return field.Type switch
        {
            3 => Enumerable.Range(0, checked((int)field.Count))
                .Select(i => (uint)ReadUInt16(bytes.AsSpan(i * 2, 2), littleEndian)).ToArray(),
            4 => Enumerable.Range(0, checked((int)field.Count))
                .Select(i => ReadUInt32(bytes.AsSpan(i * 4, 4), littleEndian)).ToArray(),
            _ => throw new NotSupportedException($"GeoTIFF tag {tag} has unsupported integer TIFF type {field.Type}."),
        };
    }

    private static ushort[] GetUShorts(IReadOnlyDictionary<ushort, TiffField> tags, ushort tag, bool littleEndian)
    {
        var bytes = GetFieldBytes(tags, tag, littleEndian);
        var field = tags[tag];
        if (field.Type != 3)
            throw new NotSupportedException($"GeoTIFF tag {tag} must use TIFF SHORT values.");
        return Enumerable.Range(0, checked((int)field.Count))
            .Select(i => ReadUInt16(bytes.AsSpan(i * 2, 2), littleEndian)).ToArray();
    }

    private static double[] GetDoubles(IReadOnlyDictionary<ushort, TiffField> tags, ushort tag, bool littleEndian)
    {
        var bytes = GetFieldBytes(tags, tag, littleEndian);
        var field = tags[tag];
        if (field.Type != 12)
            throw new NotSupportedException($"GeoTIFF tag {tag} must use TIFF DOUBLE values.");
        return Enumerable.Range(0, checked((int)field.Count))
            .Select(i => ReadDouble(bytes.AsSpan(i * 8, 8), littleEndian)).ToArray();
    }

    private static byte[] GetFieldBytes(IReadOnlyDictionary<ushort, TiffField> tags, ushort tag, bool littleEndian)
    {
        if (!tags.TryGetValue(tag, out var field))
            throw new InvalidDataException($"GeoTIFF required tag {tag} is missing.");

        var byteCount = checked((int)(field.Count * TypeSize(field.Type)));
        if (byteCount <= 4)
            return field.RawValue[..byteCount];

        var offset = ReadUInt32(field.RawValue, littleEndian);
        using var stream = File.OpenRead(field.SourcePath ?? throw new InvalidOperationException("TIFF source path is unavailable."));
        stream.Seek(offset, SeekOrigin.Begin);
        var data = new byte[byteCount];
        if (stream.Read(data) != data.Length)
            throw new InvalidDataException($"GeoTIFF tag {tag} is truncated.");
        return data;
    }

    private static int TypeSize(ushort type) => type switch
    {
        1 or 2 => 1,
        3 => 2,
        4 or 9 or 11 => 4,
        5 or 10 or 12 => 8,
        _ => throw new NotSupportedException($"Unsupported TIFF field type {type}."),
    };

    private static ushort ReadUInt16(BinaryReader reader, bool littleEndian) => ReadUInt16(reader.ReadBytes(2), littleEndian);
    private static uint ReadUInt32(BinaryReader reader, bool littleEndian) => ReadUInt32(reader.ReadBytes(4), littleEndian);
    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);
    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian) => littleEndian
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
    private static double ReadDouble(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        var raw = littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes);
        return BitConverter.Int64BitsToDouble(unchecked((long)raw));
    }

    private sealed record TiffField(ushort Type, uint Count, byte[] RawValue)
    {
        public string? SourcePath { get; init; }
    }
}
