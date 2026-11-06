using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using JobHunt.Api.Llm;

namespace JobHunt.Api.Features.AiSafeguards;

public class AiRequestDigests(IDataProtectionProvider protection)
{
    public string Hash(object input) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
    private IDataProtector For(string userId) => protection.CreateProtector("JobHunt.AiRequest.v1", userId);
    public string Protect(string userId, string digest) => For(userId).Protect(digest);
    public bool Matches(string userId, string protectedDigest, string digest)
    {
        try { return For(userId).Unprotect(protectedDigest) == digest; }
        catch (CryptographicException) { throw new AiSafetyException(LlmErrorCodes.OperationInterrupted); }
    }
}
