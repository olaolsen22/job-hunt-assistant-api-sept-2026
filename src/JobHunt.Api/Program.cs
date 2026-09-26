using System.Threading.RateLimiting;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

// Names the source-generated registration method: AddJobHuntTypes().
[assembly: Module("JobHuntTypes")]

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
// A factory, so GraphQL resolvers that run in parallel each get their own DbContext.
// It also registers a scoped AppDbContext, which Identity uses.
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

builder.Services.AddIdentityCore<AppUser>(options => options.User.RequireUniqueEmail = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();
// AddIdentityCookies registers the External and TwoFactor schemes too, which SSO and 2FA need later.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "jobhunt.auth";
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Dev and tests run over plain http; everywhere else the cookie must only travel over https.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    // The defaults redirect to an MVC login page; an API should answer with the status code.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
// Secure by default: an endpoint is private unless it opts out with AllowAnonymous().
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Per client IP. Behind a reverse proxy this needs forwarded headers (M8), or all clients share one bucket.
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

builder.AddGraphQL()
    .RegisterDbContextFactory<AppDbContext>()
    .AddJobHuntTypes()
    .AddMutationConventions(applyToAllMutations: true)
    .AddProjections()
    .AddFiltering()
    .AddSorting()
    .ModifyPagingOptions(options => options.IncludeTotalCount = true);

var app = builder.Build();

// Single instance + SQLite, so applying migrations on startup is safe and keeps deploys to one step.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    await AuthSeeder.SeedAsync(scope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
// Explicit so auth runs after UseStatusCodePages; the auto-added one runs first and its 401/403s get no ProblemDetails body.
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapAuthEndpoints();
app.MapGraphQL();

app.Run();
