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

        await SetPasswordAsync(userManager, owner, ownerPassword);

        // Extra accounts (e.g. a real one next to a test owner); registration stays closed.
        foreach (var seed in config.GetSection("Seed:Users").GetChildren())
        {
            var email = seed["Email"];
            var password = seed["Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException($"Set both Seed:Users:{seed.Key}:Email and Password.");
            await SetPasswordAsync(userManager, await EnsureUserAsync(userManager, email, Roles.Owner), password);
        }

        // No password: the guest can only sign in through the one-click guest endpoint.
        await EnsureUserAsync(userManager, config["Seed:GuestEmail"]!, Roles.Guest);
    }

    // Config is the password source until there's an email-based reset flow.
    private static async Task SetPasswordAsync(UserManager<AppUser> userManager, AppUser user, string password)
    {
        if (await userManager.CheckPasswordAsync(user, password)) return;
        ThrowIfFailed(await userManager.RemovePasswordAsync(user));
        ThrowIfFailed(await userManager.AddPasswordAsync(user, password));
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
