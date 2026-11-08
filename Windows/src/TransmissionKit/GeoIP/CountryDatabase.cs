using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;

namespace TransmissionKit;

/// <summary>
/// Finds the country of an IP address, for the Peers tab's Country column, like Transmission Remote GUI's GeoIP
/// lookup. Built from DB-IP's free "IP to Country Lite" CSV ("first,last,country" per line, licensed CC BY 4.0).
/// </summary>
public sealed class CountryDatabase
{
    /// <summary>Marks addresses the database doesn't cover. DB-IP itself uses "ZZ" for these.</summary>
    private const ushort Unknown = 0;

    private static readonly SearchValues<char> IPv6Characters = SearchValues.Create("0123456789abcdefABCDEF:.");

    // Range starts in ascending order, with the country of each range. Gaps between the CSV's ranges are filled
    // with Unknown entries, so a lookup only needs the last start at or below the address.
    private readonly uint[] _ipv4Starts;
    private readonly ushort[] _ipv4Countries;
    private readonly UInt128[] _ipv6Starts;
    private readonly ushort[] _ipv6Countries;

    /// <summary>Parses the CSV. Lines that don't parse are skipped rather than failing the whole file.</summary>
    public CountryDatabase(ReadOnlySpan<byte> csv)
    {
        var ipv4 = new RangeBuilder<uint>();
        var ipv6 = new RangeBuilder<UInt128>();

        foreach (var lineRange in csv.Split((byte)'\n'))
        {
            if (!TrySplitFields(csv[lineRange], out var firstField, out var lastField, out var countryField)
                || !TryParseCountry(countryField, out var country)
                || AsciiText(firstField) is not { } first
                || AsciiText(lastField) is not { } last)
            {
                continue;
            }
            if (TryParseIPv4(first, out var ipv4Start) && TryParseIPv4(last, out var ipv4End))
            {
                ipv4.Add(ipv4Start, ipv4End, country);
            }
            else if (TryParseIPv6(first, out var ipv6Start) && TryParseIPv6(last, out var ipv6End))
            {
                ipv6.Add(ipv6Start, ipv6End, country);
            }
        }

        (_ipv4Starts, _ipv4Countries) = ipv4.Build();
        (_ipv6Starts, _ipv6Countries) = ipv6.Build();
    }

    public bool IsEmpty => _ipv4Starts.Length == 0 && _ipv6Starts.Length == 0;

    /// <summary>The ISO country code ("DE") for an address, or null when it's unknown, private or unparsable.</summary>
    public string? CountryCodeFor(string address)
    {
        ushort? country = TryParseIPv4(address, out var ipv4) ? LookUp(ipv4, _ipv4Starts, _ipv4Countries)
            : TryParseIPv6(address, out var ipv6) ? LookUp(ipv6, _ipv6Starts, _ipv6Countries)
            : null;
        return country is ushort code and not Unknown ? new string([(char)(code >> 8), (char)(code & 0xFF)]) : null;
    }

    /// <summary>
    /// The dotted-quad form only, like the Mac app's inet_pton: four decimal parts without leading zeros.
    /// IPAddress.TryParse would also take "1.0.1" or "0x7f.1" and find a country for them.
    /// </summary>
    internal static bool TryParseIPv4(ReadOnlySpan<char> text, out uint address)
    {
        address = 0;
        var parts = 0;
        foreach (var partRange in text.Split('.'))
        {
            var part = text[partRange];
            if (++parts > 4
                || part.Length is 0 or > 3
                || (part.Length > 1 && part[0] == '0')
                || !byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var octet))
            {
                return false;
            }
            address = address << 8 | octet;
        }
        return parts == 4;
    }

    /// <summary>
    /// Plain IPv6 text, like the Mac app's inet_pton. IPAddress.TryParse would also take brackets, a port or a zone
    /// ID, which never appear in the database.
    /// </summary>
    internal static bool TryParseIPv6(ReadOnlySpan<char> text, out UInt128 address)
    {
        address = 0;
        if (text.ContainsAnyExcept(IPv6Characters)
            || !IPAddress.TryParse(text, out var parsed)
            || parsed.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return false;
        }
        Span<byte> bytes = stackalloc byte[16];
        parsed.TryWriteBytes(bytes, out _);
        address = BinaryPrimitives.ReadUInt128BigEndian(bytes);
        return true;
    }

    private static ushort? LookUp<TAddress>(TAddress address, TAddress[] starts, ushort[] countries)
        where TAddress : IComparisonOperators<TAddress, TAddress, bool>
    {
        // Binary search for the last range starting at or before the address.
        var low = 0;
        var high = starts.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (starts[middle] <= address)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }
        return low > 0 ? countries[low - 1] : null;
    }

    /// <summary>Finds the first three comma-separated fields; any further ones are ignored.</summary>
    private static bool TrySplitFields(ReadOnlySpan<byte> line, out ReadOnlySpan<byte> first,
        out ReadOnlySpan<byte> last, out ReadOnlySpan<byte> country)
    {
        first = last = country = default;
        var firstComma = line.IndexOf((byte)',');
        if (firstComma < 0)
        {
            return false;
        }
        first = line[..firstComma];
        var rest = line[(firstComma + 1)..];

        var secondComma = rest.IndexOf((byte)',');
        if (secondComma < 0)
        {
            return false;
        }
        last = rest[..secondComma];
        rest = rest[(secondComma + 1)..];

        var thirdComma = rest.IndexOf((byte)',');
        country = thirdComma < 0 ? rest : rest[..thirdComma];
        return true;
    }

    /// <summary>Two uppercase ASCII letters packed into 16 bits; "ZZ" (DB-IP's "unknown") becomes Unknown.</summary>
    private static bool TryParseCountry(ReadOnlySpan<byte> field, out ushort country)
    {
        country = Unknown;
        var letterCount = 0;
        var packed = 0;
        foreach (var character in field)
        {
            // Files saved with Windows line endings leave a carriage return on the last field.
            if (character == (byte)'\r')
            {
                continue;
            }
            if (++letterCount > 2 || character is < (byte)'A' or > (byte)'Z')
            {
                return false;
            }
            packed = packed << 8 | character;
        }
        if (letterCount != 2)
        {
            return false;
        }
        country = packed == ('Z' << 8 | 'Z') ? Unknown : (ushort)packed;
        return true;
    }

    private static string? AsciiText(ReadOnlySpan<byte> field) =>
        Ascii.IsValid(field) ? Encoding.ASCII.GetString(field) : null;

    /// <summary>Collects ranges in file order and fills the gaps between them with Unknown.</summary>
    private sealed class RangeBuilder<TAddress>
        where TAddress : struct, IBinaryInteger<TAddress>, IUnsignedNumber<TAddress>, IMinMaxValue<TAddress>
    {
        private readonly List<(TAddress Start, TAddress End, ushort Country)> _ranges = [];

        public void Add(TAddress start, TAddress end, ushort country)
        {
            if (start <= end)
            {
                _ranges.Add((start, end, country));
            }
        }

        public (TAddress[] Starts, ushort[] Countries) Build()
        {
            var starts = new List<TAddress>(_ranges.Count);
            var countries = new List<ushort>(_ranges.Count);
            TAddress? nextExpected = TAddress.Zero;

            foreach (var (start, end, country) in _ranges.OrderBy(range => range.Start))
            {
                if (nextExpected is { } expected && start > expected)
                {
                    starts.Add(expected);
                    countries.Add(Unknown);
                }
                starts.Add(start);
                countries.Add(country);
                // Null once a range reaches the very last address, so the addition can't overflow.
                nextExpected = end == TAddress.MaxValue ? null : end + TAddress.One;
            }
            if (nextExpected is { } afterLast && starts.Count > 0)
            {
                starts.Add(afterLast);
                countries.Add(Unknown);
            }
            return ([.. starts], [.. countries]);
        }
    }
}

/// <summary>A flag emoji and localized name for a country code, for the Peers tab.</summary>
public static class CountryDisplay
{
    private const int RegionalIndicatorA = 0x1F1E6;

    /// <summary>"DE" → 🇩🇪, using the regional indicator letters emoji fonts draw as flags.</summary>
    public static string Flag(string code)
    {
        var flag = new StringBuilder();
        foreach (var letter in code.ToUpperInvariant().EnumerateRunes())
        {
            if (Rune.TryCreate(RegionalIndicatorA - 'A' + letter.Value, out var indicator))
            {
                flag.Append(indicator.ToString());
            }
        }
        return flag.ToString();
    }

    /// <summary>
    /// The country's name, or the code itself when .NET doesn't know the region. .NET names regions in the
    /// operating system's display language, falling back to the region's own language when the app's UI culture
    /// is a different one.
    /// </summary>
    public static string Name(string code)
    {
        try
        {
            return new RegionInfo(code).DisplayName;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }
}
