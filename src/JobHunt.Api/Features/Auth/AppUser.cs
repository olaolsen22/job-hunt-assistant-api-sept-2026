using Microsoft.AspNetCore.Identity;

namespace JobHunt.Api.Features.Auth;

public class AppUser : IdentityUser;

public static class Roles
{
    public const string Owner = "Owner";
    public const string Guest = "Guest";
}
