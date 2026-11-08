using System.Text;

namespace TransmissionKit;

/// <summary>The two wire protocols Transmission has shipped.</summary>
/// <remarks>
/// Transmission 4.1 (RPC semver 6.0.0) introduced JSON-RPC 2.0 with snake_case keys. Everything
/// older, including Transmission 3.00 still found on many NAS boxes, only speaks the original
/// "legacy" protocol, which mixes camelCase and kebab-case keys. Newer servers still accept the
/// legacy protocol, but it's deprecated, so the client prefers JSON-RPC whenever it's available.
/// </remarks>
public enum RpcDialect
{
    JsonRpc,
    Legacy,
}

/// <summary>
/// Which family of keys a value belongs to. The legacy protocol spells keys differently depending
/// on the request, so the scope decides how a camelCase name maps to the wire.
/// </summary>
public enum KeyScope
{
    /// <summary>Field names for <c>torrent-get</c>: camelCase in legacy, with a few kebab-case exceptions.</summary>
    TorrentFields,

    /// <summary>Arguments for <c>torrent-set</c>, <c>torrent-add</c>, <c>torrent-remove</c> and friends.</summary>
    TorrentArguments,

    /// <summary>Names and arguments for <c>session-get</c> and <c>session-set</c>: kebab-case in legacy.</summary>
    Session,

    /// <summary>Keys of a bandwidth group: kebab-case in legacy, except <c>honorsSessionLimits</c>.</summary>
    BandwidthGroup,

    /// <summary>Single-word keys spelled the same everywhere (<c>ids</c>, <c>path</c>, <c>name</c>).</summary>
    Plain,
}

public static class RpcDialectExtensions
{
    /// <summary>A short name for logs and the About box.</summary>
    public static string DisplayName(this RpcDialect dialect) =>
        dialect == RpcDialect.JsonRpc ? "JSON-RPC 2.0" : "legacy RPC";

    /// <summary>Converts a canonical camelCase name to the spelling this dialect expects.</summary>
    public static string WireName(this RpcDialect dialect, string camelName, KeyScope scope) =>
        dialect == RpcDialect.JsonRpc ? KeyCase.SnakeCase(camelName) : LegacyKeys.WireName(camelName, scope);

    /// <summary>The method name as this dialect expects it, such as <c>torrent_get</c> or <c>torrent-get</c>.</summary>
    internal static string WireName(this RpcDialect dialect, RpcMethod method) =>
        dialect == RpcDialect.JsonRpc ? method.SnakeCaseName() : method.SnakeCaseName().Replace('_', '-');
}

/// <summary>Spelling rules for the legacy protocol, from the Transmission 3.00 and 4.0.6 RPC specs.</summary>
internal static class LegacyKeys
{
    /// <summary><c>torrent-get</c> fields that the legacy protocol spells in kebab-case.</summary>
    public static readonly IReadOnlySet<string> KebabTorrentFields =
        new HashSet<string>(StringComparer.Ordinal) { "fileCount", "peerLimit", "primaryMimeType" };

    /// <summary>Torrent mutation arguments that the legacy protocol spells in kebab-case.</summary>
    public static readonly IReadOnlySet<string> KebabTorrentArguments = new HashSet<string>(StringComparer.Ordinal)
    {
        "deleteLocalData", "downloadDir", "filesUnwanted", "filesWanted",
        "peerLimit", "priorityHigh", "priorityLow", "priorityNormal",
    };

    /// <summary>Session keys that the legacy protocol spells in camelCase; everything else is kebab-case.</summary>
    public static readonly IReadOnlySet<string> CamelSessionKeys =
        new HashSet<string>(StringComparer.Ordinal) { "seedRatioLimit", "seedRatioLimited" };

    public static string WireName(string camelName, KeyScope scope) => scope switch
    {
        KeyScope.TorrentFields =>
            KebabTorrentFields.Contains(camelName) ? KeyCase.KebabCase(camelName) : camelName,
        KeyScope.TorrentArguments =>
            KebabTorrentArguments.Contains(camelName) ? KeyCase.KebabCase(camelName) : camelName,
        KeyScope.Session =>
            CamelSessionKeys.Contains(camelName) ? camelName : KeyCase.KebabCase(camelName),
        KeyScope.BandwidthGroup =>
            camelName == "honorsSessionLimits" ? camelName : KeyCase.KebabCase(camelName),
        _ => camelName,
    };
}

/// <summary>Small, dependency-free key case conversions.</summary>
internal static class KeyCase
{
    /// <summary>
    /// Legacy keys whose acronyms don't survive a mechanical conversion: <c>isUTP</c> in legacy and
    /// <c>is_utp</c> in JSON-RPC must land on the same property.
    /// </summary>
    private static readonly Dictionary<string, string> CamelCaseExceptions =
        new(StringComparer.Ordinal) { ["isUTP"] = "isUtp" };

    private static readonly char[] Separators = ['_', '-'];

    public static string SnakeCase(string camelName) => Separated(camelName, '_');

    public static string KebabCase(string camelName) => Separated(camelName, '-');

    /// <summary>
    /// Normalizes any server key (snake_case, kebab-case or camelCase) to camelCase, so one set of
    /// models reads both dialects.
    /// </summary>
    public static string CamelCase(string wireKey)
    {
        if (CamelCaseExceptions.TryGetValue(wireKey, out var exception))
        {
            return exception;
        }
        if (wireKey.IndexOfAny(Separators) < 0)
        {
            return wireKey;
        }

        var parts = wireKey.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return wireKey;
        }
        var result = new StringBuilder(parts[0], wireKey.Length);
        foreach (var part in parts.Skip(1))
        {
            result.Append(char.ToUpperInvariant(part[0])).Append(part.AsSpan(1));
        }
        return result.ToString();
    }

    private static string Separated(string camelName, char separator)
    {
        var result = new StringBuilder(camelName.Length + 4);
        foreach (var character in camelName)
        {
            if (char.IsUpper(character))
            {
                if (result.Length > 0)
                {
                    result.Append(separator);
                }
                result.Append(char.ToLowerInvariant(character));
            }
            else
            {
                result.Append(character);
            }
        }
        return result.ToString();
    }
}
