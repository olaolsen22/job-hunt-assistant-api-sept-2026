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
    public const string Gemini = "gemini";
    public const string Anthropic = "anthropic";
    public const string OpenAI = "openai";
    public const string Mistral = "mistral";
    public static IReadOnlyList<string> Supported { get; } = [Gemini, Anthropic, OpenAI, Mistral];
    public static Uri Endpoint(string provider) => new(provider switch
    {
        Gemini => "https://generativelanguage.googleapis.com/",
        Anthropic => "https://api.anthropic.com/",
        OpenAI => "https://api.openai.com/v1/",
        Mistral => "https://api.mistral.ai/v1/",
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
                LlmProviders.Gemini => new Client(vertexAI: false, apiKey: apiKey,
                    httpOptions: new HttpOptions { RetryOptions = new HttpRetryOptions { Attempts = 1 } },
                    clientOptions: new ClientOptions { HttpClientFactory = () => transport })
                    .AsIChatClient(model),
                LlmProviders.Anthropic => new AnthropicClient
                {
                    ApiKey = apiKey, BaseUrl = LlmProviders.Endpoint(provider).ToString().TrimEnd('/'),
                    HttpClient = transport, MaxRetries = 0,
                    Timeout = TimeSpan.FromMinutes(2),
                }.AsIChatClient(model),
                LlmProviders.OpenAI or LlmProviders.Mistral => new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions
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
