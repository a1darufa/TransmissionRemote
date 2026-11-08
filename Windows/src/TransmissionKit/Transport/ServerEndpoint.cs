using System.Net;
using System.Net.Sockets;

namespace TransmissionKit;

/// <summary>Where a Transmission daemon's RPC interface lives.</summary>
public sealed record ServerEndpoint(
    string Host,
    int Port = ServerEndpoint.DefaultPort,
    string RpcPath = ServerEndpoint.DefaultRpcPath,
    bool UsesTls = false)
{
    public const int DefaultPort = 9091;
    public const string DefaultRpcPath = "/transmission/rpc";

    /// <summary>The host without surrounding whitespace or IPv6 brackets, in lower case.</summary>
    public string NormalizedHost => Host.Trim().Trim('[', ']').ToLowerInvariant();

    /// <summary>How well the connection protects credentials and traffic in transit.</summary>
    public TransportSecurity TransportSecurity =>
        UsesTls ? TransportSecurity.Tls : TransportSecurityClassifier.Classify(NormalizedHost);

    /// <summary>Validates the endpoint and builds the RPC URL.</summary>
    /// <exception cref="TransmissionException">With <see cref="TransmissionErrorKind.InvalidEndpoint"/>.</exception>
    public Uri ToUri()
    {
        var host = NormalizedHost;
        if (host.Length == 0)
        {
            throw TransmissionException.InvalidEndpoint("the host is empty.");
        }
        if (host.Contains("://", StringComparison.Ordinal))
        {
            throw TransmissionException.InvalidEndpoint(
                "enter just the host name or IP address, without \"http://\".");
        }
        if (!host.All(IsAllowedHostCharacter))
        {
            throw TransmissionException.InvalidEndpoint("the host contains characters that aren't allowed.");
        }
        if (Port is < 1 or > 65_535)
        {
            throw TransmissionException.InvalidEndpoint("the port must be between 1 and 65535.");
        }
        if (!RpcPath.StartsWith('/'))
        {
            throw TransmissionException.InvalidEndpoint("the RPC path must start with \"/\".");
        }

        var scheme = UsesTls ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        var urlHost = host;
        if (TransportSecurityClassifier.IsIPv6(host))
        {
            // An IPv6 zone such as fe80::1%eth0 must be written %25 inside a URL.
            urlHost = $"[{host.Replace("%", "%25", StringComparison.Ordinal)}]";
        }
        if (!Uri.TryCreate($"{scheme}://{urlHost}:{Port}{RpcPath}", UriKind.Absolute, out var uri))
        {
            throw TransmissionException.InvalidEndpoint("the address couldn't be turned into a URL.");
        }
        return uri;
    }

    private static bool IsAllowedHostCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || "-._:%".Contains(character, StringComparison.Ordinal);
}

/// <summary>
/// Transmission's RPC server only speaks plain HTTP, and Basic authentication sends the password
/// base64-encoded with every request. Plain HTTP is fine inside a trusted network or an encrypted
/// tunnel such as Tailscale, but not across the open internet.
/// </summary>
public enum TransportSecurity
{
    /// <summary>HTTPS, for example through a reverse proxy in front of the daemon.</summary>
    Tls,
    Loopback,

    /// <summary>Private LAN ranges, link-local addresses and local-only host names.</summary>
    LocalNetwork,

    /// <summary>Tailscale's address ranges and MagicDNS names: plain HTTP inside a WireGuard tunnel.</summary>
    Tailscale,

    /// <summary>Plain HTTP to a public address: credentials can be read by anyone on the path.</summary>
    UnencryptedInternet,
}

public static class TransportSecurityExtensions
{
    public static bool IsProtected(this TransportSecurity security) =>
        security != TransportSecurity.UnencryptedInternet;
}

internal static class TransportSecurityClassifier
{
    private static readonly string[] LocalSuffixes = [".local", ".lan", ".home.arpa", ".internal"];

    /// <summary>fd7a:115c:a1e0::/48</summary>
    private static readonly byte[] TailscaleIPv6Prefix = [0xFD, 0x7A, 0x11, 0x5C, 0xA1, 0xE0];

    public static TransportSecurity Classify(string host)
    {
        if (IsIPv4(host, out var ipv4))
        {
            return Classify(ipv4.GetAddressBytes());
        }
        if (IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ClassifyIPv6(address.GetAddressBytes());
        }
        return ClassifyHostName(host);
    }

    public static bool IsIPv6(string host) =>
        host.Contains(':', StringComparison.Ordinal)
        && IPAddress.TryParse(host, out var address)
        && address.AddressFamily == AddressFamily.InterNetworkV6;

    /// <summary>
    /// Only dotted quads count: <see cref="IPAddress.TryParse(string?, out IPAddress?)"/> would also
    /// read a dotless name made of digits as an address.
    /// </summary>
    private static bool IsIPv4(string host, out IPAddress address)
    {
        address = IPAddress.None;
        return host.Count(character => character == '.') == 3
            && IPAddress.TryParse(host, out address!)
            && address.AddressFamily == AddressFamily.InterNetwork;
    }

    private static TransportSecurity Classify(byte[] bytes) => (bytes[0], bytes[1]) switch
    {
        (127, _) => TransportSecurity.Loopback,
        (100, >= 64 and <= 127) => TransportSecurity.Tailscale, // 100.64.0.0/10, Tailscale's CGNAT range
        (10, _) or (172, >= 16 and <= 31) or (192, 168) or (169, 254) => TransportSecurity.LocalNetwork,
        _ => TransportSecurity.UnencryptedInternet,
    };

    private static TransportSecurity ClassifyIPv6(byte[] bytes)
    {
        if (bytes.AsSpan(0, 15).IndexOfAnyExcept((byte)0) < 0 && bytes[15] == 1)
        {
            return TransportSecurity.Loopback;
        }
        if (bytes.AsSpan().StartsWith(TailscaleIPv6Prefix))
        {
            return TransportSecurity.Tailscale;
        }
        if ((bytes[0] & 0xFE) == 0xFC)
        {
            return TransportSecurity.LocalNetwork; // fc00::/7 unique local
        }
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
        {
            return TransportSecurity.LocalNetwork; // fe80::/10 link-local
        }
        return TransportSecurity.UnencryptedInternet;
    }

    private static TransportSecurity ClassifyHostName(string hostName)
    {
        if (hostName == "localhost")
        {
            return TransportSecurity.Loopback;
        }
        if (hostName.EndsWith(".ts.net", StringComparison.Ordinal))
        {
            return TransportSecurity.Tailscale;
        }
        var isLocal = !hostName.Contains('.', StringComparison.Ordinal)
            || LocalSuffixes.Any(suffix => hostName.EndsWith(suffix, StringComparison.Ordinal));
        return isLocal ? TransportSecurity.LocalNetwork : TransportSecurity.UnencryptedInternet;
    }
}
