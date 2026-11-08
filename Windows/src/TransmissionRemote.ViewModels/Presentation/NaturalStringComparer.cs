using System.Globalization;

namespace TransmissionRemote.ViewModels;

/// <summary>
/// Sorts names the way File Explorer and Finder do: "Season 2" before "Season 10", ignoring case.
/// </summary>
/// <remarks>
/// <c>CompareOptions.NumericOrdering</c> needs ICU, which Windows 10 1809 doesn't have, so the digits are
/// compared here instead.
/// </remarks>
public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        if (x is null)
        {
            return -1;
        }
        if (y is null)
        {
            return 1;
        }

        var left = x.AsSpan();
        var right = y.AsSpan();
        while (!left.IsEmpty && !right.IsEmpty)
        {
            var leftChunk = NextChunk(left);
            var rightChunk = NextChunk(right);
            var order = char.IsAsciiDigit(leftChunk[0]) && char.IsAsciiDigit(rightChunk[0])
                ? CompareNumbers(leftChunk, rightChunk)
                // Names are sorted for people, in the user's language, not by character codes.
                : CultureInfo.CurrentCulture.CompareInfo.Compare(leftChunk, rightChunk, CompareOptions.IgnoreCase);
            if (order != 0)
            {
                return order;
            }
            left = left[leftChunk.Length..];
            right = right[rightChunk.Length..];
        }
        return left.Length.CompareTo(right.Length);
    }

    /// <summary>A run of digits, or a run of anything else.</summary>
    private static ReadOnlySpan<char> NextChunk(ReadOnlySpan<char> text)
    {
        var isDigit = char.IsAsciiDigit(text[0]);
        var length = 1;
        while (length < text.Length && char.IsAsciiDigit(text[length]) == isDigit)
        {
            length++;
        }
        return text[..length];
    }

    /// <summary>Compares digit runs by value without parsing, so any length works: "007" equals "7".</summary>
    private static int CompareNumbers(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');
        return left.Length != right.Length ? left.Length.CompareTo(right.Length) : left.SequenceCompareTo(right);
    }
}
