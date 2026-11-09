using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using TransmissionKit;

namespace TransmissionRemote.App;

/// <summary>
/// Loads the interface strings from Strings/&lt;language&gt;/Resources.resw through MRT Core. Use the generated
/// <see cref="Strings"/> class rather than names: it turns a renamed string into a build error.
/// </summary>
/// <remarks>
/// A string a language lacks comes from en-US, string by string, like macOS falls back to English.
/// </remarks>
internal static class StringResources
{
    private const string LanguageQualifier = "Language";

    private static readonly ResourceMap Resources = new ResourceManager().MainResourceMap.GetSubtree("Resources");

    /// <summary>The string in the user's language; empty if no language has it.</summary>
    public static string Get(string name) => Resources.TryGetValue(name)?.ValueAsString ?? string.Empty;

    /// <summary>Fills a string's {0}, {1}... placeholders, formatting numbers and dates for the user's region.</summary>
    public static string Format(string format, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);

    /// <summary>
    /// The form of a plural string for <paramref name="count"/>, still a format string: <c>Files_Few</c> in Russian
    /// for 3. The plural rules are those of the language the string actually came from, so a string that falls back
    /// to English uses English forms.
    /// </summary>
    public static string PluralForm(string family, long count)
    {
        var other = Resources.TryGetValue(PluralRules.ResourceName(family, PluralCategory.Other));
        if (other is null)
        {
            return string.Empty;
        }
        var language = other.QualifierValues.TryGetValue(LanguageQualifier, out var tag)
            ? tag
            : CultureInfo.CurrentUICulture.Name;
        var form = Resources.TryGetValue(PluralRules.ResourceName(family, PluralRules.CategoryFor(language, count)));
        return string.IsNullOrEmpty(form?.ValueAsString) ? other.ValueAsString : form.ValueAsString;
    }
}
