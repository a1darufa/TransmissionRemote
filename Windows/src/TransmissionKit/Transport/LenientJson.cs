using System.Text.Json;
using System.Text.Json.Nodes;

namespace TransmissionKit;

/// <summary>
/// Reads values from Transmission's JSON without failing on the variations between releases.
/// </summary>
/// <remarks>
/// A field may be missing on older servers, <c>wanted</c> was <c>0</c>/<c>1</c> before 4.1, and
/// 4.1.0 briefly sent speed limits as doubles. These helpers read what's there and return null
/// otherwise, so one odd field never hides a whole torrent list.
/// </remarks>
internal static class LenientJson
{
    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    public static long? GetInt64(this JsonObject json, string key)
    {
        switch (ElementAt(json, key))
        {
            case { ValueKind: JsonValueKind.Number } number when number.TryGetInt64(out var integer):
                return integer;
            case { ValueKind: JsonValueKind.Number } number when double.IsFinite(number.GetDouble()):
                var rounded = Math.Round(number.GetDouble(), MidpointRounding.AwayFromZero);
                return rounded is >= long.MinValue and <= long.MaxValue ? (long)rounded : 0;
            case { ValueKind: JsonValueKind.True }:
                return 1;
            case { ValueKind: JsonValueKind.False }:
                return 0;
            default:
                return null;
        }
    }

    public static int? GetInt32(this JsonObject json, string key) =>
        json.GetInt64(key) is { } value ? (int)Math.Clamp(value, int.MinValue, int.MaxValue) : null;

    public static double? GetDouble(this JsonObject json, string key) =>
        ElementAt(json, key) is { ValueKind: JsonValueKind.Number } number ? number.GetDouble() : null;

    public static bool? GetBoolean(this JsonObject json, string key) => ElementAt(json, key) switch
    {
        { ValueKind: JsonValueKind.True } => true,
        { ValueKind: JsonValueKind.False } => false,
        { ValueKind: JsonValueKind.Number } number when number.TryGetInt64(out var integer) => integer != 0,
        _ => null,
    };

    public static string? GetString(this JsonObject json, string key) =>
        ElementAt(json, key) is { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    /// <summary>A Unix timestamp; Transmission uses <c>0</c> for "never".</summary>
    public static DateTimeOffset? GetDate(this JsonObject json, string key)
    {
        var seconds = json.GetInt64(key);
        if (seconds is not > 0 || seconds > MaxUnixSeconds)
        {
            return null;
        }
        return DateTimeOffset.FromUnixTimeSeconds(seconds.Value);
    }

    /// <summary>A numeric enum value, or <paramref name="fallback"/> when it's missing or unknown.</summary>
    public static TEnum GetEnum<TEnum>(this JsonObject json, string key, TEnum fallback)
        where TEnum : struct, Enum
    {
        if (json.GetInt32(key) is not { } rawValue)
        {
            return fallback;
        }
        var value = (TEnum)Enum.ToObject(typeof(TEnum), rawValue);
        return Enum.IsDefined(value) ? value : fallback;
    }

    public static JsonObject? GetObject(this JsonObject json, string key) => json[key] as JsonObject;

    /// <summary>The objects in an array, skipping anything else; empty when the array is missing.</summary>
    public static IEnumerable<JsonObject> GetObjects(this JsonObject json, string key) =>
        json[key] is JsonArray array ? array.OfType<JsonObject>() : [];

    /// <summary>The strings in an array, skipping anything else; empty when the array is missing.</summary>
    public static IReadOnlyList<string> GetStrings(this JsonObject json, string key)
    {
        if (json[key] is not JsonArray array)
        {
            return [];
        }
        return array.OfType<JsonValue>()
            .Select(value => value.TryGetValue(out string? text) ? text : null)
            .OfType<string>()
            .ToList();
    }

    /// <summary>
    /// Values parsed from a response wrap a <see cref="JsonElement"/>, while values built in code (such
    /// as a settings diff) wrap the .NET value. Reading both through an element treats them alike.
    /// </summary>
    private static JsonElement? ElementAt(JsonObject json, string key)
    {
        if (json[key] is not JsonValue value)
        {
            return null;
        }
        if (value.TryGetValue(out JsonElement element))
        {
            return element;
        }
        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.Clone();
    }

    public static JsonArray ToJsonArray(this IEnumerable<int> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    public static JsonArray ToJsonArray(this IEnumerable<string> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
}

/// <summary>Builds request parameters, leaving out values that aren't set.</summary>
internal static class JsonBuilding
{
    public static void PutIfSet(this JsonObject json, string key, int? value)
    {
        if (value is { } number)
        {
            json[key] = number;
        }
    }

    public static void PutIfSet(this JsonObject json, string key, long? value)
    {
        if (value is { } number)
        {
            json[key] = number;
        }
    }

    public static void PutIfSet(this JsonObject json, string key, double? value)
    {
        if (value is { } number)
        {
            json[key] = number;
        }
    }

    public static void PutIfSet(this JsonObject json, string key, bool? value)
    {
        if (value is { } flag)
        {
            json[key] = flag;
        }
    }

    public static void PutIfSet(this JsonObject json, string key, string? value)
    {
        if (value is not null)
        {
            json[key] = value;
        }
    }

    public static void PutIfSet(this JsonObject json, string key, IEnumerable<int>? values)
    {
        if (values is not null)
        {
            json[key] = values.ToJsonArray();
        }
    }

    public static void PutIfSet(this JsonObject json, string key, IEnumerable<string>? values)
    {
        if (values is not null)
        {
            json[key] = values.ToJsonArray();
        }
    }
}
