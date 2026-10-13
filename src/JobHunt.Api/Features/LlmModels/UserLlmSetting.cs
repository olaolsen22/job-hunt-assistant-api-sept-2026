using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.LlmModels;

// At most one row per user. Provider is stored now so a second provider doesn't need a table rebuild later.
public class UserLlmSetting
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Provider { get; set; } = null!;
    // Null means "use the configured default". Can go stale when config drops a model; the resolver then falls back.
    public string? DefaultModel { get; set; }
}

public class UserLlmSettingConfiguration : IEntityTypeConfiguration<UserLlmSetting>
{
    public void Configure(EntityTypeBuilder<UserLlmSetting> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.UserId).IsUnique();
        builder.Property(s => s.Provider).HasMaxLength(50);
        builder.Property(s => s.DefaultModel).HasMaxLength(LlmModelResolver.MaxModelLength);
    }
}
