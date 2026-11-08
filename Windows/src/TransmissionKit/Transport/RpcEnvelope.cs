using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TransmissionKit;

/// <summary>Wraps requests in the envelope each dialect expects and unwraps the answers.</summary>
/// <remarks>
/// JSON-RPC: <c>{"jsonrpc": "2.0", "method": "...", "params": {...}, "id": 1}</c><br/>
/// Legacy: <c>{"method": "...", "arguments": {...}, "tag": 1}</c><br/>
/// Requests are built with canonical camelCase keys and converted for the dialect here; answers are
/// converted back to camelCase so one set of models reads both.
/// </remarks>
internal static class RpcEnvelope
{
    public static byte[] Encode(RpcDialect dialect, RpcMethod method, JsonObject? parameters, KeyScope scope, int tag)
    {
        var wireParameters = RenameKeys(parameters ?? [], key => dialect.WireName(key, scope));
        var envelope = new JsonObject { ["method"] = dialect.WireName(method) };
        if (dialect == RpcDialect.JsonRpc)
        {
            envelope["jsonrpc"] = "2.0";
            envelope["params"] = wireParameters;
            envelope["id"] = tag;
        }
        else
        {
            envelope["arguments"] = wireParameters;
            envelope["tag"] = tag;
        }
        return Encoding.UTF8.GetBytes(envelope.ToJsonString());
    }

    /// <summary>Returns the result object, or throws the server's error.</summary>
    public static JsonNode? DecodeResult(byte[] body, RpcDialect dialect)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException error)
        {
            // A wrong RPC path or port often reaches a web page, such as a NAS's admin site.
            if (LooksLikeWebPage(body))
            {
                throw TransmissionException.NotTransmissionRpc();
            }
            throw TransmissionException.InvalidResponse($"not valid JSON: {error.Message}", error);
        }
        if (NormalizeKeys(root) is not JsonObject response)
        {
            throw TransmissionException.InvalidResponse("expected a JSON object");
        }

        if (dialect == RpcDialect.JsonRpc)
        {
            if (response["error"] is JsonObject error)
            {
                // Transmission puts its own, more specific message in data.errorString.
                var message = error.GetObject("data")?.GetString("errorString")
                    ?? error.GetString("message")
                    ?? "unknown error";
                throw TransmissionException.RpcFailure(message);
            }
            return response["result"];
        }

        var result = response.GetString("result")
            ?? throw TransmissionException.InvalidResponse("the response has no result");
        if (result != "success")
        {
            throw TransmissionException.RpcFailure(result);
        }
        return response["arguments"];
    }

    public static bool LooksLikeWebPage(ReadOnlySpan<byte> body)
    {
        var start = body[..Math.Min(body.Length, 256)];
        // The cut may land inside a multi-byte character, so decode leniently.
        return Encoding.UTF8.GetString(start).TrimStart().StartsWith('<');
    }

    /// <summary>
    /// Copies a request's parameters with every object key renamed. Keys of the envelope itself
    /// (method, params, id) are spelled the same in both dialects and never pass through here.
    /// </summary>
    internal static JsonObject RenameKeys(JsonObject source, Func<string, string> rename) =>
        (JsonObject)Rename(source, rename)!;

    /// <summary>Copies an answer with every key converted to camelCase.</summary>
    internal static JsonNode? NormalizeKeys(JsonNode? source) => Rename(source, KeyCase.CamelCase);

    private static JsonNode? Rename(JsonNode? node, Func<string, string> rename)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                var renamedObject = new JsonObject();
                foreach (var (key, value) in jsonObject)
                {
                    // The indexer rather than Add: a server sending both spellings of a key must not crash us.
                    renamedObject[rename(key)] = Rename(value, rename);
                }
                return renamedObject;
            case JsonArray jsonArray:
                var renamedArray = new JsonArray();
                foreach (var item in jsonArray)
                {
                    renamedArray.Add(Rename(item, rename));
                }
                return renamedArray;
            default:
                return node?.DeepClone();
        }
    }
}
