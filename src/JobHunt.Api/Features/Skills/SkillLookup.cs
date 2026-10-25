using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Skills;

// Found case-insensitively or created, so listings, companies and the profile share skill ids. A new skill isn't
// added to the context here: it's saved through whatever references it, so an unused name never becomes a row.
public static class SkillLookup
{
    // Names arrive validated and trimmed. Returns one skill per distinct name, in order.
    public static async Task<List<Skill>> FindOrCreateAsync(IEnumerable<string> names, AppDbContext db, CancellationToken ct)
    {
        var distinct = names.DistinctBy(Names.Normalize).ToList();
        var normalized = distinct.Select(Names.Normalize).ToList();
        var saved = await db.Skills.Where(s => normalized.Contains(s.NormalizedName)).ToDictionaryAsync(s => s.NormalizedName, ct);
        return [.. distinct.Select(name =>
        {
            var key = Names.Normalize(name);
            return saved.GetValueOrDefault(key)
                // Created earlier in this request and attached to an entity that isn't saved yet.
                ?? db.Skills.Local.FirstOrDefault(s => s.UserId == db.CurrentUserId && s.NormalizedName == key)
                ?? new Skill { UserId = db.CurrentUserId!, Name = name, NormalizedName = key };
        })];
    }
}
