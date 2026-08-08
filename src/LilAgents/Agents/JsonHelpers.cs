using System.Text.Json;
using System.Text.Json.Nodes;

namespace LilAgents.Agents;

/// <summary>
/// Small conveniences for poking at loosely-typed agent JSON. The CLIs disagree about
/// where they put things and change shape between versions, so every read is defensive.
/// </summary>
public static class JsonHelpers
{
    public static JsonObject? TryParseObject(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var trimmed = line.TrimStart();
        // Cheap rejection so plain-text output does not pay for an exception.
        if (trimmed.Length == 0 || trimmed[0] != '{') return null;

        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? String(this JsonObject? node, string key)
    {
        if (node is null) return null;
        if (!node.TryGetPropertyValue(key, out var value) || value is null) return null;
        try
        {
            return value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static bool? Bool(this JsonObject? node, string key)
    {
        if (node is null) return null;
        if (!node.TryGetPropertyValue(key, out var value) || value is null) return null;
        try
        {
            var kind = value.GetValueKind();
            if (kind == JsonValueKind.True) return true;
            if (kind == JsonValueKind.False) return false;
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static int? Int(this JsonObject? node, string key)
    {
        if (node is null) return null;
        if (!node.TryGetPropertyValue(key, out var value) || value is null) return null;
        try
        {
            return value.GetValueKind() == JsonValueKind.Number ? value.GetValue<int>() : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    public static JsonObject? Object(this JsonObject? node, string key)
    {
        if (node is null) return null;
        return node.TryGetPropertyValue(key, out var value) ? value as JsonObject : null;
    }

    public static JsonArray? Array(this JsonObject? node, string key)
    {
        if (node is null) return null;
        return node.TryGetPropertyValue(key, out var value) ? value as JsonArray : null;
    }

    /// <summary>Flattens a JSON object into the dictionary shape the tool-use event carries.</summary>
    public static IReadOnlyDictionary<string, object?> ToDictionary(this JsonObject? node)
    {
        var result = new Dictionary<string, object?>();
        if (node is null) return result;

        foreach (var (key, value) in node)
        {
            result[key] = value is null ? null : ToClrValue(value);
        }

        return result;
    }

    private static object? ToClrValue(JsonNode node)
    {
        try
        {
            return node.GetValueKind() switch
            {
                JsonValueKind.String => node.GetValue<string>(),
                JsonValueKind.Number => node.GetValue<double>(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => node.ToJsonString()
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return node.ToJsonString();
        }
    }

    /// <summary>
    /// Truncates a tool result for the one-line summary shown in the terminal,
    /// matching the 80-character budget the macOS build uses.
    /// </summary>
    public static string Summarize(string? text, int limit = 80)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var collapsed = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return collapsed.Length <= limit ? collapsed : collapsed[..limit];
    }
}
