namespace TransmissionKit;

/// <summary>The CLDR plural categories, in CLDR's order.</summary>
public enum PluralCategory
{
    Zero,
    One,
    Two,
    Few,
    Many,
    Other,
}

/// <summary>
/// CLDR cardinal plural rules for the app's 28 languages, so "{0} files" can pick the right form at run time.
/// .resw files have no plurals, so each form is its own resource: <c>Files_One</c>, <c>Files_Few</c>, ... (see
/// <see cref="ResourceName"/>). The rules follow the CLDR chart at
/// https://www.unicode.org/cldr/charts/latest/supplemental/language_plural_rules.html.
/// </summary>
/// <remarks>
/// Only whole numbers: the app pluralizes counts of torrents, files, peers and rules. CLDR categories that only
/// apply to decimals (Russian "other", Czech and Lithuanian "many") are therefore never returned.
/// </remarks>
public static class PluralRules
{
    /// <summary>
    /// The plural category for <paramref name="count"/> in a language such as "ru", "pt-BR" or "zh-Hans".
    /// Languages outside the app's 28 use the English rules, matching the English text they fall back to.
    /// </summary>
    public static PluralCategory CategoryFor(string languageTag, long count)
    {
        ArgumentNullException.ThrowIfNull(languageTag);
        var rule = RuleFor(languageTag);
        return rule(Magnitude(count));
    }

    /// <summary>
    /// The resource name of one plural form, the convention the String Catalog converter writes:
    /// <c>ResourceName("Files", PluralCategory.Few)</c> is <c>Files_Few</c>.
    /// </summary>
    public static string ResourceName(string baseName, PluralCategory category)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseName);
        return $"{baseName}_{category}";
    }

    private static Func<ulong, PluralCategory> RuleFor(string languageTag)
    {
        var (language, region) = LanguageAndRegion(languageTag);
        return language switch
        {
            "be" or "ru" or "uk" => EastSlavic,
            "cs" => Czech,
            "hr" => Croatian,
            "lt" => Lithuanian,
            "lv" => Latvian,
            "pl" => Polish,
            "ro" => Romanian,
            "ca" or "es" or "it" => OneAndMillions,
            "pt" when region == "pt" => OneAndMillions,
            "fr" or "pt" => ZeroOrOneAndMillions,
            "ko" or "zh" => AlwaysOther,
            // da, de, el, en, fi, hu, nb, nl, sv, tr, and any language the app isn't translated into.
            _ => OnlyOne,
        };
    }

    /// <summary>
    /// The lowercased language and region subtags of a BCP-47 tag ("pt-PT", "zh-Hans", "en_US"). Region is
    /// empty when the tag has none. Only Portuguese needs it: Portugal and Brazil count differently.
    /// </summary>
    private static (string Language, string Region) LanguageAndRegion(string languageTag)
    {
        var subtags = languageTag.ToLowerInvariant().Split('-', '_');
        var next = 1;
        if (next < subtags.Length && subtags[next].Length == 4)
        {
            next++; // A script, such as Hans in zh-Hans-CN.
        }
        var hasRegion = next < subtags.Length && subtags[next].Length is 2 or 3;
        return (subtags[0], hasRegion ? subtags[next] : "");
    }

    // CLDR rules use the number's absolute value. Unsigned, so that even long.MinValue has one.
    private static ulong Magnitude(long count) => count < 0 ? (ulong)(-(count + 1)) + 1 : (ulong)count;

    private static bool IsBetween(ulong value, ulong low, ulong high) => value >= low && value <= high;

    /// <summary>ko, zh: no plural forms.</summary>
    private static PluralCategory AlwaysOther(ulong _) => PluralCategory.Other;

    /// <summary>da, de, el, en, fi, hu, nb, nl, sv, tr: one for 1, other for everything else.</summary>
    private static PluralCategory OnlyOne(ulong n) => n == 1 ? PluralCategory.One : PluralCategory.Other;

    /// <summary>ca, es, it, pt-PT: one for 1, many for whole millions ("1 milione di file"), other.</summary>
    private static PluralCategory OneAndMillions(ulong n) => n switch
    {
        1 => PluralCategory.One,
        _ when IsWholeMillions(n) => PluralCategory.Many,
        _ => PluralCategory.Other,
    };

    /// <summary>fr, pt (Brazil): one for 0 and 1, many for whole millions, other.</summary>
    private static PluralCategory ZeroOrOneAndMillions(ulong n) => n switch
    {
        0 or 1 => PluralCategory.One,
        _ when IsWholeMillions(n) => PluralCategory.Many,
        _ => PluralCategory.Other,
    };

    private static bool IsWholeMillions(ulong n) => n != 0 && n % 1_000_000 == 0;

    /// <summary>be, ru, uk: 1, 21, 101 are one; 2–4, 22–24 are few; 0, 5–20, 25–30, 111 are many.</summary>
    private static PluralCategory EastSlavic(ulong n)
    {
        var (lastDigit, lastTwoDigits) = (n % 10, n % 100);
        if (lastDigit == 1 && lastTwoDigits != 11)
        {
            return PluralCategory.One;
        }
        if (IsBetween(lastDigit, 2, 4) && !IsBetween(lastTwoDigits, 12, 14))
        {
            return PluralCategory.Few;
        }
        return PluralCategory.Many;
    }

    /// <summary>pl: only 1 is one; 2–4, 22–24 are few; 0, 5–21, 25–31, 112 are many.</summary>
    private static PluralCategory Polish(ulong n)
    {
        var (lastDigit, lastTwoDigits) = (n % 10, n % 100);
        if (n == 1)
        {
            return PluralCategory.One;
        }
        if (IsBetween(lastDigit, 2, 4) && !IsBetween(lastTwoDigits, 12, 14))
        {
            return PluralCategory.Few;
        }
        return PluralCategory.Many;
    }

    /// <summary>cs: 1 is one, 2–4 are few, everything else is other ("many" is for decimals).</summary>
    private static PluralCategory Czech(ulong n) => n switch
    {
        1 => PluralCategory.One,
        2 or 3 or 4 => PluralCategory.Few,
        _ => PluralCategory.Other,
    };

    /// <summary>hr: 1, 21, 101 are one; 2–4, 22–24 are few; 0, 5–20, 25–30 are other.</summary>
    private static PluralCategory Croatian(ulong n)
    {
        var (lastDigit, lastTwoDigits) = (n % 10, n % 100);
        if (lastDigit == 1 && lastTwoDigits != 11)
        {
            return PluralCategory.One;
        }
        if (IsBetween(lastDigit, 2, 4) && !IsBetween(lastTwoDigits, 12, 14))
        {
            return PluralCategory.Few;
        }
        return PluralCategory.Other;
    }

    /// <summary>lt: 1, 21, 101 are one; 2–9, 22–29 are few; 0, 10–20, 30 are other ("many" is decimals).</summary>
    private static PluralCategory Lithuanian(ulong n)
    {
        var (lastDigit, lastTwoDigits) = (n % 10, n % 100);
        if (IsBetween(lastTwoDigits, 11, 19))
        {
            return PluralCategory.Other;
        }
        return lastDigit switch
        {
            1 => PluralCategory.One,
            0 => PluralCategory.Other,
            _ => PluralCategory.Few,
        };
    }

    /// <summary>lv: 0, 10–20, 30 are zero; 1, 21, 101 are one; 2–9, 22–29 are other.</summary>
    private static PluralCategory Latvian(ulong n)
    {
        var (lastDigit, lastTwoDigits) = (n % 10, n % 100);
        if (lastDigit == 0 || IsBetween(lastTwoDigits, 11, 19))
        {
            return PluralCategory.Zero;
        }
        return lastDigit == 1 ? PluralCategory.One : PluralCategory.Other;
    }

    /// <summary>ro: 1 is one; 0, 2–19, 101–119 are few; 20–100, 120 are other.</summary>
    private static PluralCategory Romanian(ulong n)
    {
        if (n == 1)
        {
            return PluralCategory.One;
        }
        if (n == 0 || IsBetween(n % 100, 1, 19))
        {
            return PluralCategory.Few;
        }
        return PluralCategory.Other;
    }
}
