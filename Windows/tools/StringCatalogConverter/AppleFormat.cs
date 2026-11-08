using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StringCatalogConverter;

/// <summary>
/// An Apple format string ("%@ of %lld", "%2$@ %1$@") split into text and placeholders, so it can be written as a
/// .NET composite format string ("{0} of {1}", "{1} {0}").
/// </summary>
/// <remarks>
/// Numbers carry no formatting into the .NET text: the app formats counts, sizes and speeds itself before
/// inserting them, as the Mac app does. The one exception is a fixed number of decimals (%.1f), whose exact
/// .NET equivalent is {0:F1}. Specifiers the Mac catalogs don't use (widths, flags, %x, %s, ...) are rejected
/// rather than guessed at.
/// </remarks>
internal sealed partial class AppleFormat
{
    // %[position$][flags][width][.precision][length]conversion, as in printf, plus %@ for objects.
    private const string SpecifierPattern =
        @"%(?:(?<position>[1-9][0-9]{0,2})\$)?(?<flags>[-+ #0']*)(?<width>[0-9]+)?(?:\.(?<precision>[0-9]+))?" +
        "(?<length>hh|h|ll|l|q|L|z|t|j)?(?<conversion>[@dDiuUxXoOfFeEgGaAcCsSp%])";

    private readonly IReadOnlyList<Segment> _segments;

    private AppleFormat(IReadOnlyList<Segment> segments)
    {
        _segments = segments;
        PlaceholderIndices = segments.OfType<Placeholder>().Select(placeholder => placeholder.Index).ToHashSet();
    }

    /// <summary>The zero-based argument indices the text uses, in no particular order.</summary>
    public IReadOnlySet<int> PlaceholderIndices { get; }

    /// <summary>Splits Apple format text into literal text and placeholders.</summary>
    /// <exception cref="FormatException">A specifier is malformed or has no faithful .NET equivalent.</exception>
    public static AppleFormat Parse(string text)
    {
        var segments = new List<Segment>();
        var literal = new StringBuilder();
        var numbering = new ArgumentNumbering(text);
        var position = 0;
        while (text.IndexOf('%', position) is var percent and >= 0)
        {
            literal.Append(text, position, percent - position);
            var specifier = AnchoredSpecifier().Match(text, percent);
            if (!specifier.Success)
            {
                throw new FormatException($"Unrecognized format specifier at position {percent} in \"{text}\".");
            }
            position = percent + specifier.Length;

            if (specifier.Value == "%%")
            {
                literal.Append('%');
                continue;
            }
            if (literal.Length > 0)
            {
                segments.Add(new Literal(literal.ToString()));
                literal.Clear();
            }
            segments.Add(new Placeholder(numbering.IndexFor(specifier), DotNetFormat(specifier, text)));
        }
        literal.Append(text, position, text.Length - position);
        if (literal.Length > 0)
        {
            segments.Add(new Literal(literal.ToString()));
        }
        return new AppleFormat(segments);
    }

    /// <summary>
    /// The .NET text. As a format string, literal braces are doubled so that string.Format keeps them; text the
    /// app shows without formatting keeps them single.
    /// </summary>
    public string ToDotNet(bool asFormatString)
    {
        if (!asFormatString && PlaceholderIndices.Count > 0)
        {
            throw new InvalidOperationException("Text with placeholders can only be written as a format string.");
        }

        var result = new StringBuilder();
        foreach (var segment in _segments)
        {
            switch (segment)
            {
                case Literal { Text: var text } when asFormatString:
                    result.Append(text.Replace("{", "{{", StringComparison.Ordinal)
                        .Replace("}", "}}", StringComparison.Ordinal));
                    break;
                case Literal { Text: var text }:
                    result.Append(text);
                    break;
                case Placeholder { Index: var index, Format: null }:
                    result.Append(CultureInfo.InvariantCulture, $"{{{index}}}");
                    break;
                case Placeholder { Index: var index, Format: var format }:
                    result.Append(CultureInfo.InvariantCulture, $"{{{index}:{format}}}");
                    break;
            }
        }
        return result.ToString();
    }

    /// <summary>The .NET format for one specifier: none for objects and integers, "F1" for %.1f.</summary>
    private static string? DotNetFormat(Match specifier, string text)
    {
        var conversion = specifier.Groups["conversion"].Value;
        var precision = specifier.Groups["precision"];
        if (specifier.Groups["flags"].Length > 0 || specifier.Groups["width"].Success)
        {
            throw new FormatException($"\"{specifier.Value}\" in \"{text}\": widths and flags aren't supported.");
        }
        return conversion switch
        {
            "f" or "F" => $"F{(precision.Success ? precision.Value : "6")}",
            "@" or "d" or "i" or "u" when !precision.Success => null,
            _ => throw new FormatException($"\"{specifier.Value}\" in \"{text}\" has no supported .NET equivalent."),
        };
    }

    /// <summary>Matches a specifier anywhere, to strip specifiers from text.</summary>
    [GeneratedRegex(SpecifierPattern)]
    internal static partial Regex Specifier();

    [GeneratedRegex(@"\G" + SpecifierPattern)]
    private static partial Regex AnchoredSpecifier();

    /// <summary>
    /// Argument numbers: "%2$@" names its argument, "%@" takes the next one. Mixing the two is ambiguous in printf
    /// and never needed, so it's an error.
    /// </summary>
    private sealed class ArgumentNumbering(string text)
    {
        private int _nextSequentialIndex;
        private bool _usesPositions;

        public int IndexFor(Match specifier)
        {
            var position = specifier.Groups["position"];
            var isPositional = position.Success;
            if ((isPositional && _nextSequentialIndex > 0) || (!isPositional && _usesPositions))
            {
                throw new FormatException($"\"{text}\" mixes numbered (%1$@) and unnumbered (%@) specifiers.");
            }
            if (isPositional)
            {
                _usesPositions = true;
                return int.Parse(position.Value, CultureInfo.InvariantCulture) - 1;
            }
            return _nextSequentialIndex++;
        }
    }

    private abstract record Segment;

    private sealed record Literal(string Text) : Segment;

    /// <summary>An argument, by zero-based index, with an optional .NET format such as "F1".</summary>
    private sealed record Placeholder(int Index, string? Format) : Segment;
}
