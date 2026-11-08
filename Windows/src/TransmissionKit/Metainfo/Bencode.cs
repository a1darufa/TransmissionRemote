using System.Globalization;
using System.Text;

namespace TransmissionKit;

/// <summary>A decoded bencode value (BEP 3): an integer, a byte string, a list or a dictionary.</summary>
public abstract record BencodeValue
{
    /// <summary>The number, if this is an integer.</summary>
    public long? AsInteger => (this as BencodeInteger)?.Value;

    /// <summary>The raw bytes, if this is a byte string.</summary>
    public ReadOnlyMemory<byte>? AsBytes => (this as BencodeBytes)?.Value;

    /// <summary>
    /// A byte string decoded as UTF-8, with invalid bytes replaced: torrents in the wild aren't always valid UTF-8,
    /// and a name with a few replacement characters beats no name at all.
    /// </summary>
    public string? AsString => this is BencodeBytes bytes ? Encoding.UTF8.GetString(bytes.Value.Span) : null;

    /// <summary>The items, if this is a list.</summary>
    public IReadOnlyList<BencodeValue>? AsList => (this as BencodeList)?.Items;

    /// <summary>The entries, if this is a dictionary.</summary>
    public IReadOnlyDictionary<string, BencodeValue>? AsDictionary => (this as BencodeMap)?.Entries;

    /// <summary>The value under <paramref name="key"/>, if this is a dictionary that has one.</summary>
    public BencodeValue? this[string key] => AsDictionary?.GetValueOrDefault(key);
}

/// <summary>A bencode integer.</summary>
public sealed record BencodeInteger(long Value) : BencodeValue;

/// <summary>A bencode byte string, which may or may not be text.</summary>
public sealed record BencodeBytes(ReadOnlyMemory<byte> Value) : BencodeValue
{
    // Records compare ReadOnlyMemory by reference; two strings with the same bytes are the same value.
    public bool Equals(BencodeBytes? other) => other is not null && Value.Span.SequenceEqual(other.Value.Span);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Value.Span);
        return hash.ToHashCode();
    }
}

/// <summary>A bencode list.</summary>
public sealed record BencodeList(IReadOnlyList<BencodeValue> Items) : BencodeValue
{
    public bool Equals(BencodeList? other) => other is not null && Items.SequenceEqual(other.Items);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// A bencode dictionary, called a map because .NET keeps the Dictionary suffix for collection types.
/// Keys are decoded as UTF-8, like <see cref="BencodeValue.AsString"/>.
/// </summary>
public sealed record BencodeMap(IReadOnlyDictionary<string, BencodeValue> Entries) : BencodeValue
{
    public bool Equals(BencodeMap? other) =>
        other is not null
        && Entries.Count == other.Entries.Count
        && Entries.All(entry => other.Entries.TryGetValue(entry.Key, out var value) && entry.Value.Equals(value));

    public override int GetHashCode()
    {
        // XOR doesn't depend on order, and neither does dictionary equality.
        var hash = 0;
        foreach (var (key, value) in Entries)
        {
            hash ^= HashCode.Combine(key, value);
        }
        return hash;
    }
}

/// <summary>Why bytes aren't valid bencode.</summary>
public enum BencodeError
{
    UnexpectedEnd,
    InvalidCharacter,
    InvalidInteger,
    InvalidLength,
    NestingTooDeep,
    TrailingData,
}

/// <summary>Thrown when bytes aren't valid bencode.</summary>
public sealed class BencodeException : FormatException
{
    public BencodeException(BencodeError error, int? offset = null)
        : base(Describe(error, offset))
    {
        Error = error;
        Offset = offset;
    }

    public BencodeError Error { get; }

    /// <summary>Where the problem is, in bytes from the start, for errors that point at one spot.</summary>
    public int? Offset { get; }

    private static string Describe(BencodeError error, int? offset)
    {
        var problem = error switch
        {
            BencodeError.UnexpectedEnd => "the data ends in the middle of a value",
            BencodeError.InvalidCharacter => "unexpected character",
            BencodeError.InvalidInteger => "invalid number",
            BencodeError.InvalidLength => "invalid string length",
            BencodeError.NestingTooDeep => "values are nested too deeply",
            BencodeError.TrailingData => "extra data after the end",
            _ => error.ToString(),
        };
        return offset is { } position
            ? $"Invalid bencode: {problem} at byte {position}."
            : $"Invalid bencode: {problem}.";
    }
}

/// <summary>A parsed bencode document.</summary>
/// <param name="Root">The top-level value.</param>
/// <param name="InfoRange">
/// Where the top-level "info" value sits in the original bytes, so the info hash can be computed over exactly them.
/// </param>
public sealed record BencodeDocument(BencodeValue Root, Range? InfoRange);

/// <summary>
/// A strict, bounded bencode parser. .torrent files come from the internet, so it never trusts lengths, limits
/// nesting depth and never reads past the buffer.
/// </summary>
public static class BencodeParser
{
    private const int MaximumDepth = 64;

    // An Int64 has at most 19 digits plus a sign; anything longer is garbage or an attack.
    private const int MaximumDigits = 20;

    /// <summary>Parses a complete document; anything after the top-level value is an error.</summary>
    /// <exception cref="BencodeException">The bytes aren't valid bencode.</exception>
    public static BencodeDocument Parse(ReadOnlySpan<byte> data)
    {
        var reader = new Reader(data);
        var root = reader.ReadValue(depth: 0);
        if (!reader.IsAtEnd)
        {
            throw new BencodeException(BencodeError.TrailingData);
        }
        return new BencodeDocument(root, reader.InfoRange);
    }

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;

        public Reader(ReadOnlySpan<byte> bytes) => _bytes = bytes;

        public Range? InfoRange { get; private set; }

        public readonly bool IsAtEnd => _offset == _bytes.Length;

        public BencodeValue ReadValue(int depth)
        {
            if (depth > MaximumDepth)
            {
                throw new BencodeException(BencodeError.NestingTooDeep);
            }
            return Peek() switch
            {
                null => throw new BencodeException(BencodeError.UnexpectedEnd),
                (byte)'i' => new BencodeInteger(ReadInteger()),
                (byte)'l' => new BencodeList(ReadList(depth)),
                (byte)'d' => new BencodeMap(ReadDictionary(depth)),
                >= (byte)'0' and <= (byte)'9' => new BencodeBytes(ReadBytes()),
                _ => throw new BencodeException(BencodeError.InvalidCharacter, _offset),
            };
        }

        private long ReadInteger()
        {
            var start = _offset;
            _offset++; // "i"
            var digits = ReadDigits(allowSign: true, terminator: (byte)'e');
            if (!long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            {
                throw new BencodeException(BencodeError.InvalidInteger, start);
            }
            return value;
        }

        private byte[] ReadBytes()
        {
            var start = _offset;
            var digits = ReadDigits(allowSign: false, terminator: (byte)':');
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || length > _bytes.Length - _offset)
            {
                throw new BencodeException(BencodeError.InvalidLength, start);
            }
            var value = _bytes.Slice(_offset, length).ToArray();
            _offset += length;
            return value;
        }

        private List<BencodeValue> ReadList(int depth)
        {
            _offset++; // "l"
            var items = new List<BencodeValue>();
            while (!ConsumeEndMarker())
            {
                items.Add(ReadValue(depth + 1));
            }
            return items;
        }

        private Dictionary<string, BencodeValue> ReadDictionary(int depth)
        {
            _offset++; // "d"
            var entries = new Dictionary<string, BencodeValue>(StringComparer.Ordinal);
            while (!ConsumeEndMarker())
            {
                // Keys aren't always text: BitTorrent v2 "piece layers" uses raw SHA-256 hashes as keys,
                // and rejecting them would reject every hybrid torrent.
                var key = Encoding.UTF8.GetString(ReadBytes());
                var valueStart = _offset;
                entries[key] = ReadValue(depth + 1);
                if (depth == 0 && key == "info")
                {
                    InfoRange = valueStart.._offset;
                }
            }
            return entries;
        }

        /// <summary>Reads ASCII digits (and an optional leading minus) up to the terminator, consuming it.</summary>
        private ReadOnlySpan<byte> ReadDigits(bool allowSign, byte terminator)
        {
            var start = _offset;
            while (Peek() is { } next && next != terminator)
            {
                var isDigit = next is >= (byte)'0' and <= (byte)'9';
                var isSign = allowSign && next == (byte)'-' && _offset == start;
                if (!isDigit && !isSign)
                {
                    throw new BencodeException(BencodeError.InvalidCharacter, _offset);
                }
                _offset++;
                if (_offset - start > MaximumDigits)
                {
                    throw new BencodeException(BencodeError.InvalidInteger, start);
                }
            }
            if (Peek() != terminator)
            {
                throw new BencodeException(BencodeError.UnexpectedEnd);
            }
            var digits = _bytes[start.._offset];
            _offset++; // the terminator
            return digits;
        }

        private bool ConsumeEndMarker()
        {
            var next = Peek() ?? throw new BencodeException(BencodeError.UnexpectedEnd);
            if (next != (byte)'e')
            {
                return false;
            }
            _offset++;
            return true;
        }

        private readonly byte? Peek() => _offset < _bytes.Length ? _bytes[_offset] : null;
    }
}
