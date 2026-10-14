using System.Text.Json;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Skills;

// Shared by listings now and CVs later, so CV-to-listing matching is a join on SkillId.
public class Skill
{
    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    // Null = uncategorized. Only developer-order CVs group by it, so non-technical skills can stay null.
    public SkillCategory? Category { get; set; }
    // Only for skills that are words ("Code review" → "Kodegjennomgang"); product names have none and print as Name.
    public List<SkillTranslation> Translations { get; set; } = [];

    public string NameIn(Language language) => Translations.FirstOrDefault(t => t.Language == language)?.Name ?? Name;
}

public class SkillTranslation
{
    public const int MaxNameLength = 200;

    public int Id { get; set; }
    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public Language Language { get; set; }
    public string Name { get; set; } = null!;
}

// Fixed rather than user-defined, so PDF headings can be translated and the AI picks from a closed list.
// Declaration order is the order of the grouped lines on a developer CV.
public enum SkillCategory { Language, Framework, Database, CloudDevops, Tool, Method }

public static class SkillCategories
{
    private static readonly Dictionary<SkillCategory, string> Definitions = new()
    {
        [SkillCategory.Language] = "programming, query and markup languages (C#, TypeScript, SQL, HTML/CSS)",
        [SkillCategory.Framework] = "frameworks and libraries (ASP.NET Core, EF Core, Vue, Nuxt, React)",
        [SkillCategory.Database] = "database products and data stores (PostgreSQL, SQLite, Redis, MongoDB)",
        [SkillCategory.CloudDevops] = "hosting, CI/CD, containers and infrastructure (Azure, AWS, Docker, GitHub Actions, Kubernetes)",
        [SkillCategory.Tool] = "developer tools and software (Git, Visual Studio, Figma, Jira, Postman)",
        [SkillCategory.Method] = "practices and ways of working (Scrum, TDD, API design, code review)",
    };

    // Same UPPER_SNAKE names as the GraphQL enum and the JSON converters (CloudDevops → CLOUD_DEVOPS).
    public static string Value(SkillCategory category) => JsonNamingPolicy.SnakeCaseUpper.ConvertName(category.ToString());

    // Built from the enum, so a prompt can't offer a value the schema doesn't have.
    public static string PromptGuidance(string field) => $"""
        {field}: one of the categories below for software development skills, or null for anything else
          (licences, certifications, trade and service skills such as "Truckførerbevis" or "Kundeservice") and when unsure.
        {string.Join("\n", Enum.GetValues<SkillCategory>().Select(c => $"  {Value(c)}: {Definitions[c]}"))}
          SQL is LANGUAGE; a database product is DATABASE.
        """;
}

public class SkillConfiguration : IEntityTypeConfiguration<Skill>
{
    public void Configure(EntityTypeBuilder<Skill> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => new { s.UserId, s.NormalizedName }).IsUnique();
    }
}

public class SkillTranslationConfiguration : IEntityTypeConfiguration<SkillTranslation>
{
    public void Configure(EntityTypeBuilder<SkillTranslation> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(SkillTranslation.MaxNameLength);
        builder.HasIndex(t => new { t.SkillId, t.Language }).IsUnique();
        builder.HasOne(t => t.Skill).WithMany(s => s.Translations).OnDelete(DeleteBehavior.Cascade);
    }
}
