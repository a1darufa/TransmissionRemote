using System.Globalization;
using Microsoft.UI.Xaml;

namespace TransmissionRemote.App.Startup;

/// <summary>
/// Debug builds only: environment variables that override the Windows theme and display language, to take review
/// screenshots without changing the PC's settings. Release builds ignore them and always follow Windows.
/// </summary>
internal static class DevelopmentOverrides
{
    /// <summary><c>TRANSMISSIONREMOTE_THEME</c>: <c>Light</c> or <c>Dark</c>.</summary>
    public static ApplicationTheme? Theme
    {
        get
        {
#if DEBUG
            return Enum.TryParse<ApplicationTheme>(Environment.GetEnvironmentVariable("TRANSMISSIONREMOTE_THEME"), out var theme)
                ? theme
                : null;
#else
            return null;
#endif
        }
    }

    /// <summary>
    /// <c>TRANSMISSIONREMOTE_LANGUAGE</c>, such as <c>ru</c>: the app's strings and formats in that language. Call
    /// before anything loads a string.
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public static void ApplyLanguage()
    {
        if (Environment.GetEnvironmentVariable("TRANSMISSIONREMOTE_LANGUAGE") is not { Length: > 0 } language)
        {
            return;
        }
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
