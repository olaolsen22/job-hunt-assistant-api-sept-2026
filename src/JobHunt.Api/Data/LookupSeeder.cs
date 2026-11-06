using JobHunt.Api.Features.JobRoles;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.Skills;
using Microsoft.EntityFrameworkCore;
// Hot Chocolate also has a global Location type (a position in a query document).
using Location = JobHunt.Api.Features.Locations.Location;

namespace JobHunt.Api.Data;

// Starter lookups, so pickers aren't empty and the parse prompt has canonical names. Only adds missing rows.
public static class LookupSeeder
{
    private static readonly string[] JobRoles =
    [
        "Frontend Developer", "Backend Developer", "Full Stack Developer", "Mobile Developer",
        "DevOps Engineer", "Site Reliability Engineer", "Platform Engineer", "Cloud Engineer",
        "Data Engineer", "Data Scientist", "Machine Learning Engineer", "QA Engineer",
        "Security Engineer", "Embedded Developer", "Software Architect", "Engineering Manager",
        "UX Designer", "Product Manager",
    ];

    private static readonly string[] Skills =
    [
        "JavaScript", "TypeScript", "HTML", "CSS", "Vue.js", "Nuxt", "React", "Next.js", "Angular",
        "Node.js", "C#", ".NET", "ASP.NET Core", "Java", "Spring Boot", "Kotlin", "Python", "Go", "Rust",
        "SQL", "PostgreSQL", "Microsoft SQL Server", "MongoDB", "Redis", "GraphQL", "REST",
        "Docker", "Kubernetes", "Terraform", "Microsoft Azure", "Amazon Web Services", "Google Cloud",
        "Git", "CI/CD", "Linux", "Agile", "Scrum", "Figma",
    ];

    // As written locally, like the parse prompt asks. Null = the whole country, for remote jobs.
    private static readonly string?[] NorwegianCities =
    [
        null, "Oslo", "Bergen", "Trondheim", "Stavanger", "Kristiansand", "Tromsø", "Drammen",
        "Fredrikstad", "Sandnes", "Bodø", "Ålesund", "Tønsberg", "Moss", "Lillestrøm",
    ];

    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        // No signed-in user at startup, so the user filters would hide every row; filter by UserId by hand.
        foreach (var userId in await db.Users.Select(u => u.Id).ToListAsync(ct))
        {
            var categories = await db.JobRoleCategories.IgnoreQueryFilters()
                .Where(c => c.UserId == userId).ToDictionaryAsync(c => c.NormalizedName, ct);
            foreach (var name in StarterJobRoleCategories.Names)
            {
                var normalized = Names.Normalize(name);
                if (categories.ContainsKey(normalized)) continue;
                var category = new JobRoleCategory { UserId = userId, Name = name, NormalizedName = normalized };
                db.JobRoleCategories.Add(category);
                categories.Add(normalized, category);
            }

            var roles = await db.JobRoles.IgnoreQueryFilters()
                .Where(r => r.UserId == userId).Select(r => r.NormalizedName).ToListAsync(ct);
            db.JobRoles.AddRange(JobRoles
                .Where(name => !roles.Contains(Names.Normalize(name)))
                .Select(name => new JobRole
                {
                    UserId = userId, Name = name, NormalizedName = Names.Normalize(name),
                    Category = StarterJobRoleCategories.TechnicalRoles.Contains(Names.Normalize(name)) ? categories["IT"] : null,
                }));

            // Only into an empty list, so a starter category the user deleted doesn't come back on the next start.
            if (!await db.SkillCategories.IgnoreQueryFilters().AnyAsync(c => c.UserId == userId, ct))
                db.SkillCategories.AddRange(StarterSkillCategories.For(userId));

            var skills = await db.Skills.IgnoreQueryFilters()
                .Where(s => s.UserId == userId).Select(s => s.NormalizedName).ToListAsync(ct);
            db.Skills.AddRange(Skills
                .Where(name => !skills.Contains(Names.Normalize(name)))
                .Select(name => new Skill { UserId = userId, Name = name, NormalizedName = Names.Normalize(name) }));

            var cities = await db.Locations.IgnoreQueryFilters()
                .Where(l => l.UserId == userId && l.CountryCode == "NO").Select(l => l.NormalizedCity).ToListAsync(ct);
            db.Locations.AddRange(NorwegianCities
                .Select(city => (City: city, Normalized: city is null ? "" : Names.Normalize(city)))
                .Where(c => !cities.Contains(c.Normalized))
                .Select(c => new Location { UserId = userId, City = c.City, NormalizedCity = c.Normalized, CountryCode = "NO" }));

            // Created up front so the profile query and mutations never have to handle a missing profile.
            if (!await db.Profiles.IgnoreQueryFilters().AnyAsync(p => p.UserId == userId, ct))
                db.Profiles.Add(new Profile { UserId = userId });
        }
        await db.SaveChangesAsync(ct);
    }
}
