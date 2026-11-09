using Microsoft.UI.Xaml;

namespace TransmissionRemote.App.Presentation;

/// <summary>Visibility from a condition, for x:Bind: <c>Visibility="{x:Bind presentation:Visible.When(...)}"</c>.</summary>
internal static class Visible
{
    public static Visibility When(bool condition) => condition ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Unless(bool condition) => When(!condition);

    public static Visibility WhenText(string? text) => When(!string.IsNullOrEmpty(text));
}
