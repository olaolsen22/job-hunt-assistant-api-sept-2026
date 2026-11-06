using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Skills;

// Found or created so features share skill ids.
// New skills are saved through what references them, so an unused name leaves no row.
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
