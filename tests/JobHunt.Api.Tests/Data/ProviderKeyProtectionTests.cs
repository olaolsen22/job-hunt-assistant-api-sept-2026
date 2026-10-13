using JobHunt.Api.Features.LlmProviderKeys;
using Microsoft.AspNetCore.DataProtection;

namespace JobHunt.Api.Tests.Data;

public class ProviderKeyProtectionTests
{
    [Fact]
    public void KeyRing_SurvivesReconstruction_AndBindsCredentialPurposes()
    {
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "jobhunt-protection-" + Guid.NewGuid()));
        var missing = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "jobhunt-missing-" + Guid.NewGuid()));
        try
        {
            var key = new UserProviderKey { UserId = "first", Provider = "gemini" };
            var first = new ProviderKeyProtector(DataProtectionProvider.Create(directory, b => b.SetApplicationName("JobHunt.Api")));
            key.ProtectedKey = first.Protect(key, "private-test-key");
            Assert.DoesNotContain("private-test-key", key.ProtectedKey);
            var second = new ProviderKeyProtector(DataProtectionProvider.Create(directory, b => b.SetApplicationName("JobHunt.Api")));
            Assert.Equal("private-test-key", second.Unprotect(key));
            key.UserId = "other";
            Assert.Equal("credential-unavailable", Assert.Throws<ProviderKeyException>(() => second.Unprotect(key)).Code);
            key.UserId = "first";
            key.Provider = "anthropic";
            Assert.Throws<ProviderKeyException>(() => second.Unprotect(key));
            key.Provider = "gemini";
            var id = key.Id;
            key.Id = Guid.NewGuid();
            Assert.Throws<ProviderKeyException>(() => second.Unprotect(key));
            key.Id = id;
            var lost = new ProviderKeyProtector(DataProtectionProvider.Create(missing, b => b.SetApplicationName("JobHunt.Api")));
            Assert.Throws<ProviderKeyException>(() => lost.Unprotect(key));
            key.RetiredAt = DateTime.UtcNow;
            Assert.Throws<ProviderKeyException>(() => second.Unprotect(key));
        }
        finally
        {
            if (directory.Exists) directory.Delete(true);
            if (missing.Exists) missing.Delete(true);
        }
    }
}
