#:package Microsoft.Extensions.AI.OpenAI
#:package OpenAI
// A dev tool, not a published app; file-based apps default to AOT and its trim warnings.
#:property PublishAot=false

// Prompt A/B/C runner (docs/llm-prompting-research-2026-11-21.md §10).
// Dev-only: no API, DB or app code. Prompt files hold real profile data, so keep folders under .local/.
//
//   dotnet run tools/prompt-eval/prompt-eval.cs -- <folder> [--runs 3]   send <folder>/*.txt, blind the replies, check
//   dotnet run tools/prompt-eval/prompt-eval.cs -- <folder> --blind <dir> blind hand-run replies named <variant>-<n>.txt
//   dotnet run tools/prompt-eval/prompt-eval.cs -- <folder> --check-only  re-run the checks on <folder>/replies
//
// Env: PROMPT_EVAL_MODEL, PROMPT_EVAL_API_KEY, optional PROMPT_EVAL_ENDPOINT (default: Gemini's OpenAI-compatible API).
using System.ClientModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using OpenAI;

if (args.Length == 0 || args[0].StartsWith("--"))
{
    Console.Error.WriteLine("usage: prompt-eval <folder> [--runs N] [--blind <dir>] [--check-only]");
    return 1;
}
var folder = Path.GetFullPath(args[0]);
var replies = Path.Combine(folder, "replies");
var keyFile = Path.Combine(folder, "key.json");
Directory.CreateDirectory(replies);
var key = File.Exists(keyFile)
    ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(keyFile))!
    : new Dictionary<string, Entry>();

string Option(string name) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault()
    ?? throw new ArgumentException($"{name} needs a value");

if (args.Contains("--blind"))
{
    // Hand-run replies are named after their variant, e.g. A-1.txt; the copies get random names.
    foreach (var file in Directory.GetFiles(Path.GetFullPath(Option("--blind")), "*.txt"))
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var dash = name.LastIndexOf('-');
        Save(dash > 0 ? name[..dash] : name, dash > 0 && int.TryParse(name[(dash + 1)..], out var run) ? run : 1,
            File.ReadAllText(file), "manual");
    }
}
else if (!args.Contains("--check-only"))
{
    var runs = args.Contains("--runs") ? int.Parse(Option("--runs")) : 3;
    var model = Env("PROMPT_EVAL_MODEL");
    var endpoint = Environment.GetEnvironmentVariable("PROMPT_EVAL_ENDPOINT")
        ?? "https://generativelanguage.googleapis.com/v1beta/openai/";
    IChatClient chat = new OpenAIClient(new ApiKeyCredential(Env("PROMPT_EVAL_API_KEY")),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
        .GetChatClient(model).AsIChatClient();
    foreach (var file in Directory.GetFiles(folder, "*.txt").Order())
    {
        var variant = Path.GetFileNameWithoutExtension(file);
        var prompt = File.ReadAllText(file);
        for (var run = 1; run <= runs; run++)
        {
            // Provider default temperature, so repeats differ like real use.
            var response = await chat.GetResponseAsync(prompt);
            Save(variant, run, response.Text, model);
            Console.WriteLine($"{variant} run {run}: {response.Text.Length} characters");
        }
    }
}

File.WriteAllText(keyFile, JsonSerializer.Serialize(key, new JsonSerializerOptions { WriteIndented = true }));
WriteChecks();
return 0;

void Save(string variant, int run, string text, string source)
{
    var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(3));
    File.WriteAllText(Path.Combine(replies, $"{id}.txt"), text);
    key[id] = new Entry(variant, run, source);
}

static string Env(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Set {name} first.");

// Mechanical rule checks only; quality is scored by hand in scores.csv without looking at key.json.
void WriteChecks()
{
    var rows = new StringBuilder("id,jsonValid,bullets,over140,emDashes,bannedWords,headline,summaryChars,answers\n");
    foreach (var file in Directory.GetFiles(replies, "*.txt").Order())
    {
        var text = File.ReadAllText(file);
        var json = ParseJson(text);
        var bullets = json is null ? [] : Strings(json, "bullets", "text").ToList();
        var content = json?["content"];
        var answers = json?["answers"] as JsonArray;
        rows.Append(string.Join(",", Path.GetFileNameWithoutExtension(file), json is not null, bullets.Count,
            bullets.Count(b => b.Length > 140), text.Count(c => c == '—'), Banned.Count(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)),
            content?["headline"]?.GetValue<string>() is { Length: > 0 }, content?["summary"]?.GetValue<string>()?.Length ?? 0,
            answers?.Count ?? 0)).Append('\n');
    }
    File.WriteAllText(Path.Combine(folder, "checks.csv"), rows.ToString());
    var scores = Path.Combine(folder, "scores.csv");
    var scored = File.Exists(scores) ? File.ReadAllLines(scores).Skip(1).Select(l => l.Split(',')[0]).ToHashSet() : [];
    File.AppendAllLines(scores, (File.Exists(scores) ? [] : new[] { "id,quality(1-5),notes" })
        .Concat(key.Keys.Where(id => !scored.Contains(id)).Order().Select(id => $"{id},,")));
    Console.WriteLine($"{key.Count} replies; see checks.csv, score blind in scores.csv, unblind with key.json.");
}

// External chats wrap the JSON in a ```json block; take the outermost object.
static JsonNode? ParseJson(string text)
{
    var start = text.IndexOf('{');
    var end = text.LastIndexOf('}');
    if (start < 0 || end <= start) return null;
    try { return JsonNode.Parse(text[start..(end + 1)]); }
    catch (JsonException) { return null; }
}

// Every node[list][i][field] string anywhere in the tree, e.g. each bullet's text.
static IEnumerable<string> Strings(JsonNode node, string list, string field) => node switch
{
    JsonObject o => o.SelectMany(p => p.Key == list && p.Value is JsonArray items
        ? items.OfType<JsonObject>().Select(i => i[field]?.GetValue<string>()).OfType<string>()
        : p.Value is null ? [] : Strings(p.Value, list, field)),
    JsonArray a => a.OfType<JsonNode>().SelectMany(n => Strings(n, list, field)),
    _ => [],
};

// Kept in step with CvGuidance.Style by hand: this tool doesn't reference the API project.
partial class Program
{
    static readonly string[] Banned =
    [
        "spearheaded", "leveraged", "utilized", "orchestrated", "passionate", "results-driven", "results-oriented",
        "innovative", "seamless", "cutting-edge", "dynamic", "resultatorientert", "løsningsorientert",
        "brennende engasjert", "dynamisk", "innovativ",
    ];
}

record Entry(string Variant, int Run, string Source);
