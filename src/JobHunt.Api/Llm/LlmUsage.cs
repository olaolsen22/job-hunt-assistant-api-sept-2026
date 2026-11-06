using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.LlmProviderKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Llm;

public enum LlmKeySource { Shared, User }

// One row per LLM call. Gemini has no remaining-quota API, so this is the only view of the free tier.
public class LlmUsage
{
    public int Id { get; set; }
    public Guid? AiOperationId { get; set; }
    public int? AttemptOrdinal { get; set; }
    public string UserId { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string Model { get; set; } = null!;
    public LlmKeySource KeySource { get; set; }
    public Guid? UserProviderKeyId { get; set; }
    public UserProviderKey? UserProviderKey { get; set; }
    public string Operation { get; set; } = null!;
    // Null when the call failed before the provider reported usage.
    public long? InputTokens { get; set; }
    public long? OutputTokens { get; set; }
    public long LatencyMs { get; set; }
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class LlmUsageConfiguration : IEntityTypeConfiguration<LlmUsage>
{
    public void Configure(EntityTypeBuilder<LlmUsage> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
        // Daily usage per user.
        builder.HasIndex(u => new { u.UserId, u.CreatedAt });
        builder.HasOne<AiOperation>().WithMany().HasForeignKey(u => new { u.AiOperationId, u.UserId })
            .HasPrincipalKey(o => new { o.Id, o.UserId }).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(u => new { u.AiOperationId, u.AttemptOrdinal }).IsUnique();
        builder.HasIndex(u => new { u.UserId, u.Provider, u.KeySource, u.CreatedAt });
        builder.ToTable(t => t.HasCheckConstraint("CK_LlmUsages_AttemptIdentity",
            "(\"AiOperationId\" IS NULL AND \"AttemptOrdinal\" IS NULL) OR (\"AiOperationId\" IS NOT NULL AND \"AttemptOrdinal\" IS NOT NULL AND \"AttemptOrdinal\" >= 0)"));
        builder.Property(u => u.KeySource).HasDefaultValue(LlmKeySource.Shared);
        builder.HasOne(u => u.UserProviderKey).WithMany()
            .HasForeignKey(u => new { u.UserProviderKeyId, u.UserId })
            .HasPrincipalKey(k => new { k.Id, k.UserId }).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(u => new { u.KeySource, u.Provider, u.Model, u.CreatedAt });
        builder.HasIndex(u => new { u.UserProviderKeyId, u.Provider, u.Model, u.CreatedAt });
        builder.ToTable(t => t.HasCheckConstraint("CK_LlmUsages_CredentialSource",
            "(\"KeySource\" = 'Shared' AND \"UserProviderKeyId\" IS NULL) OR (\"KeySource\" = 'User' AND \"UserProviderKeyId\" IS NOT NULL)"));
    }
}
