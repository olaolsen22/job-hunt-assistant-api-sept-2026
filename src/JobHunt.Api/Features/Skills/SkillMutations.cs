using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Skills;

public class SkillNotFoundException(int id) : Exception($"Skill {id} was not found.");

[MutationType]
public static class SkillMutations
{
    // Null clears it. Set on the lookup, so listings, the profile and new CV drafts all share it.
    [Error<SkillNotFoundException>]
    public static async Task<Skill> SetSkillCategoryAsync(int skillId, SkillCategory? category, AppDbContext db, CancellationToken ct)
    {
        var skill = await db.Skills.FirstOrDefaultAsync(s => s.Id == skillId, ct) ?? throw new SkillNotFoundException(skillId);
        skill.Category = category;
        await db.SaveChangesAsync(ct);
        return skill;
    }
}
