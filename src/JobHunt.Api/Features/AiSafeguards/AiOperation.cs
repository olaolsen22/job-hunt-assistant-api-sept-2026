using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.AiSafeguards;

public enum AiOperationStatus { Running, Succeeded, Failed, Interrupted }

public class AiOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public string UserId { get; set; } = null!;
    public string Kind { get; set; } = null!;
    public string ProtectedRequestDigest { get; set; } = null!;
    public string? ProtectedWorkDigest { get; set; }
    public string Provider { get; set; } = null!;
    public string Model { get; set; } = null!;
    public string CredentialId { get; set; } = null!;
    public AiOperationStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public Guid? ParseJobId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class AiOperationConfiguration : IEntityTypeConfiguration<AiOperation>
{
    public void Configure(EntityTypeBuilder<AiOperation> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasAlternateKey(o => new { o.Id, o.UserId });
        builder.HasIndex(o => new { o.UserId, o.RequestId }).IsUnique();
        builder.HasIndex(o => o.UserId).IsUnique().HasFilter("\"Status\" = 'Running'");
        builder.HasOne<ParseJob>().WithMany().HasForeignKey(o => new { o.ParseJobId, o.UserId })
            .HasPrincipalKey(j => new { j.Id, j.UserId }).OnDelete(DeleteBehavior.NoAction);
    }
}

public class UserLlmLimit
{
    public string UserId { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public int? DailyRequestLimit { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UserLlmLimitConfiguration : IEntityTypeConfiguration<UserLlmLimit>
{
    public void Configure(EntityTypeBuilder<UserLlmLimit> builder)
    {
        builder.HasKey(l => new { l.UserId, l.Provider });
        builder.HasIndex(l => l.UserId);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(l => l.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t => t.HasCheckConstraint("CK_UserLlmLimits_Nonnegative", "\"DailyRequestLimit\" IS NULL OR \"DailyRequestLimit\" >= 0"));
    }
}
