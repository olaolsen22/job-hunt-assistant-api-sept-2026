using Microsoft.AspNetCore.Identity;

namespace JobHunt.Api.Features.Auth;

public static class AuthSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();

        foreach (var role in new[] { Roles.Owner, Roles.Guest })
        {
            if (!await roleManager.RoleExistsAsync(role))
                ThrowIfFailed(await roleManager.CreateAsync(new IdentityRole(role)));
        }

        var ownerPassword = config["Seed:OwnerPassword"]
            ?? throw new InvalidOperationException("Set Seed:OwnerPassword (user-secrets or env var).");
        var owner = await EnsureUserAsync(userManager, config["Seed:OwnerEmail"]
            ?? throw new InvalidOperationException("Set Seed:OwnerEmail (user-secrets or env var)."), Roles.Owner);

        // Config is the owner's password source until there's an email-based reset flow.
        if (!await userManager.CheckPasswordAsync(owner, ownerPassword))
        {
            ThrowIfFailed(await userManager.RemovePasswordAsync(owner));
            ThrowIfFailed(await userManager.AddPasswordAsync(owner, ownerPassword));
        }

        // No password: the guest can only sign in through the one-click guest endpoint.
        await EnsureUserAsync(userManager, config["Seed:GuestEmail"]!, Roles.Guest);
    }

    private static async Task<AppUser> EnsureUserAsync(UserManager<AppUser> userManager, string email, string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is not null) return user;

        // Registration is closed, so seeded addresses come from our own config and count as verified.
        user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        ThrowIfFailed(await userManager.CreateAsync(user));
        ThrowIfFailed(await userManager.AddToRoleAsync(user, role));
        return user;
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
