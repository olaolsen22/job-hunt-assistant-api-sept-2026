using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace JobHunt.Api.Features.Auth;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

public record UserResponse(string Id, string Email, string Role);

// RequiresTwoFactor is always false until 2FA exists; it's in the contract now so the UI flow won't change.
public record LoginResponse(bool RequiresTwoFactor, UserResponse? User);

public static class AuthEndpoints
{
    public const string LoginRateLimitPolicy = "login";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy);
        group.MapPost("/guest", GuestLogin).AllowAnonymous().RequireRateLimiting(LoginRateLimitPolicy);
        group.MapPost("/logout", Logout).AllowAnonymous();
        group.MapGet("/me", Me);
    }

    private static async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> Login(
        LoginRequest request, SignInManager<AppUser> signInManager)
    {
        var result = await signInManager.PasswordSignInAsync(
            request.Email, request.Password, isPersistent: true, lockoutOnFailure: true);

        if (result.RequiresTwoFactor)
            return TypedResults.Ok(new LoginResponse(true, null));
        if (result.IsLockedOut)
            return TypedResults.Problem("Too many failed attempts. Try again later.", statusCode: StatusCodes.Status429TooManyRequests);
        if (!result.Succeeded)
            return TypedResults.Problem("Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);

        var user = await signInManager.UserManager.FindByNameAsync(request.Email);
        return TypedResults.Ok(new LoginResponse(false, await ToResponseAsync(user!, signInManager.UserManager)));
    }

    private static async Task<Ok<LoginResponse>> GuestLogin(SignInManager<AppUser> signInManager, IConfiguration config)
    {
        var guest = await signInManager.UserManager.FindByEmailAsync(config["Seed:GuestEmail"]!);
        await signInManager.SignInAsync(guest!, isPersistent: false);
        return TypedResults.Ok(new LoginResponse(false, await ToResponseAsync(guest!, signInManager.UserManager)));
    }

    private static async Task<NoContent> Logout(SignInManager<AppUser> signInManager)
    {
        await signInManager.SignOutAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Ok<UserResponse>> Me(ClaimsPrincipal principal, UserManager<AppUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        return TypedResults.Ok(await ToResponseAsync(user!, userManager));
    }

    private static async Task<UserResponse> ToResponseAsync(AppUser user, UserManager<AppUser> userManager) =>
        new(user.Id, user.Email!, (await userManager.GetRolesAsync(user)).Single());
}
