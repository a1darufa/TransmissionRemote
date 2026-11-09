using TransmissionKit;

namespace StringCatalogConverter;

/// <summary>One entry of a .resw file.</summary>
internal sealed record ResourceString(string Name, string Value, string Comment);

/// <summary>
/// Turns String Catalogs into the resources of each language's .resw file, keyed by the language's folder name
/// ("en-US", "ru", "zh-Hans", ...) and sorted by name so regenerated files diff cleanly.
/// </summary>
/// <remarks>
/// Only finished translations are written. A string a language lacks is left out of that language's file, and
/// MRT then falls back to the default language, en-US, for that one string, just as macOS falls back to English.
/// </remarks>
internal static class CatalogConverter
{
    /// <summary>The app's default language: MRT's fallback for anything a language doesn't have.</summary>
    public const string DefaultLanguage = "en-US";

    private const string SourceLanguage = "en";

    /// <exception cref="InvalidDataException">A catalog can't be converted faithfully.</exception>
    public static IReadOnlyDictionary<string, IReadOnlyList<ResourceString>> Convert(
        IEnumerable<StringCatalog> catalogs)
    {
        var entries = catalogs.SelectMany(EnglishSourceEntries).ToList();
        var names = ResourceNamer.AssignNames(PluralAgreement(entries));
        var files = new Dictionary<string, Dictionary<string, ResourceString>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            foreach (var (language, resource) in Resources(entry, names[entry.Key]))
            {
                Add(files, language, resource, entry.Key);
            }
        }

        AddDefaultLanguagePluralForms(files);

        var sorted = new SortedDictionary<string, IReadOnlyList<ResourceString>>(StringComparer.Ordinal);
        foreach (var (language, file) in files)
        {
            sorted[language] = [.. file.Values.OrderBy(resource => resource.Name, StringComparer.Ordinal)];
        }
        return sorted;
    }

    /// <summary>"Files" for "Files_Few"; null for a name that isn't a plural form.</summary>
    public static string? PluralFamily(string name) => Enum.GetValues<PluralCategory>()
        .Select(category => $"_{category}")
        .Where(suffix => name.EndsWith(suffix, StringComparison.Ordinal))
        .Select(suffix => name[..^suffix.Length])
        .FirstOrDefault();

    /// <summary>
    /// MRT wants every name to exist in the default language, or it warns at build time (PRI263) and may throw
    /// for some language setups. English has fewer plural forms than Russian or Polish, so the forms only other
    /// languages use (Files_Few) get English's "other" text in en-US. English never reads them: its plural rules
    /// only pick One and Other.
    /// </summary>
    private static void AddDefaultLanguagePluralForms(Dictionary<string, Dictionary<string, ResourceString>> files)
    {
        if (!files.TryGetValue(DefaultLanguage, out var english))
        {
            return;
        }
        var formNames = files.Values
            .SelectMany(file => file.Keys)
            .Where(name => PluralFamily(name) is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var name in formNames.Where(name => !english.ContainsKey(name)))
        {
            var otherName = PluralRules.ResourceName(PluralFamily(name)!, PluralCategory.Other);
            if (english.TryGetValue(otherName, out var other))
            {
                english[name] = other with
                {
                    Name = name,
                    Comment = other.Comment + "\nOnly a default for MRT: English never uses this plural form.",
                };
            }
        }
    }

    private static IReadOnlyList<CatalogEntry> EnglishSourceEntries(StringCatalog catalog) =>
        catalog.SourceLanguage == SourceLanguage
            ? catalog.Entries
            : throw new InvalidDataException(
                $"The catalog's source language is \"{catalog.SourceLanguage}\"; the converter expects English.");

    /// <summary>
    /// Keys with whether they're plural. A key in two catalogs must be plural in both, or it would need two
    /// kinds of resource under one name.
    /// </summary>
    private static IEnumerable<(string Key, bool IsPlural)> PluralAgreement(List<CatalogEntry> entries)
    {
        foreach (var group in entries.GroupBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (group.Select(entry => entry.IsPlural).Distinct().Skip(1).Any())
            {
                throw new InvalidDataException(
                    $"\"{group.Key}\" is plural in one catalog and not in another.");
            }
            yield return (group.Key, group.First().IsPlural);
        }
    }

    /// <summary>Every language's resources for one entry: always English, then each finished translation.</summary>
    private static IEnumerable<(string Language, ResourceString Resource)> Resources(
        CatalogEntry entry, string baseName)
    {
        var comment = Comment(entry);
        var english = Parsed(EnglishTexts(entry, baseName), entry.Key, DefaultLanguage);
        // A string with placeholders is a format string in every language: the app passes it to string.Format.
        var isFormatString = entry.IsPlural || english.Values.Any(format => format.PlaceholderIndices.Count > 0);
        var englishPlaceholders = english.Values.SelectMany(format => format.PlaceholderIndices).ToHashSet();

        foreach (var (name, format) in english)
        {
            yield return (DefaultLanguage, new ResourceString(name, format.ToDotNet(isFormatString), comment));
        }
        if (!entry.ShouldTranslate)
        {
            yield break;
        }

        foreach (var (language, localization) in entry.Localizations)
        {
            if (language == SourceLanguage || TranslatedTexts(entry, localization, baseName) is not { } texts)
            {
                continue;
            }
            foreach (var (name, format) in Parsed(texts, entry.Key, language))
            {
                RequireKnownPlaceholders(format, englishPlaceholders, entry.Key, language);
                yield return (language, new ResourceString(name, format.ToDotNet(isFormatString), comment));
            }
        }
    }

    /// <summary>
    /// The English texts by resource name. Xcode only stores an English localization when it differs from the
    /// key (positional specifiers, plural forms); otherwise the key is the English text.
    /// </summary>
    private static Dictionary<string, string> EnglishTexts(CatalogEntry entry, string baseName)
    {
        var english = entry.Localizations.GetValueOrDefault(SourceLanguage);
        if (entry.IsPlural && english?.PluralForms is { } forms)
        {
            return forms.ToDictionary(
                form => PluralRules.ResourceName(baseName, form.Key), form => form.Value.Value);
        }
        var name = entry.IsPlural ? PluralRules.ResourceName(baseName, PluralCategory.Other) : baseName;
        return new() { [name] = english?.Value?.Value ?? entry.Key };
    }

    /// <summary>A language's texts by resource name, or null when it should fall back to English.</summary>
    private static Dictionary<string, string>? TranslatedTexts(
        CatalogEntry entry, CatalogLocalization localization, string baseName)
    {
        if (localization.PluralForms is { } forms)
        {
            // A half-finished set of plural forms would mix languages in one string, so it falls back as a whole.
            return forms.Values.All(form => form.IsTranslated)
                ? forms.ToDictionary(form => PluralRules.ResourceName(baseName, form.Key), form => form.Value.Value)
                : null;
        }
        if (localization.Value is not { IsTranslated: true } value)
        {
            return null;
        }
        // Languages without plurals (Chinese, Korean) may store a plural string as one value: their "other" form.
        var name = entry.IsPlural ? PluralRules.ResourceName(baseName, PluralCategory.Other) : baseName;
        return new() { [name] = value.Value };
    }

    private static Dictionary<string, AppleFormat> Parsed(
        Dictionary<string, string> texts, string key, string language)
    {
        var formats = new Dictionary<string, AppleFormat>(StringComparer.Ordinal);
        foreach (var (name, text) in texts)
        {
            if (text.Contains("](inflect:", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"\"{key}\" ({language}) uses automatic grammar agreement, which .resw can't express. Give it " +
                    "plural variations in the catalog instead.");
            }
            try
            {
                formats[name] = AppleFormat.Parse(text);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException($"\"{key}\" ({language}): {exception.Message}", exception);
            }
        }
        return formats;
    }

    /// <summary>
    /// A translation may leave out an argument ("One file"), but one the English text doesn't have would make
    /// string.Format throw at run time.
    /// </summary>
    private static void RequireKnownPlaceholders(
        AppleFormat translation, HashSet<int> englishPlaceholders, string key, string language)
    {
        var extra = translation.PlaceholderIndices.Except(englishPlaceholders).Order().ToList();
        if (extra.Count > 0)
        {
            throw new InvalidDataException(
                $"\"{key}\" ({language}) has argument {extra[0] + 1}, which the English text doesn't.");
        }
    }

    /// <summary>
    /// The Mac key, so the Windows developer can search for the Mac wording, plus anything the catalog says.
    /// </summary>
    private static string Comment(CatalogEntry entry)
    {
        List<string> lines = [$"Mac key: {entry.Key}"];
        if (!entry.ShouldTranslate)
        {
            lines.Add("Not translated: the Mac catalog marks it shouldTranslate = false.");
        }
        if (!string.IsNullOrWhiteSpace(entry.Comment))
        {
            lines.Add($"Mac comment: {entry.Comment}");
        }
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Adds a resource to its language's file. The same key in two catalogs is fine as long as the text is the
    /// same; different text would leave one of them unused.
    /// </summary>
    private static void Add(
        Dictionary<string, Dictionary<string, ResourceString>> files,
        string language,
        ResourceString resource,
        string key)
    {
        if (!files.TryGetValue(language, out var file))
        {
            file = new Dictionary<string, ResourceString>(StringComparer.OrdinalIgnoreCase);
            files[language] = file;
        }
        if (file.TryGetValue(resource.Name, out var existing) && existing != resource)
        {
            throw new InvalidDataException(
                $"\"{key}\" ({language}) is in more than one catalog with different text, or its name " +
                $"{resource.Name} clashes with another string's.");
        }
        file[resource.Name] = resource;
    }
}
