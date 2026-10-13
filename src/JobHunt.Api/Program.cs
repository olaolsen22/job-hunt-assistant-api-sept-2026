using System.Threading.RateLimiting;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Documents;
using JobHunt.Api.Features.Parsing;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.TailoredCvDrafts;
using JobHunt.Api.Llm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
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

// ValidateOnStart: a missing API key stops the app at boot instead of failing the first parse.
builder.Services.AddOptions<GeminiOptions>()
    .BindConfiguration(GeminiOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();
// Keyed by provider, so Ollama (and later others) can register side by side and a user setting picks one.
builder.Services.AddKeyedSingleton<IChatClient>(GeminiOptions.Provider, (sp, _) =>
{
    var gemini = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;
    return new Google.GenAI.Client(apiKey: gemini.ApiKey).AsIChatClient(gemini.Model);
});
builder.Services.AddOptions<LlmPricingOptions>().BindConfiguration(LlmPricingOptions.Section);
builder.Services.AddScoped<ListingParser>();
builder.Services.AddScoped<ProfileCvParser>();
builder.Services.AddScoped<TailoredCvAnalyzer>();
TailoredCvPdfFonts.EnsureRegistered();
// Browser-like User-Agent: several job boards answer 403 to the default .NET one.
builder.Services.AddHttpClient<ListingPageFetcher>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
    })
    .ConfigurePrimaryHttpMessageHandler(ListingPageFetcher.CreateHandler);

builder.AddGraphQL()
    .RegisterDbContextFactory<AppDbContext>()
    .AddJobHuntTypes()
    .AddType<UploadType>()
    .AddMutationConventions(applyToAllMutations: true)
    .AddProjections()
    .AddFiltering()
    .AddSorting()
    .ModifyPagingOptions(options => options.IncludeTotalCount = true)
    // The 30 s default cancels LLM mutations mid-call: a full CV parse or analyze-all runs well past it.
    .ModifyRequestOptions(options => options.ExecutionTimeout = TimeSpan.FromMinutes(5))
    // Quick add sends one aliased createListing per row in a single document (~20 field cost each).
    .ModifyCostOptions(options =>
    {
        options.MaxFieldCost = 5000;
        options.MaxTypeCost = 5000;
    });

var app = builder.Build();

// Single instance + SQLite, so applying migrations on startup is safe and keeps deploys to one step.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
    await AuthSeeder.SeedAsync(scope.ServiceProvider);
    await LookupSeeder.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
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
app.MapDocumentEndpoints();
app.MapGraphQL();

app.Run();
