using JobHunt.Api.Llm;
using Microsoft.Extensions.AI;

namespace JobHunt.Api.Tests.Features.LlmProviderKeys;

public class ProviderClientTests
{
    [Theory]
    [InlineData("gemini")]
    [InlineData("openai")]
    [InlineData("mistral")]
    public void AllAdapters_ConstructWithoutNetwork(string provider)
    {
        using var client = ProviderClients.Create(provider, "fake-key", "configured-model", new NoNetworkFactory());
        Assert.NotNull(client.GetService<ChatClientMetadata>());
    }

    private sealed class NoNetworkFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new NoNetworkHandler());
    }
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected network request");
    }
}
