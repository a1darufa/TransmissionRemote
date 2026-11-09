using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Presentation;

/// <summary>The connection pages' sentences, for x:Bind.</summary>
internal static class ConnectionText
{
    /// <summary>"Connecting to My Server…"</summary>
    public static string Connecting(string serverName) => StringResources.Format(Strings.ConnectingToEllipsis, serverName);

    /// <summary>"Trying again in 0:12", or "Trying again…" while the attempt is under way.</summary>
    public static string Retry(TimeSpan? countdown) => countdown is { } remaining
        ? StringResources.Format(Strings.TryingAgainIn, DisplayFormatter.Countdown(remaining))
        : Strings.TryingAgainEllipsis;
}
