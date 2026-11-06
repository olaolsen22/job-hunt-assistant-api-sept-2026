using System.Text.Json;

namespace JobHunt.Api.Features.Parsing;

// Built-in calls run in JSON mode and must return raw JSON; external chats render a code block whose copy
// button grabs exactly the JSON to paste back.
public static class JsonReply
{
    public const string ExternalCodeBlock = "Put the JSON in a single ```json code block, with nothing outside it.";

    public static string Rule(string shape, bool external) => external
        ? $"Reply with ONE JSON {shape} and no prose or comments. {ExternalCodeBlock}"
        : $"Reply with ONE JSON {shape} and nothing else: no prose, no code fences, no comments.";

    // Null for an answer that isn't the expected JSON.
    public static T? TryDecode<T>(string text) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(text, JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
    }
}
