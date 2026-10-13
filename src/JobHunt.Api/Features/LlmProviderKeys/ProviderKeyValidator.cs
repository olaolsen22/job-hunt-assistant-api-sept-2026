using System.Net;
using System.Net.Http.Headers;
using JobHunt.Api.Llm;

namespace JobHunt.Api.Features.LlmProviderKeys;

public class ProviderKeyValidator(IHttpClientFactory clients)
{
    public async Task ValidateAsync(string provider, string apiKey, CancellationToken ct)
    {
        if (!LlmProviders.Supported.Contains(provider) || string.IsNullOrWhiteSpace(apiKey) || apiKey.Length > 4096
            || apiKey.Any(char.IsControl))
            throw new ProviderKeyException("invalid-input");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = clients.CreateClient(LlmProviders.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(LlmProviders.Endpoint(provider), provider == "gemini" ? "v1beta/models?pageSize=1" : provider == "anthropic" ? "v1/models?limit=1" : "models"));
        if (provider == "gemini") request.Headers.Add("x-goog-api-key", apiKey);
        else if (provider == "anthropic")
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
        }
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new ProviderKeyException(CodeFor(response.StatusCode));
        }
        catch (HttpRequestException) { throw new ProviderKeyException("provider-error"); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new ProviderKeyException("timeout"); }
    }

    public static string CodeFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "invalid-key",
        HttpStatusCode.TooManyRequests => "rate-limited",
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => "timeout",
        _ => "provider-error",
    };
}
