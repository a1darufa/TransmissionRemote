using System.Buffers.Binary;
using System.IO.Compression;

namespace TransmissionKit;

/// <summary>Why data couldn't be unpacked as gzip.</summary>
public enum GzipError
{
    NotGzip,
    Truncated,
    Corrupt,
}

/// <summary>Thrown when data isn't a complete, intact gzip file.</summary>
public sealed class GzipException : FormatException
{
    public GzipException(GzipError error, Exception? innerException = null)
        : base(Describe(error), innerException)
    {
        Error = error;
    }

    public GzipError Error { get; }

    private static string Describe(GzipError error) => error switch
    {
        GzipError.NotGzip => "The data isn't gzip-compressed.",
        GzipError.Truncated => "The gzip data is cut short.",
        GzipError.Corrupt => "The gzip data is damaged.",
        _ => error.ToString(),
    };
}

/// <summary>Unpacks single-member .gz files, such as DB-IP's country database.</summary>
public static class Gzip
{
    // The 10-byte header and 8-byte trailer every gzip member has.
    private const int MinimumLength = 18;

    /// <exception cref="GzipException">The data isn't gzip, is cut short, or is damaged.</exception>
    public static byte[] Decompress(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        // The magic number and deflate, the only compression method gzip defines.
        if (data.Length < MinimumLength || data[0] != 0x1F || data[1] != 0x8B || data[2] != 8)
        {
            throw new GzipException(GzipError.NotGzip);
        }

        byte[] unpacked;
        try
        {
            using var gzip = new GZipStream(new MemoryStream(data, writable: false), CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            unpacked = output.ToArray();
        }
        catch (InvalidDataException error)
        {
            throw new GzipException(GzipError.Corrupt, error);
        }

        // GZipStream quietly stops where a cut-off file ends, so compare with the length the trailer records
        // (modulo 2^32). In a truncated file those last bytes are really compressed data and won't match.
        var recordedLength = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - sizeof(uint)));
        if (recordedLength != (uint)unpacked.Length)
        {
            throw new GzipException(GzipError.Truncated);
        }
        return unpacked;
    }
}
