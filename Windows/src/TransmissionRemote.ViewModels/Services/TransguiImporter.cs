using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TransmissionKit;

namespace TransmissionRemote.ViewModels;

/// <summary>What an import from transgui.ini did. The app turns it into words.</summary>
public sealed record TransguiImportSummary
{
    /// <summary>Servers added, in the file's order.</summary>
    public IReadOnlyList<string> ImportedNames { get; init; } = [];

    /// <summary>Servers skipped because the same daemon is already set up here.</summary>
    public IReadOnlyList<string> AlreadyAddedNames { get; init; } = [];

    public Guid? FirstImportedId { get; init; }

    /// <summary>
    /// The server the import made active, so the app connects to it. Set only when the app had no servers
    /// before: someone setting up the app for the first time continues where they left off in transgui.
    /// </summary>
    public Guid? ActivatedServerId { get; init; }

    /// <summary>An imported server has path mappings; the app can point to Settings → Paths to check them.</summary>
    public bool HasPathMappings { get; init; }

    /// <summary>
    /// An imported server used a client certificate. transgui keeps it as a file path, so it isn't imported:
    /// the user picks it again.
    /// </summary>
    public bool HasClientCertificates { get; init; }

    /// <summary>False when the file had no servers at all, so it's probably not transgui.ini.</summary>
    public bool FoundServers => ImportedNames.Count > 0 || AlreadyAddedNames.Count > 0;
}

/// <summary>
/// Adds the servers from Transmission Remote GUI's transgui.ini, so people moving over don't set everything up
/// again: addresses, passwords (into <see cref="ICredentialStore"/>), proxies, path mappings and folder history,
/// plus the speed menus and folder history limit. transgui's file is only read, never changed.
/// </summary>
/// <remarks>Use from the UI thread, like the stores it writes to.</remarks>
public sealed class TransguiImporter
{
    /// <summary>The highest folder history limit accepted from the file, the same cap as the Mac app's.</summary>
    private const int MaximumFolderHistoryLimit = 999;

    private const string FolderName = "Transmission Remote GUI";

    // transgui names its INI after its executable, which the installer and the zip both call transgui.exe.
    private const string FileName = "transgui.ini";

    private readonly ServerStore _servers;
    private readonly SettingsStore _settings;
    private readonly ILogger _logger;

    public TransguiImporter(ServerStore servers, SettingsStore settings, ILogger<TransguiImporter>? logger = null)
    {
        _servers = servers ?? throw new ArgumentNullException(nameof(servers));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? NullLogger<TransguiImporter>.Instance;
    }

    /// <summary>
    /// Where transgui.ini probably is on this PC, most likely first, for the file picker's starting folder.
    /// </summary>
    /// <remarks>
    /// transgui (main.pas, <c>CheckAppParams</c>) uses <c>--home=</c> when given; otherwise, if an INI named after
    /// the executable sits next to it, that folder ("portable mode"); otherwise Lazarus's
    /// <c>GetAppConfigDirUTF8(False)</c>, which on Windows is <c>CSIDL_LOCAL_APPDATA</c> plus the application
    /// title: <c>%LOCALAPPDATA%\Transmission Remote GUI</c>. Portable copies in the installer's folder under
    /// Program Files are listed too; one unpacked from the zip elsewhere can only be found by browsing.
    /// </remarks>
    public static IReadOnlyList<string> LikelySettingsFiles() => LikelySettingsFiles(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

    /// <inheritdoc cref="LikelySettingsFiles()"/>
    /// <param name="localAppData">%LOCALAPPDATA%.</param>
    /// <param name="programFolders">
    /// Program Files and Program Files (x86): transgui has a 32-bit and a 64-bit installer. Empty ones are skipped.
    /// </param>
    public static IReadOnlyList<string> LikelySettingsFiles(string localAppData, params string[] programFolders)
    {
        ArgumentNullException.ThrowIfNull(localAppData);
        ArgumentNullException.ThrowIfNull(programFolders);
        return new[] { localAppData }
            .Concat(programFolders)
            .Where(folder => !string.IsNullOrEmpty(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(folder => Path.Combine(folder, FolderName, FileName))
            .ToList();
    }

    /// <summary>The first of <see cref="LikelySettingsFiles()"/> that exists, or null.</summary>
    public static string? FindSettingsFile() => LikelySettingsFiles().FirstOrDefault(File.Exists);

    /// <summary>
    /// Adds the servers from the file, skipping ones already set up here (the same host, port and RPC path,
    /// whatever they're called). Check <see cref="TransguiImportSummary.FoundServers"/> before showing the result.
    /// </summary>
    /// <exception cref="IOException">The file couldn't be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file couldn't be read.</exception>
    public TransguiImportSummary Import(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        byte[] contents;
        try
        {
            contents = File.ReadAllBytes(filePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log.TransguiFileUnreadable(_logger, error, StorageFiles.DisplayPath(filePath));
            throw;
        }

        var summary = Import(new TransguiSettings(DecodeText(contents)));
        Log.TransguiImported(
            _logger, summary.ImportedNames.Count, summary.AlreadyAddedNames.Count, StorageFiles.DisplayPath(filePath));
        return summary;
    }

    /// <summary>
    /// transgui writes UTF-8: Lazarus strings are UTF-8, and its INI class writes them unchanged. A file saved
    /// again in an editor may be UTF-16 or carry a byte order mark, and one saved as "ANSI" is in the system's
    /// code page, so those are read too rather than failing on a name with accents.
    /// </summary>
    internal static string DecodeText(byte[] contents)
    {
        ReadOnlySpan<byte> utf8Bom = [0xEF, 0xBB, 0xBF];
        ReadOnlySpan<byte> bytes = contents;
        if (bytes.StartsWith(utf8Bom))
        {
            return Encoding.UTF8.GetString(bytes[utf8Bom.Length..]);
        }
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[2..]);
        }
        if (System.Text.Unicode.Utf8.IsValid(bytes))
        {
            return Encoding.UTF8.GetString(bytes);
        }
        // Code page 0 is the system's ANSI code page on Windows; elsewhere the provider has none, and Latin-1,
        // which maps every byte to a character, is the Mac app's fallback too.
        var ansi = CodePagesEncodingProvider.Instance.GetEncoding(0) ?? Encoding.Latin1;
        return ansi.GetString(bytes);
    }

    private TransguiImportSummary Import(TransguiSettings settings)
    {
        var hadServers = _servers.Servers.Count > 0;
        List<string> imported = [];
        List<string> alreadyAdded = [];
        Guid? firstImportedId = null;
        Guid? transguiActiveId = null;
        var hasPathMappings = false;
        var hasClientCertificates = false;

        foreach (var connection in settings.Connections)
        {
            if (_servers.Servers.Any(server => Matches(server, connection)))
            {
                alreadyAdded.Add(connection.Name);
                continue;
            }
            var profile = Profile(connection);
            _servers.Save(profile);
            if (connection.Password is { } password)
            {
                _servers.SetPassword(profile, password);
            }
            if (connection.Proxy?.Password is { } proxyPassword)
            {
                _servers.SetProxyPassword(profile, proxyPassword);
            }

            imported.Add(connection.Name);
            firstImportedId ??= profile.Id;
            hasPathMappings |= connection.PathMappings.Count > 0;
            hasClientCertificates |= connection.HasClientCertificate;
            if (connection.Name == settings.ActiveConnectionName)
            {
                transguiActiveId = profile.Id;
            }
        }

        ApplyAppWideSettings(settings);
        var activatedId = hadServers ? null : transguiActiveId ?? firstImportedId;
        if (activatedId is { } id)
        {
            _servers.Activate(id);
        }

        return new TransguiImportSummary
        {
            ImportedNames = imported,
            AlreadyAddedNames = alreadyAdded,
            FirstImportedId = firstImportedId,
            ActivatedServerId = activatedId,
            HasPathMappings = hasPathMappings,
            HasClientCertificates = hasClientCertificates,
        };
    }

    /// <summary>
    /// transgui keeps speed menus per connection; this app has one set, taken from the connection transgui
    /// last used.
    /// </summary>
    private void ApplyAppWideSettings(TransguiSettings settings)
    {
        var connections = settings.Connections;
        var active = connections.FirstOrDefault(connection => connection.Name == settings.ActiveConnectionName)
            ?? (connections.Count > 0 ? connections[0] : null);
        _settings.Update(current => current with
        {
            DownloadSpeedPresets = active?.DownloadSpeeds ?? current.DownloadSpeedPresets,
            UploadSpeedPresets = active?.UploadSpeeds ?? current.UploadSpeedPresets,
            FolderHistoryLimit = settings.FolderHistoryLimit is > 0 and var limit
                ? Math.Min(limit, MaximumFolderHistoryLimit)
                : current.FolderHistoryLimit,
        });
    }

    /// <summary>The same daemon, whatever it's called.</summary>
    internal static bool Matches(ServerProfile server, TransguiConnection connection)
    {
        var other = new ServerEndpoint(connection.Host, connection.Port, connection.RpcPath);
        return server.Endpoint.NormalizedHost == other.NormalizedHost
            && server.Endpoint.Port == connection.Port
            && server.Endpoint.RpcPath == connection.RpcPath;
    }

    private static ServerProfile Profile(TransguiConnection connection) => new()
    {
        Name = connection.Name,
        Endpoint = new ServerEndpoint(connection.Host, connection.Port, connection.RpcPath, connection.UsesTls),
        Username = connection.Username,
        PathMappings = [.. connection.PathMappings.Select(
            folder => new PathMapping(folder.RemotePath, folder.LocalPath))],
        RecentFolders = [.. connection.Folders.Select(folder => new RecentFolder(
            folder.Path,
            folder.UseCount,
            // transgui records only the day; folders without a readable date rank as the oldest.
            folder.LastUsed is { } day
                ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
                : DateTimeOffset.MinValue))],
        Proxy = connection.Proxy is { } proxy
            ? new ProxySettings
            {
                IsEnabled = true,
                Host = proxy.Host,
                Port = proxy.Port,
                UsesSocks5 = proxy.UsesSocks5,
                Username = proxy.Username,
                RequiresAuthentication = proxy.Username.Length > 0,
            }
            : new ProxySettings(),
    };
}
