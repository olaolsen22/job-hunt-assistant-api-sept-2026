using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Llm;

// One row per LLM call, for the usage widget and "would cost" estimates. Gemini has no remaining-quota API,
// so this is the only way to know how much of the free tier is left.
public class LlmUsage
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string Model { get; set; } = null!;
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
        // Daily usage per user (usage widget, guest cap).
        builder.HasIndex(u => new { u.UserId, u.CreatedAt });
    }
}
