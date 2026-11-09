using System.Globalization;
using TransmissionKit;

namespace TransmissionRemote.App.Presentation;

/// <summary>
/// The localized sentence for an error. The core and the view models only say what kind of error it was; their
/// own messages are English and meant for the log.
/// </summary>
internal static class ErrorMessages
{
    /// <summary>The message for an error; empty for none.</summary>
    public static string For(Exception? error) => error switch
    {
        null => "",
        TransmissionException transmission => For(transmission),
        _ => Strings.SomethingUnexpectedWentWrongTheLogFiles,
    };

    private static string For(TransmissionException error) => error.Kind switch
    {
        TransmissionErrorKind.InvalidEndpoint => Format(Strings.TheServerAddressIsInvalid, error.Detail ?? ""),
        TransmissionErrorKind.Unauthorized => Strings.TheServerRejectedTheUsernameOrPassword,
        TransmissionErrorKind.ProxyUnauthorized => Strings.TheProxyRejectedTheUsernameOrPassword,
        TransmissionErrorKind.IpNotAllowed => Strings.TheServerRefusedThisComputersIPAddress,
        TransmissionErrorKind.HostNotAllowed => Strings.TheServerDoesNotAcceptThisHostname,
        TransmissionErrorKind.UntrustedCertificate => Strings.TheServersTLSCertificateIsNotTrusted,
        TransmissionErrorKind.HttpsHandshakeFailed => Strings.TheServerDidNotAcceptASecureHTTPS,
        TransmissionErrorKind.RedirectRefused => Strings.TheServerTriedToRedirectTheConnectionToA,
        // A status code is a number, not a quantity: no digit grouping.
        TransmissionErrorKind.HttpStatus => Format(
            Strings.TheServerRespondedWithHTTPCheckTheRPC,
            error.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "?"),
        // The daemon's own words, as the Mac app shows them.
        TransmissionErrorKind.RpcFailure => error.Detail ?? error.Message,
        TransmissionErrorKind.UnsupportedServer => Format(Strings.TransmissionIsTooOldVersion240OrNewerIs, error.Detail ?? ""),
        TransmissionErrorKind.InvalidResponse => Format(Strings.TheServerSentAResponseThatCouldNotBeRead, error.Detail ?? ""),
        TransmissionErrorKind.NotTransmissionRpc => Strings.TheServerAnsweredWithAWebPageInsteadOf,
        TransmissionErrorKind.Network => ForNetwork(error.NetworkProblem),
        _ => Strings.SomethingUnexpectedWentWrongTheLogFiles,
    };

    private static string ForNetwork(NetworkProblem? problem) => problem switch
    {
        NetworkProblem.Timeout => Strings.TheServerDidNotAnswerInTime,
        NetworkProblem.NameNotResolved => Strings.TheServersNameCouldNotBeFoundCheckThe,
        NetworkProblem.CannotConnect => Strings.CouldNotConnectToTheServerCheckThat,
        NetworkProblem.ConnectionLost => Strings.TheConnectionToTheServerWasLost,
        NetworkProblem.ProxyFailed => Strings.CouldNotConnectThroughTheProxyServer,
        _ => Strings.TheConnectionToTheServerFailed,
    };

    private static string Format(string format, string argument) =>
        StringResources.Format(format, argument);
}
