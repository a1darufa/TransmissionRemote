using System.Text.Json;
using System.Text.RegularExpressions;
using TransmissionKit;

namespace StringCatalogConverter;

/// <summary>
/// Reads the .xcstrings JSON format: string units, plural variations, comments and "shouldTranslate". Shapes
/// that .resw can't express faithfully (substitutions, device variations) are errors rather than skipped, so no
/// string silently goes missing on Windows.
/// </summary>
internal static partial class StringCatalogReader
{
    /// <summary>Reads a catalog. <paramref name="sourceName"/> (usually its path) appears in error messages.</summary>
    /// <exception cref="InvalidDataException">The catalog has a shape the converter doesn't support.</exception>
    /// <exception cref="JsonException">The text isn't JSON.</exception>
    public static StringCatalog Parse(string json, string sourceName)
    {
        using var document = JsonDocument.Parse(json);
        var root = RequireObject(document.RootElement, sourceName);
        var sourceLanguage = RequireString(root, "sourceLanguage", sourceName);
        var strings = RequireObject(RequireProperty(root, "strings", sourceName), $"{sourceName}, \"strings\"");
        List<CatalogEntry> entries = [.. strings.EnumerateObject().Select(entry => Entry(entry, sourceName))];
        return new StringCatalog(sourceLanguage, entries);
    }

    private static CatalogEntry Entry(JsonProperty property, string sourceName)
    {
        var context = $"{sourceName}, \"{property.Name}\"";
        var entry = RequireObject(property.Value, context);
        var comment = entry.TryGetProperty("comment", out var commentElement)
            ? StringValue(commentElement, $"{context}, \"comment\"")
            : null;
        var shouldTranslate = ShouldTranslate(entry, context);

        var localizations = new Dictionary<string, CatalogLocalization>(StringComparer.Ordinal);
        if (entry.TryGetProperty("localizations", out var localizationsElement))
        {
            foreach (var localization in RequireObject(localizationsElement, context).EnumerateObject())
            {
                var language = localization.Name;
                // Language codes become folder names, so a broken or hostile catalog mustn't reach outside the
                // output folder.
                if (!LanguageCode().IsMatch(language))
                {
                    throw new InvalidDataException($"{context}: \"{language}\" isn't a language code.");
                }
                localizations[language] = Localization(localization.Value, $"{context}, {language}");
            }
        }
        return new CatalogEntry(property.Name, comment, shouldTranslate, localizations);
    }

    /// <summary>Xcode writes "shouldTranslate": false for strings that stay as they are, such as "%@ %@".</summary>
    private static bool ShouldTranslate(JsonElement entry, string context)
    {
        if (!entry.TryGetProperty("shouldTranslate", out var flag))
        {
            return true;
        }
        return flag.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"{context}: \"shouldTranslate\" isn't true or false."),
        };
    }

    private static CatalogLocalization Localization(JsonElement element, string context)
    {
        var localization = RequireObject(element, context);
        if (localization.TryGetProperty("substitutions", out _))
        {
            throw new InvalidDataException(
                $"{context}: substitutions (several plural numbers in one string) can't be converted to .resw yet.");
        }

        var hasValue = localization.TryGetProperty("stringUnit", out var stringUnit);
        var hasVariations = localization.TryGetProperty("variations", out var variations);
        return (hasValue, hasVariations) switch
        {
            (true, false) => new CatalogLocalization(Text(stringUnit, context), PluralForms: null),
            (false, true) => new CatalogLocalization(Value: null, PluralForms(variations, context)),
            _ => throw new InvalidDataException($"{context}: expected either a stringUnit or plural variations."),
        };
    }

    private static Dictionary<PluralCategory, CatalogText> PluralForms(JsonElement element, string context)
    {
        var variations = RequireObject(element, context);
        var forms = new Dictionary<PluralCategory, CatalogText>();
        foreach (var variation in variations.EnumerateObject())
        {
            if (variation.Name != "plural")
            {
                throw new InvalidDataException(
                    $"{context}: \"{variation.Name}\" variations can't be converted to .resw; only plural ones can.");
            }
            foreach (var form in RequireObject(variation.Value, context).EnumerateObject())
            {
                var formContext = $"{context}, plural {form.Name}";
                var formElement = RequireObject(form.Value, formContext);
                if (formElement.TryGetProperty("variations", out _))
                {
                    throw new InvalidDataException($"{formContext}: nested variations can't be converted to .resw.");
                }
                forms[Category(form.Name, formContext)] =
                    Text(RequireProperty(formElement, "stringUnit", formContext), formContext);
            }
        }
        // The app falls back to the "other" form whenever a language lacks the one it asks for.
        if (!forms.ContainsKey(PluralCategory.Other))
        {
            throw new InvalidDataException($"{context}: plural variations need an \"other\" form.");
        }
        return forms;
    }

    private static CatalogText Text(JsonElement stringUnit, string context)
    {
        var unit = RequireObject(stringUnit, context);
        var state = RequireString(unit, "state", context);
        return new CatalogText(RequireString(unit, "value", context), IsTranslated: state == "translated");
    }

    private static PluralCategory Category(string name, string context) => name switch
    {
        "zero" => PluralCategory.Zero,
        "one" => PluralCategory.One,
        "two" => PluralCategory.Two,
        "few" => PluralCategory.Few,
        "many" => PluralCategory.Many,
        "other" => PluralCategory.Other,
        _ => throw new InvalidDataException($"{context}: \"{name}\" isn't a CLDR plural category."),
    };

    private static JsonElement RequireObject(JsonElement element, string context) =>
        element.ValueKind == JsonValueKind.Object
            ? element
            : throw new InvalidDataException($"{context}: expected a JSON object, found {element.ValueKind}.");

    private static JsonElement RequireProperty(JsonElement element, string name, string context) =>
        element.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidDataException($"{context}: \"{name}\" is missing.");

    private static string RequireString(JsonElement element, string name, string context) =>
        StringValue(RequireProperty(element, name, context), $"{context}, \"{name}\"");

    private static string StringValue(JsonElement element, string context) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : throw new InvalidDataException($"{context}: expected a string, found {element.ValueKind}.");

    /// <summary>A BCP-47 tag such as "ru", "pt-BR" or "zh-Hans".</summary>
    [GeneratedRegex(@"\A[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*\z")]
    private static partial Regex LanguageCode();
}
