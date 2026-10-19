using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PdfForm.Core;

/// <summary>Resource-bounded strict JSON. Never returns exception messages or payload-derived paths.</summary>
public static class StrictJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = Limits.MaxDepth,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        RespectNullableAnnotations = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static ParseResult<T> Parse<T>(ReadOnlySpan<byte> input, string role) where T : class
    {
        string code = role == "profile" ? "E_PROFILE_JSON" : "E_INPUT_JSON";
        string stage = "syntax";
        ParseResult<T> Fail(string c, string message) => new(null, [new(c, stage, message)]);
        if (input.Length > Limits.MaxJsonBytes) return Fail("E_JSON_SIZE", "JSON input exceeds the byte limit.");
        if (input.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) input = input[3..];
        try
        {
            _ = new UTF8Encoding(false, true).GetString(input);
            var reader = new Utf8JsonReader(input, new JsonReaderOptions
            { MaxDepth = Limits.MaxDepth, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
            var objects = new Stack<HashSet<string>>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject) objects.Push(new(StringComparer.Ordinal));
                else if (reader.TokenType == JsonTokenType.EndObject) objects.Pop();
                else if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    string name = reader.GetString()!;
                    if (!objects.Peek().Add(name)) return Fail("E_JSON_DUPLICATE", "Duplicate JSON member is not allowed.");
                    if (objects.Peek().Count > Limits.MaxKeysOrTargets) return Fail("E_JSON_MEMBERS", "JSON object exceeds the member limit.");
                }
                else if (reader.TokenType == JsonTokenType.String) _ = reader.GetString();
                else if (reader.TokenType == JsonTokenType.Null) return Fail(code, "Null is not supported by this contract.");
            }
            using var document = JsonDocument.Parse(input.ToArray(), new() { MaxDepth = Limits.MaxDepth });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Fail(code, "JSON root must be an object.");
            // Reject lone UTF-16 surrogate escapes rather than normalize them to replacement characters.
            if (!ValidUnicodeEscapes(input)) return Fail("E_JSON_UNICODE", "JSON contains an invalid Unicode scalar escape.");
            var value = JsonSerializer.Deserialize<T>(input, Options);
            return value is null ? Fail(code, "JSON document is empty.") : new(value, Array.Empty<Finding>());
        }
        catch (DecoderFallbackException) { return Fail("E_JSON_UTF8", "JSON must use valid UTF-8 encoding."); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        { return Fail(code, "JSON syntax or member type does not match the contract."); }
    }

    private static bool ValidUnicodeEscapes(ReadOnlySpan<byte> json)
    {
        for (int i = 0; i < json.Length; i++)
        {
            if (json[i] != '\\') continue;
            if (++i >= json.Length) return false;
            if (json[i] != 'u') continue;
            if (i + 4 >= json.Length) return false;
            int code = Hex(json.Slice(i + 1, 4)); i += 4;
            if (code is >= 0xDC00 and <= 0xDFFF) return false;
            if (code is >= 0xD800 and <= 0xDBFF)
            {
                if (i + 6 >= json.Length || json[i + 1] != '\\' || json[i + 2] != 'u') return false;
                int low = Hex(json.Slice(i + 3, 4));
                if (low is < 0xDC00 or > 0xDFFF) return false;
                i += 6;
            }
        }
        return true;
    }
    private static int Hex(ReadOnlySpan<byte> bytes)
    {
        int n = 0;
        foreach (byte c in bytes) n = n * 16 + (c <= '9' ? c - '0' : (c | 32) - 'a' + 10);
        return n;
    }
}
