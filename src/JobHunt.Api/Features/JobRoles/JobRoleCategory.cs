using JobHunt.Api.Features.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.JobRoles;

public class JobRoleCategory
{
    public const int MaxNameLength = 100;

    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
}

public class JobRoleCategoryConfiguration : IEntityTypeConfiguration<JobRoleCategory>
{
    public void Configure(EntityTypeBuilder<JobRoleCategory> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.UserId, c.NormalizedName }).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(JobRoleCategory.MaxNameLength);
    }
}

internal static class StarterJobRoleCategories
{
    internal static readonly string[] Names =
    [
        "IT", "Healthcare", "Education", "Business & Administration", "Sales & Marketing",
        "Logistics", "Trades & Manufacturing", "Hospitality",
    ];

    internal static readonly HashSet<string> TechnicalRoles = new(StringComparer.Ordinal)
    {
        "FRONTEND DEVELOPER", "BACKEND DEVELOPER", "FULL STACK DEVELOPER", "MOBILE DEVELOPER",
        "DEVOPS ENGINEER", "SITE RELIABILITY ENGINEER", "PLATFORM ENGINEER", "CLOUD ENGINEER",
        "DATA ENGINEER", "DATA SCIENTIST", "MACHINE LEARNING ENGINEER", "QA ENGINEER",
        "SECURITY ENGINEER", "EMBEDDED DEVELOPER", "SOFTWARE ARCHITECT",
    };
}
