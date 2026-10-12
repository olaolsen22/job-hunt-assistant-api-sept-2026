using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.JobRoles;

public class InvalidJobRoleCategoryException(string message) : Exception(message);
public class JobRoleCategoryNotFoundException(int id) : Exception($"Job role category {id} was not found.");
public class JobRoleNotFoundException(int id) : Exception($"Job role {id} was not found.");
public class DuplicateJobRoleCategoryException(int existingCategoryId) : Exception("A category with this name already exists.")
{
    public int ExistingCategoryId { get; } = existingCategoryId;
}

[MutationType]
public static class JobRoleCategoryMutations
{
    [Error<InvalidJobRoleCategoryException>]
    public static async Task<JobRoleCategory> CreateJobRoleCategoryAsync(string name, AppDbContext db, CancellationToken ct)
    {
        name = ValidateName(name);
        var normalized = Names.Normalize(name);
        var existing = await db.JobRoleCategories.FirstOrDefaultAsync(c => c.NormalizedName == normalized, ct);
        if (existing is not null) return existing;
        var category = new JobRoleCategory { UserId = db.CurrentUserId!, Name = name, NormalizedName = normalized };
        db.JobRoleCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    [Error<InvalidJobRoleCategoryException>]
    [Error<JobRoleCategoryNotFoundException>]
    [Error<DuplicateJobRoleCategoryException>]
    public static async Task<JobRoleCategory> UpdateJobRoleCategoryAsync(int id, string name, AppDbContext db, CancellationToken ct)
    {
        var category = await db.JobRoleCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new JobRoleCategoryNotFoundException(id);
        name = ValidateName(name);
        var normalized = Names.Normalize(name);
        var existingId = await db.JobRoleCategories.Where(c => c.Id != id && c.NormalizedName == normalized)
            .Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
        if (existingId is { } duplicate) throw new DuplicateJobRoleCategoryException(duplicate);
        category.Name = name;
        category.NormalizedName = normalized;
        await db.SaveChangesAsync(ct);
        return category;
    }

    [Error<JobRoleNotFoundException>]
    [Error<JobRoleCategoryNotFoundException>]
    public static async Task<JobRole> SetJobRoleCategoryAsync(int jobRoleId, int? categoryId, AppDbContext db, CancellationToken ct)
    {
        var role = await db.JobRoles.FirstOrDefaultAsync(r => r.Id == jobRoleId, ct)
            ?? throw new JobRoleNotFoundException(jobRoleId);
        var category = categoryId is { } id
            ? await db.JobRoleCategories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new JobRoleCategoryNotFoundException(id)
            : null;
        role.Category = category;
        role.CategoryId = categoryId;
        await db.SaveChangesAsync(ct);
        return role;
    }

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 100)
            throw new InvalidJobRoleCategoryException("Category names must contain 1 to 100 characters.");
        return name;
    }
}
