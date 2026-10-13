using Anthropic;
using Google.GenAI;
using Google.GenAI.Types;
using JobHunt.Api.Features.LlmProviderKeys;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace JobHunt.Api.Llm;

public static class LlmProviders
{
    public const string HttpClientName = "LlmProvider";
    public const string SharedCredentialId = "shared-gemini";
    public static IReadOnlyList<string> Supported { get; } = ["gemini", "anthropic", "openai", "mistral"];
    public static Uri Endpoint(string provider) => new(provider switch
    {
        "gemini" => "https://generativelanguage.googleapis.com/",
        "anthropic" => "https://api.anthropic.com/",
        "openai" => "https://api.openai.com/v1/",
        "mistral" => "https://api.mistral.ai/v1/",
        _ => throw new ProviderKeyException("invalid-input"),
    });
}

public static class ProviderClients
{
    public static IChatClient Create(string provider, string apiKey, string model, IHttpClientFactory http)
    {
        _ = LlmProviders.Endpoint(provider);
        var transport = http.CreateClient(LlmProviders.HttpClientName);
        try
        {
            IChatClient client = provider switch
            {
                "gemini" => new Client(vertexAI: false, apiKey: apiKey,
                    httpOptions: new HttpOptions { RetryOptions = new HttpRetryOptions { Attempts = 1 } },
                    clientOptions: new ClientOptions { HttpClientFactory = () => transport })
                    .AsIChatClient(model),
                "anthropic" => new AnthropicClient
                {
                    ApiKey = apiKey, BaseUrl = LlmProviders.Endpoint(provider).ToString().TrimEnd('/'),
                    HttpClient = transport, MaxRetries = 0,
                    Timeout = TimeSpan.FromMinutes(2),
                }.AsIChatClient(model),
                "openai" or "mistral" => new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
                {
                    Endpoint = LlmProviders.Endpoint(provider),
                    RetryPolicy = new ClientRetryPolicy(0),
                    Transport = new HttpClientPipelineTransport(transport),
                }).GetChatClient(model).AsIChatClient(),
                _ => throw new ProviderKeyException("invalid-input"),
            };
            return new OwnedClient(client, transport);
        }
        catch { transport.Dispose(); throw; }
    }

    private sealed class OwnedClient(IChatClient inner, HttpClient transport) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => inner.GetResponseAsync(messages, options, cancellationToken);
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            inner.GetStreamingResponseAsync(messages, options, cancellationToken);
        public object? GetService(System.Type serviceType, object? serviceKey = null) => inner.GetService(serviceType, serviceKey);
        public void Dispose() { inner.Dispose(); transport.Dispose(); }
    }
}
