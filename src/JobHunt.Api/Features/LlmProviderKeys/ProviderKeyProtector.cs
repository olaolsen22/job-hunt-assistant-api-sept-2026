using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace JobHunt.Api.Features.LlmProviderKeys;

public class ProviderKeyException(string code) : Exception(MessageFor(code))
{
    public string Code { get; } = code;

    public static string MessageFor(string code) => code switch
    {
        "invalid-key" => "The provider rejected the API key. Update it or switch models.",
        "credential-unavailable" => "The saved API key is unavailable. Re-enter it or switch models.",
        "rate-limited" => "The AI provider's rate limit was reached. Try later or switch models.",
        "timeout" => "The AI took too long to answer. Try again.",
        "forbidden" => "Only an Owner can manage provider keys.",
        "invalid-input" => "Choose a supported provider and supply an API key of 1–4096 characters.",
        "conflict" => "The provider key changed concurrently. Refresh and try again.",
        _ => "The AI provider couldn't be reached. Try again later.",
    };
}

public class ProviderKeyProtector(IDataProtectionProvider protection)
{
    private IDataProtector For(UserProviderKey key) => protection.CreateProtector(
        "JobHunt.ProviderKey.v1", key.UserId, key.Provider, key.Id.ToString("D"));

    public string Protect(UserProviderKey key, string secret) => For(key).Protect(secret);

    public string Unprotect(UserProviderKey key)
    {
        if (key.RetiredAt is not null || key.ProtectedKey is null)
            throw new ProviderKeyException("credential-unavailable");
        try { return For(key).Unprotect(key.ProtectedKey); }
        catch (CryptographicException) { throw new ProviderKeyException("credential-unavailable"); }
    }
}
