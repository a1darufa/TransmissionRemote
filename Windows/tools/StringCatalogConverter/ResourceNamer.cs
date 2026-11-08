using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StringCatalogConverter;

/// <summary>
/// Derives .resw resource names from the English source text: "Show Log Files" becomes ShowLogFiles. The Mac
/// catalogs use the English text as the key, but .resw names are looked up from code and through x:Uid, where "."
/// and "/" have meanings, so sentences can't be names.
/// </summary>
internal static partial class ResourceNamer
{
    /// <summary>The longest name before a collision suffix. Longer texts keep their first whole words.</summary>
    public const int MaxLength = 40;

    private const string EllipsisWord = "Ellipsis";

    // Names never start with a digit, so they stay valid C# identifiers for code that refers to them.
    private const string NoWordsName = "Text";

    /// <summary>
    /// Names for all keys. Keys whose names clash (MRT compares names case-insensitively, so "Total Size" and
    /// "Total size" do) all get a suffix from a hash of their own key; that way a name doesn't depend on catalog
    /// order or on which catalog a key comes from. Plural keys are named apart from the others because their
    /// resources always end in a category ("Files_One"): "Files" and "%lld files" don't clash.
    /// </summary>
    public static IReadOnlyDictionary<string, string> AssignNames(IEnumerable<(string Key, bool IsPlural)> keys)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var candidates = keys.Distinct().Select(key => (key.Key, key.IsPlural, Name: BaseName(key.Key)));
        foreach (var clashing in candidates.GroupBy(key => (key.IsPlural, key.Name.ToUpperInvariant())))
        {
            var isAmbiguous = clashing.Skip(1).Any();
            foreach (var (key, _, name) in clashing)
            {
                names.Add(key, isAmbiguous ? $"{name}_{HashSuffix(key)}" : name);
            }
        }
        return names;
    }

    /// <summary>
    /// The PascalCase name for one English text, before collisions are resolved: placeholders and punctuation
    /// are dropped, contractions spelled out ("couldn't" → CouldNot), arrows named (↓ → Down), and a trailing
    /// ellipsis kept as a word, since "Settings" and "Settings…" are different strings.
    /// </summary>
    public static string BaseName(string englishText)
    {
        var text = MarkdownLink().Replace(englishText, "${text}");
        text = AppleFormat.Specifier().Replace(text, " ");
        var trimmed = text.TrimEnd();
        var hasEllipsis = trimmed.EndsWith('…') || trimmed.EndsWith("...", StringComparison.Ordinal);

        text = Contraction().Replace(text, SpellOut);
        text = text.Replace("↓", " Down ", StringComparison.Ordinal)
            .Replace("↑", " Up ", StringComparison.Ordinal)
            .Replace('µ', 'u') // MICRO SIGN, as in µTP
            .Replace('μ', 'u'); // GREEK SMALL LETTER MU
        List<string> words = [.. Word().Matches(WithoutAccents(text)).Select(match => Capitalized(match.Value))];
        if (words.Count == 0 || char.IsAsciiDigit(words[0][0]))
        {
            words.Insert(0, NoWordsName);
        }

        var suffix = hasEllipsis ? EllipsisWord : "";
        return JoinWords(words, MaxLength - suffix.Length) + suffix;
    }

    /// <summary>
    /// Six hex digits from the key's SHA-256, the same on every machine and run (string.GetHashCode isn't).
    /// </summary>
    public static string HashSuffix(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0, 3);

    /// <summary>Whole words up to <paramref name="maxLength"/>; a single longer word is cut.</summary>
    private static string JoinWords(List<string> words, int maxLength)
    {
        var name = new StringBuilder();
        foreach (var word in words)
        {
            if (name.Length > 0 && name.Length + word.Length > maxLength)
            {
                break;
            }
            name.Append(word);
        }
        return name.Length > maxLength ? name.ToString(0, maxLength) : name.ToString();
    }

    private static string SpellOut(Match contraction)
    {
        var stem = contraction.Groups["stem"].Value;
        var ending = contraction.Groups["ending"].Value;
        return ending.ToUpperInvariant() switch
        {
            "T" when stem.Equals("can", StringComparison.OrdinalIgnoreCase) => $"{stem}not",
            "T" when stem.Equals("won", StringComparison.OrdinalIgnoreCase) => $"{stem[0]}ill not",
            "T" when stem.EndsWith('n') || stem.EndsWith('N') => $"{stem[..^1]} not",
            "RE" => $"{stem} are",
            "LL" => $"{stem} will",
            "VE" => $"{stem} have",
            "M" => $"{stem} am",
            "D" => $"{stem} would",
            // Possessives ("server's") and anything else just lose the apostrophe.
            _ => stem + ending,
        };
    }

    private static string WithoutAccents(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        return string.Concat(decomposed.Where(
            character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark));
    }

    private static string Capitalized(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    /// <summary>
    /// Markdown the Mac strings use: links, [text](url), and automatic grammar agreement, ^[text](inflect: true).
    /// </summary>
    [GeneratedRegex(@"\^?\[(?<text>[^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"(?<stem>\p{L}+)['’](?<ending>t|re|ll|ve|m|d|s)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Contraction();

    [GeneratedRegex("[A-Za-z0-9]+")]
    private static partial Regex Word();
}
