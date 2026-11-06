using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Listings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobHunt.Api.Features.Skills;

// User-defined. SortOrder orders the grouped lines on a developer CV; Description tells the AI what belongs.
public class SkillCategory
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 500;

    public int Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NormalizedName { get; set; } = null!;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    // Headings for CVs in another language; without one the PDF prints Name.
    public List<SkillCategoryTranslation> Translations { get; set; } = [];

    public string NameIn(Language language) => Translations.FirstOrDefault(t => t.Language == language)?.Name ?? Name;
}

public class SkillCategoryTranslation
{
    public int Id { get; set; }
    public int SkillCategoryId { get; set; }
    public SkillCategory SkillCategory { get; set; } = null!;
    public Language Language { get; set; }
    public string Name { get; set; } = null!;
}

public class SkillCategoryConfiguration : IEntityTypeConfiguration<SkillCategory>
{
    public void Configure(EntityTypeBuilder<SkillCategory> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.UserId, c.NormalizedName }).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(SkillCategory.MaxNameLength);
        builder.Property(c => c.Description).HasMaxLength(SkillCategory.MaxDescriptionLength);
    }
}

public class SkillCategoryTranslationConfiguration : IEntityTypeConfiguration<SkillCategoryTranslation>
{
    public void Configure(EntityTypeBuilder<SkillCategoryTranslation> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(SkillCategory.MaxNameLength);
        builder.HasIndex(t => new { t.SkillCategoryId, t.Language }).IsUnique();
        builder.HasOne(t => t.SkillCategory).WithMany(c => c.Translations).OnDelete(DeleteBehavior.Cascade);
    }
}

// Starter categories for a new account. Keep in step with the AddUserSkillCategories migration.
internal static class StarterSkillCategories
{
    internal static readonly (string Name, string Norwegian, string Description)[] All =
    [
        ("Languages", "Programmeringsspråk", "programming, query and markup languages (C#, TypeScript, SQL, HTML/CSS); SQL belongs here, not under databases"),
        ("Frameworks", "Rammeverk", "frameworks and libraries (ASP.NET Core, EF Core, Vue, Nuxt, React)"),
        ("Databases", "Databaser", "database products and data stores (PostgreSQL, SQLite, Redis, MongoDB)"),
        ("Cloud & DevOps", "Sky og DevOps", "hosting, CI/CD, containers and infrastructure (Azure, AWS, Docker, GitHub Actions, Kubernetes)"),
        ("Testing", "Testing", "test frameworks and tools (xUnit, NUnit, Jest, Vitest, Playwright, Cypress); TDD is a method, not testing"),
        ("AI & ML", "KI og maskinlæring", "AI, LLM and machine learning (OpenAI/Gemini APIs, Microsoft.Extensions.AI, LangChain, RAG, prompt engineering, PyTorch)"),
        ("Tools", "Verktøy", "developer tools and software (Git, Visual Studio, Figma, Jira, Postman)"),
        ("Methods", "Metoder", "practices and ways of working (Scrum, TDD, API design, code review)"),
    ];

    internal static IEnumerable<SkillCategory> For(string userId) => All.Select((c, i) => new SkillCategory
    {
        UserId = userId, Name = c.Name, NormalizedName = Names.Normalize(c.Name), Description = c.Description, SortOrder = i,
        Translations = [new SkillCategoryTranslation { Language = Language.Norwegian, Name = c.Norwegian }],
    });
}

public static class SkillCategoryPrompt
{
    // Built from the user's own categories, so the AI can only pick a name the server resolves.
    public static async Task<string> GuidanceAsync(AppDbContext db, string field, CancellationToken ct)
    {
        var categories = await db.SkillCategories.OrderBy(c => c.SortOrder)
            .Select(c => new { c.Name, c.Description }).ToListAsync(ct);
        if (categories.Count == 0) return $"{field}: always null.";
        var lines = categories.Select(c =>
            $"  \"{c.Name}\"{(c.Description is null ? "" : $": {c.Description}")}");
        return $"""
            {field}: the exact name of one category below, or null for anything that fits none (licences,
              certifications, trade and service skills such as "Truckførerbevis" or "Kundeservice") and when unsure.
              Never invent category names.
            {string.Join("\n", lines)}
            """;
    }

    // Prompt suggestions only fill an uncategorized skill; an unknown name is ignored rather than failing the save.
    public static async Task<Dictionary<string, SkillCategory>> ByNormalizedNameAsync(
        IEnumerable<string?> names, AppDbContext db, CancellationToken ct)
    {
        var normalized = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Names.Normalize(n!)).Distinct().ToList();
        if (normalized.Count == 0) return [];
        return await db.SkillCategories.Where(c => normalized.Contains(c.NormalizedName)).ToDictionaryAsync(c => c.NormalizedName, ct);
    }
}
