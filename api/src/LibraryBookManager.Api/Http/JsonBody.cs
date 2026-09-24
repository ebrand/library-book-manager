using System.Text.Json;

namespace LibraryBookManager.Api.Http;

/// <summary>
/// Reads request bodies as raw JSON so that missing, null, empty and wrongly-typed fields
/// can each be reported with the field's name, rather than as a generic binding failure.
/// </summary>
public static class JsonBody
{
    public static async Task<JsonElement?> ReadObjectAsync(HttpRequest request)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(request.Body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IResult Malformed() =>
        ApiError.BadRequest("INVALID_REQUEST", "The request body must be a JSON object.");

    public static bool Has(this JsonElement body, string name) =>
        body.TryGetProperty(name, out _);

    /// <summary>Absent, null, or a string that is empty after trimming.</summary>
    public static bool IsEmpty(this JsonElement body, string name) =>
        !body.TryGetProperty(name, out var value)
        || value.ValueKind == JsonValueKind.Null
        || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

    public static string? Text(this JsonElement body, string name) =>
        body.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!.Trim()
            : null;

    public static bool TryInt(this JsonElement body, string name, out int result)
    {
        result = 0;
        return body.TryGetProperty(name, out var value)
               && value.ValueKind == JsonValueKind.Number
               && value.TryGetInt32(out result);
    }
}
