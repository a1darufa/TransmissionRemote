using TransmissionKit;

namespace StringCatalogConverter;

/// <summary>An Xcode String Catalog (.xcstrings): English keys and their translations.</summary>
internal sealed record StringCatalog(string SourceLanguage, IReadOnlyList<CatalogEntry> Entries);

/// <summary>One string: its key, the English text as written in the Mac app's code, and its localizations.</summary>
internal sealed record CatalogEntry(
    string Key,
    string? Comment,
    bool ShouldTranslate,
    IReadOnlyDictionary<string, CatalogLocalization> Localizations)
{
    /// <summary>Whether any language, English included, gives the string plural forms.</summary>
    public bool IsPlural => Localizations.Values.Any(localization => localization.PluralForms is not null);
}

/// <summary>One language's text for an entry: a single value, or one value per plural category.</summary>
internal sealed record CatalogLocalization(
    CatalogText? Value,
    IReadOnlyDictionary<PluralCategory, CatalogText>? PluralForms);

/// <summary>Xcode's "stringUnit": the text, and whether a translator has finished it.</summary>
internal sealed record CatalogText(string Value, bool IsTranslated);
