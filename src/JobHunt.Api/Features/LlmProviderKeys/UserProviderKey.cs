using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.LlmProviderKeys;

// Retired records retain attribution, but never their secret.
public class UserProviderKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string? ProtectedKey { get; set; }
    public string Last4 { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? RetiredAt { get; set; }
}

public class UserProviderKeyConfiguration : IEntityTypeConfiguration<UserProviderKey>
{
    public void Configure(EntityTypeBuilder<UserProviderKey> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(k => k.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasAlternateKey(k => new { k.Id, k.UserId });
        builder.HasIndex(k => new { k.UserId, k.Provider }).IsUnique().HasFilter("\"RetiredAt\" IS NULL");
        builder.Property(k => k.Provider).HasMaxLength(50);
        builder.Property(k => k.Last4).HasMaxLength(4);
        builder.ToTable(t => t.HasCheckConstraint("CK_UserProviderKeys_SecretLifecycle",
            "(\"RetiredAt\" IS NULL AND \"ProtectedKey\" IS NOT NULL) OR (\"RetiredAt\" IS NOT NULL AND \"ProtectedKey\" IS NULL)"));
    }
}
