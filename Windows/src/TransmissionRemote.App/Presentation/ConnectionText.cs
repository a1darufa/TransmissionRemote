using TransmissionRemote.ViewModels;

namespace TransmissionRemote.App.Presentation;

/// <summary>The connection pages' sentences, for x:Bind.</summary>
/// <remarks>x:Bind skips a function whose argument is null, so these take values that are never null.</remarks>
internal static class ConnectionText
{
    /// <summary>"Connecting to My Server…"</summary>
    public static string Connecting(string serverName) => StringResources.Format(Strings.ConnectingToEllipsis, serverName);

    /// <summary>"Trying again in 0:12", or "Trying again…" once the attempt is due or under way.</summary>
    public static string Retry(TimeSpan countdown) => countdown > TimeSpan.Zero
        ? StringResources.Format(Strings.TryingAgainIn, DisplayFormatter.Countdown(countdown))
        : Strings.TryingAgainEllipsis;
}
