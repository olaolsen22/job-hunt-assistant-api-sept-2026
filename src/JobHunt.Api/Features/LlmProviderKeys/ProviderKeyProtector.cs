using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using JobHunt.Api.Llm;

namespace JobHunt.Api.Features.LlmProviderKeys;

public class ProviderKeyException(string code) : Exception(MessageFor(code))
{
    public string Code { get; } = code;

    public static string MessageFor(string code) => code switch
    {
        LlmErrorCodes.Forbidden => "Only an Owner can manage provider keys.",
        LlmErrorCodes.InvalidInput => "Choose a supported provider and supply an API key of 1–4096 characters.",
        LlmErrorCodes.Conflict => "The provider key changed concurrently. Refresh and try again.",
        _ => LlmErrorCodes.MessageFor(code),
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
            throw new ProviderKeyException(LlmErrorCodes.CredentialUnavailable);
        try { return For(key).Unprotect(key.ProtectedKey); }
        catch (CryptographicException) { throw new ProviderKeyException(LlmErrorCodes.CredentialUnavailable); }
    }
}
