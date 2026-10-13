using System.Net;
using JobHunt.Api.Features.LlmProviderKeys;

namespace JobHunt.Api.Tests.Features.LlmProviderKeys;

public class ProviderKeyValidatorTests
{
    [Theory]
    [InlineData("gemini", "generativelanguage.googleapis.com", "x-goog-api-key")]
    [InlineData("anthropic", "api.anthropic.com", "x-api-key")]
    [InlineData("openai", "api.openai.com", "Authorization")]
    [InlineData("mistral", "api.mistral.ai", "Authorization")]
    public async Task Validation_UsesFixedEndpoint_AndPerRequestCredentials(string provider, string host, string header)
    {
        var fake = new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(host, request.RequestUri!.Host);
            Assert.DoesNotContain("test-key", request.RequestUri.ToString());
            Assert.Contains("test-key", request.Headers.GetValues(header).Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        await new ProviderKeyValidator(fake).ValidateAsync(provider, "test-key", default);
        Assert.Equal(1, fake.Calls);
    }

    [Theory]
    [InlineData(401, "invalid-key")]
    [InlineData(403, "invalid-key")]
    [InlineData(429, "rate-limited")]
    [InlineData(500, "provider-error")]
    [InlineData(408, "timeout")]
    public async Task FailedValidation_IsSafeAndDoesNotRetry(int status, string code)
    {
        var fake = new Handler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        { Content = new StringContent("secret test-key raw-provider-body") }));
        var error = await Assert.ThrowsAsync<ProviderKeyException>(() => new ProviderKeyValidator(fake)
            .ValidateAsync("gemini", "test-key", default));
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("test-key", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task InvalidInput_DoesNotCallProvider()
    {
        var fake = new Handler((_, _) => throw new InvalidOperationException());
        var validator = new ProviderKeyValidator(fake);
        foreach (var (provider, key) in new[] { ("custom", "key"), ("gemini", " "), ("openai", new string('x', 4097)) })
            Assert.Equal("invalid-input", (await Assert.ThrowsAsync<ProviderKeyException>(() => validator.ValidateAsync(provider, key, default))).Code);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task NetworkAndTimeout_AreSanitized_AndCallerCancellationPropagates()
    {
        var network = new Handler((_, _) => throw new HttpRequestException("test-key"));
        Assert.Equal("provider-error", (await Assert.ThrowsAsync<ProviderKeyException>(() =>
            new ProviderKeyValidator(network).ValidateAsync("openai", "test-key", default))).Code);
        var timeout = new Handler((_, _) => throw new TaskCanceledException("test-key"));
        Assert.Equal("timeout", (await Assert.ThrowsAsync<ProviderKeyException>(() =>
            new ProviderKeyValidator(timeout).ValidateAsync("openai", "test-key", default))).Code);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProviderKeyValidator(timeout).ValidateAsync("openai", "test-key", canceled.Token));
    }

    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler, IHttpClientFactory
    {
        public int Calls { get; private set; }
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; return send(request, ct); }
    }
}
