using System.Net;
using System.Text;
using System.Text.Json;

namespace JobHunt.Api.Tests;

// SDKs use this transport so tests never validate keys or perform inference on the network.
public class FakeProviderHandler : HttpMessageHandler
{
    public HttpStatusCode ValidationStatus { get; set; } = HttpStatusCode.OK;
    public HttpStatusCode InferenceStatus { get; set; } = HttpStatusCode.OK;
    public string ResponseText { get; set; } = "{}";
    public int ValidationCalls { get; private set; }
    public int InferenceCalls { get; private set; }
    public Func<CancellationToken, Task>? BeforeInference { get; set; }
    public string? LastBody { get; private set; }
    public string? LastHost { get; private set; }
    public string? LastCredential { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastHost = request.RequestUri!.Host;
        LastCredential = request.Headers.Authorization?.Parameter
            ?? (request.Headers.TryGetValues("x-goog-api-key", out var google) ? google.Single() : null)
            ?? (request.Headers.TryGetValues("x-api-key", out var anthropic) ? anthropic.Single() : null);
        var validation = request.Method == HttpMethod.Get;
        if (validation) ValidationCalls++;
        else
        {
            InferenceCalls++; LastBody = await request.Content!.ReadAsStringAsync(ct);
            if (BeforeInference is { } before) await before(ct);
        }
        var status = validation ? ValidationStatus : InferenceStatus;
        object body = validation ? new { data = Array.Empty<object>() } : LastHost switch
        {
            "generativelanguage.googleapis.com" => new
            {
                candidates = new[] { new { content = new { role = "model", parts = new[] { new { text = ResponseText } } }, finishReason = "STOP" } },
                usageMetadata = new { promptTokenCount = 100, candidatesTokenCount = 20, thoughtsTokenCount = 5, totalTokenCount = 125 },
            },
            "api.anthropic.com" => new
            {
                id = "msg_fake", type = "message", role = "assistant", model = "configured-model", stop_reason = "end_turn",
                content = new[] { new { type = "text", text = ResponseText } }, usage = new { input_tokens = 100, output_tokens = 20 },
            },
            _ => new
            {
                id = "chatcmpl-fake", @object = "chat.completion", created = 1, model = "configured-model",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = ResponseText }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 100, completion_tokens = 20, total_tokens = 120, completion_tokens_details = new { reasoning_tokens = 5 } },
            },
        };
        if (status != HttpStatusCode.OK) body = new { error = new { type = "authentication_error", message = "private provider body", code = "invalid_api_key" } };
        return new HttpResponseMessage(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    }
}
