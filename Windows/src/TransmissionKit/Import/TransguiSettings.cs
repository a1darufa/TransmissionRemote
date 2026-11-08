using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace TransmissionKit;

/// <summary>
/// Servers and folder history from Transmission Remote GUI's transgui.ini, so people moving over don't have to set
/// everything up again. Only reads; transgui's file is left untouched.
/// </summary>
public sealed class TransguiSettings
{
    // FolderCount comes from the file; a broken one mustn't make the import loop for ages.
    private const int MaximumFolders = 999;

    /// <summary>
    /// Reads the file's text. Anything missing or unreadable is skipped rather than failing the import.
    /// </summary>
    public TransguiSettings(string iniText)
    {
        var ini = new IniFile(iniText);
        Connections = [.. ConnectionNames(ini).Select(name => Connection(name, ini)).OfType<TransguiConnection>()];
        ActiveConnectionName = NonEmpty(ini.Value("CurHost", "Hosts"));
        FolderHistoryLimit = ParseInt(ini.Value("MaxFoldersHistory", "Interface"));
    }

    public IReadOnlyList<TransguiConnection> Connections { get; }

    /// <summary>The connection transgui last used.</summary>
    public string? ActiveConnectionName { get; }

    public int? FolderHistoryLimit { get; }

    /// <summary>transgui lists connections under [Hosts]; very old versions had a single [Connection].</summary>
    private static List<string> ConnectionNames(IniFile ini)
    {
        var count = ParseInt(ini.Value("Count", "Hosts")) ?? 0;
        List<string> listed = [.. Enumerable.Range(1, Math.Max(count, 1))
            .Select(index => ini.Value($"Host{index}", "Hosts"))
            .OfType<string>()
            .Where(name => name.Length > 0)];
        if (listed.Count > 0)
        {
            return listed;
        }

        const string sectionPrefix = "Connection.";
        List<string> sectionNames = [.. ini.SectionNames
            .Where(name => name.StartsWith(sectionPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(name => name[sectionPrefix.Length..])];
        if (sectionNames.Count > 0)
        {
            return sectionNames;
        }

        return ini.Value("Host", "Connection") is { } host ? [host] : [];
    }

    private static TransguiConnection? Connection(string name, IniFile ini)
    {
        var section = ini.HasSection($"Connection.{name}") ? $"Connection.{name}" : "Connection";
        string? Text(string key) => ini.Value(key, section);
        bool Flag(string key) => Text(key) is { } text && IniFile.ParseBool(text);

        if (Text("Host")?.Trim() is not { Length: > 0 } host)
        {
            return null;
        }
        return new TransguiConnection
        {
            Name = name,
            Host = host,
            Port = ParseInt(Text("Port")) ?? 9091,
            RpcPath = NonEmpty(Text("RpcPath")) ?? "/transmission/rpc",
            UsesTls = Flag("UseSSL"),
            Username = Text("UserName") ?? "",
            Password = DecodedPassword(Text("Password")),
            Proxy = Flag("UseProxy") ? Proxy(ini, section, usesSocks5: Flag("UseSockProxy")) : null,
            PathMappings = Text("PathMap") is { } pathMap ? MappedFolders(pathMap) : [],
            DownloadSpeeds = NonEmpty(Text("DownSpeeds")),
            UploadSpeeds = NonEmpty(Text("UpSpeeds")),
            HasClientCertificate = !string.IsNullOrEmpty(Text("CertFile")),
            Folders = Folders(ini, name),
        };
    }

    private static TransguiProxy? Proxy(IniFile ini, string section, bool usesSocks5)
    {
        if (ini.Value("ProxyHost", section)?.Trim() is not { Length: > 0 } host)
        {
            return null;
        }
        return new TransguiProxy(
            host,
            ParseInt(ini.Value("ProxyPort", section)) ?? 8080,
            usesSocks5,
            ini.Value("ProxyUser", section) ?? "",
            DecodedPassword(ini.Value("ProxyPass", section)));
    }

    /// <summary>transgui saves passwords as base64, and "-" when it should ask every time.</summary>
    private static string? DecodedPassword(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || stored == "-")
        {
            return null;
        }
        var bytes = new byte[stored.Length]; // base64 always decodes to fewer bytes than it has characters
        if (!Convert.TryFromBase64String(stored, bytes, out var length) || !Utf8.IsValid(bytes.AsSpan(0, length)))
        {
            return null;
        }
        return Encoding.UTF8.GetString(bytes, 0, length);
    }

    /// <summary>"remote=local" pairs separated by "|".</summary>
    private static List<TransguiMappedFolder> MappedFolders(string text)
    {
        var folders = new List<TransguiMappedFolder>();
        foreach (var pair in text.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            if (pair.Split('=', 2, StringSplitOptions.TrimEntries) is [{ Length: > 0 } remote, { Length: > 0 } local])
            {
                folders.Add(new TransguiMappedFolder(remote, local));
            }
        }
        return folders;
    }

    private static List<TransguiFolder> Folders(IniFile ini, string connectionName)
    {
        var section = $"AddTorrent.{connectionName}";
        var count = Math.Min(ParseInt(ini.Value("FolderCount", section)) ?? 0, MaximumFolders);
        var folders = new List<TransguiFolder>();
        for (var index = 0; index < count; index++)
        {
            if (NonEmpty(ini.Value($"Folder{index}", section)) is not { } path)
            {
                continue;
            }
            folders.Add(new TransguiFolder(
                path,
                UseCount: Math.Max(ParseInt(ini.Value($"FolHit{index}", section)) ?? 1, 1),
                LastUsed: ParseDate(ini.Value($"LastDt{index}", section))));
        }
        return folders;
    }

    /// <summary>transgui writes "dd.mm.yyyy".</summary>
    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "d.M.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>A whole number exactly as written; surrounding spaces or anything else make it unreadable.</summary>
    private static int? ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? NonEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;
}

/// <summary>A server connection from transgui.ini.</summary>
public sealed class TransguiConnection
{
    public required string Name { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    public required string RpcPath { get; init; }

    public bool UsesTls { get; init; }

    public string Username { get; init; } = "";

    /// <summary>Null when no password is saved, including when transgui asks for it on every connect.</summary>
    public string? Password { get; init; }

    public TransguiProxy? Proxy { get; init; }

    public IReadOnlyList<TransguiMappedFolder> PathMappings { get; init; } = [];

    /// <summary>
    /// Speed menu items in kB/s, comma-separated, the same format <see cref="SpeedLimitPresets"/> reads.
    /// </summary>
    public string? DownloadSpeeds { get; init; }

    /// <inheritdoc cref="DownloadSpeeds"/>
    public string? UploadSpeeds { get; init; }

    /// <summary>
    /// transgui keeps the client certificate as a file path, so it's reported rather than imported: the user picks
    /// the certificate again.
    /// </summary>
    public bool HasClientCertificate { get; init; }

    /// <summary>The connection's Add Torrent folder history.</summary>
    public IReadOnlyList<TransguiFolder> Folders { get; init; } = [];
}

/// <summary>A connection's proxy from transgui.ini.</summary>
public sealed record TransguiProxy(string Host, int Port, bool UsesSocks5, string Username, string? Password)
{
    // The generated ToString would print the password into any log the proxy ends up in.
    public override string ToString() => $"{(UsesSocks5 ? "SOCKS5" : "HTTP")} proxy {Host}:{Port}";
}

/// <summary>A server folder and the same folder on this computer, from transgui's path map.</summary>
public sealed record TransguiMappedFolder(string RemotePath, string LocalPath);

/// <summary>A download folder from transgui's Add Torrent history.</summary>
/// <param name="Path">The folder on the server.</param>
/// <param name="UseCount">How often it was picked, at least 1.</param>
/// <param name="LastUsed">When it was last picked, if transgui recorded a readable date.</param>
public sealed record TransguiFolder(string Path, int UseCount, DateOnly? LastUsed);

/// <summary>
/// A minimal reader for the INI files Lazarus writes: [Section] headers and key=value lines. Section and key names
/// compare case-insensitively, like Lazarus's TIniFile.
/// </summary>
internal sealed class IniFile
{
    private static readonly HashSet<string> TrueValues = new(["1", "true", "yes"], StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _sectionNames = [];

    public IniFile(string text)
    {
        var current = "";
        foreach (var rawLine in text.AsSpan().EnumerateLines())
        {
            var line = rawLine.Trim().ToString();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1];
                if (!_sections.ContainsKey(current))
                {
                    _sectionNames.Add(current);
                }
                Section(current);
                continue;
            }
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }
            Section(current)[line[..separator].Trim()] = line[(separator + 1)..];
        }
    }

    /// <summary>Section names as first written, in file order.</summary>
    public IReadOnlyList<string> SectionNames => _sectionNames;

    public string? Value(string key, string section) =>
        _sections.GetValueOrDefault(section)?.GetValueOrDefault(key);

    public bool HasSection(string section) => _sections.ContainsKey(section);

    public static bool ParseBool(string text) => TrueValues.Contains(text.Trim());

    private Dictionary<string, string> Section(string name)
    {
        if (!_sections.TryGetValue(name, out var section))
        {
            section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections[name] = section;
        }
        return section;
    }
}
