using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>A saved daemon connection. Passwords are in <see cref="ICredentialStore"/>, never here.</summary>
/// <remarks>
/// Treat it as a value and change it with <c>with</c>. The setters are public only because the JSON
/// reader needs them to keep the defaults of settings an older file doesn't have.
/// </remarks>
public sealed record ServerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public ServerEndpoint Endpoint { get; set; } = new("");
    public string Username { get; set; } = "";
    public ProtocolPreference ProtocolPreference { get; set; } = ProtocolPreference.Automatic;

    /// <summary>SHA-256 of a self-signed certificate the user explicitly chose to trust.</summary>
    public string? PinnedCertificateFingerprint { get; set; }

    /// <summary>Plain HTTP to a public address exposes the password, so the user opts in per server.</summary>
    public bool AllowsUnencryptedInternetConnection { get; set; }

    /// <summary>Download folders used on this server, for the "Save to" suggestions.</summary>
    public IReadOnlyList<RecentFolder> RecentFolders { get; set; } = [];

    /// <summary>Where this server's folders are on this PC, for Open and Show in folder.</summary>
    public IReadOnlyList<PathMapping> PathMappings { get; set; } = [];

    public ProxySettings Proxy { get; set; } = new();

    /// <summary>The chosen client certificate's subject, for display.</summary>
    public string? ClientCertificateName { get; set; }

    /// <summary>The client certificate's thumbprint in the user's certificate store.</summary>
    public string? ClientCertificateThumbprint { get; set; }

    public string DisplayName => Name.Length > 0 ? Name : Endpoint.Host;

    public bool IsBlockedByTransportPolicy =>
        Endpoint.TransportSecurity == TransportSecurity.UnencryptedInternet && !AllowsUnencryptedInternetConnection;

    /// <summary>Most-used folders first, like Transmission Remote GUI's folder history.</summary>
    public IReadOnlyList<string> SuggestedFolders => RecentFolders
        .OrderByDescending(folder => folder.UseCount)
        .ThenByDescending(folder => folder.LastUsed)
        .Select(folder => folder.Path)
        .ToList();

    /// <summary>
    /// The folder used most recently, which the Add Torrent dialog starts with. Starting with the most-used
    /// one instead kept proposing an old folder long after the user had moved on (transgui #807, #738).
    /// </summary>
    public string? LastUsedFolder => RecentFolders.MaxBy(folder => folder.LastUsed)?.Path;

    /// <summary>Remembers a folder for "Save to", keeping at most <paramref name="limit"/> folders.</summary>
    public ServerProfile WithFolderUse(string path, int limit, DateTimeOffset now)
    {
        var cleaned = ServerFolder.Cleaned(path);
        if (cleaned.Length == 0)
        {
            return this;
        }
        var folders = RecentFolders.ToList();
        var index = folders.FindIndex(folder => ServerFolder.Cleaned(folder.Path) == cleaned);
        if (index >= 0)
        {
            folders[index] = folders[index] with { Path = cleaned, UseCount = folders[index].UseCount + 1, LastUsed = now };
        }
        else
        {
            folders.Add(new RecentFolder(cleaned, 1, now));
        }
        return (this with { RecentFolders = folders }).WithFolderHistoryTrimmed(limit);
    }

    /// <summary>Keeps the <paramref name="limit"/> most recently used folders.</summary>
    public ServerProfile WithFolderHistoryTrimmed(int limit)
    {
        if (RecentFolders.Count <= limit)
        {
            return this;
        }
        var kept = RecentFolders.OrderByDescending(folder => folder.LastUsed).Take(Math.Max(limit, 1)).ToList();
        return this with { RecentFolders = kept };
    }
}

public sealed record RecentFolder(string Path, int UseCount, DateTimeOffset LastUsed);

public enum ProxyValidationError
{
    None,
    MissingHost,
    InvalidPort,
}

/// <summary>A server's proxy, like Transmission Remote GUI's Proxy tab. The password is in the credential store.</summary>
/// <remarks>Treat it as a value, like <see cref="ServerProfile"/>.</remarks>
public sealed record ProxySettings
{
    public bool IsEnabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 8080;
    public bool UsesSocks5 { get; set; }
    public bool RequiresAuthentication { get; set; }
    public string Username { get; set; } = "";

    /// <summary>Why these settings can't be used yet; None when they're fine or the proxy is off.</summary>
    public ProxyValidationError ValidationError => true switch
    {
        _ when !IsEnabled => ProxyValidationError.None,
        _ when string.IsNullOrWhiteSpace(Host) => ProxyValidationError.MissingHost,
        _ when Port is < 1 or > 65_535 => ProxyValidationError.InvalidPort,
        _ => ProxyValidationError.None,
    };

    /// <summary>What the client needs, or null when no proxy should be used.</summary>
    public ProxyConfiguration? ToConfiguration(string? password)
    {
        if (!IsEnabled || ValidationError != ProxyValidationError.None)
        {
            return null;
        }
        var credentials = RequiresAuthentication && Username.Length > 0
            ? new Credentials(Username, password ?? "")
            : null;
        return new ProxyConfiguration(UsesSocks5 ? ProxyKind.Socks5 : ProxyKind.Http, Host.Trim(), Port, credentials);
    }
}

/// <summary>Cleans up a folder path typed for the server before it's sent or remembered.</summary>
public static class ServerFolder
{
    /// <summary>
    /// Trims spaces and trailing slashes, so "/data/ " and "/data" are the same folder in the history and the
    /// daemon never gets a path ending in a separator (transgui #1143). A root ("/", "C:\") keeps its separator.
    /// </summary>
    public static string Cleaned(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var cleaned = path.Trim();
        while (cleaned.Length > 1 && cleaned[^1] is '/' or '\\')
        {
            var withoutLast = cleaned[..^1];
            // Keep "C:\" whole: without the backslash it means "the current folder on C:".
            if (withoutLast.Length == 2 && withoutLast[1] == ':')
            {
                break;
            }
            cleaned = withoutLast;
        }
        return cleaned;
    }
}

/// <summary>Checks a new name for a torrent's top-level file or folder before sending it to the daemon.</summary>
public static class TorrentName
{
    /// <summary>
    /// The trimmed name, or null when Transmission would reject it: empty, "." or "..", or containing "/"
    /// (a rename can't move the item into another folder).
    /// </summary>
    public static string? Validated(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var name = text.Trim();
        return name.Length == 0 || name is "." or ".." || name.Contains('/', StringComparison.Ordinal) ? null : name;
    }
}
