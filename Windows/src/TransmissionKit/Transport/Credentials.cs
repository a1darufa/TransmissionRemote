using System.Net;
using System.Text;

namespace TransmissionKit;

/// <summary>A username and password for the daemon or a proxy.</summary>
public sealed record Credentials(string Username, string Password)
{
    internal string BasicAuthorizationParameter =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));

    internal NetworkCredential ToNetworkCredential() => new(Username, Password);

    /// <summary>Records print every property; this keeps the password out of logs and the debugger.</summary>
    public override string ToString() => $"Credentials {{ Username = {Username}, Password = *** }}";
}

/// <summary>Lets a user force the legacy protocol, for example behind a proxy that mangles JSON-RPC.</summary>
public enum ProtocolPreference
{
    Automatic,
    Legacy,
}

public enum ProxyKind
{
    Http,
    Socks5,
}

/// <summary>A proxy between this computer and the daemon, like Transmission Remote GUI's Proxy tab.</summary>
/// <remarks>
/// .NET answers an HTTP proxy's login request (407) itself, for plain-HTTP servers as well as for
/// HTTPS tunnels, and sends SOCKS 5 logins during the handshake, so unlike the Mac app no header
/// has to be sent up front.
/// </remarks>
public sealed record ProxyConfiguration(ProxyKind Kind, string Host, int Port, Credentials? Credentials = null)
{
    internal WebProxy CreateWebProxy()
    {
        var scheme = Kind == ProxyKind.Http ? "http" : "socks5";
        var host = Host.Trim().Trim('[', ']');
        var urlHost = host.Contains(':', StringComparison.Ordinal) ? $"[{host}]" : host;
        return new WebProxy(new Uri($"{scheme}://{urlHost}:{Port}"))
        {
            // Without this, .NET skips the proxy for addresses it considers local.
            BypassProxyOnLocal = false,
            Credentials = Credentials?.ToNetworkCredential(),
        };
    }
}
