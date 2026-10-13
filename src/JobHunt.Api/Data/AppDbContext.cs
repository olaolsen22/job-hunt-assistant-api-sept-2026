using System.Security.Claims;
using JobHunt.Api.Features.AiSafeguards;
using JobHunt.Api.Features.Applications;
using JobHunt.Api.Features.Auth;
using JobHunt.Api.Features.Companies;
using JobHunt.Api.Features.Contacts;
using JobHunt.Api.Features.Documents;
using JobHunt.Api.Features.Dashboard;
using JobHunt.Api.Features.Industries;
using JobHunt.Api.Features.JobRoles;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.LlmModels;
using JobHunt.Api.Features.LlmProviderKeys;
using JobHunt.Api.Features.Parsing;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.TailoredCvDrafts;
// Hot Chocolate also has a global Location type (a position in a query document).
using Location = JobHunt.Api.Features.Locations.Location;
using JobHunt.Api.Features.Skills;
using JobHunt.Api.Llm;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Data;

// IdentityDbContext adds the AspNet* tables, including UserLogins (SSO) and UserTokens (2FA),
// so those features can be added later without a schema change.
public class AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor httpContextAccessor)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingSkill> ListingSkills => Set<ListingSkill>();
    public DbSet<ListingLanguage> ListingLanguages => Set<ListingLanguage>();
    public DbSet<ListingLocation> ListingLocations => Set<ListingLocation>();
    public DbSet<TrainingProgram> TrainingPrograms => Set<TrainingProgram>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyLocation> CompanyLocations => Set<CompanyLocation>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ListingContact> ListingContacts => Set<ListingContact>();
    public DbSet<Industry> Industries => Set<Industry>();
    public DbSet<DashboardWidget> DashboardWidgets => Set<DashboardWidget>();
    public DbSet<JobRole> JobRoles => Set<JobRole>();
    public DbSet<JobRoleCategory> JobRoleCategories => Set<JobRoleCategory>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentContent> DocumentContents => Set<DocumentContent>();
    public DbSet<StatusChange> StatusChanges => Set<StatusChange>();
    public DbSet<Interview> Interviews => Set<Interview>();
    public DbSet<ParseJob> ParseJobs => Set<ParseJob>();
    public DbSet<LlmUsage> LlmUsages => Set<LlmUsage>();
    public DbSet<UserLlmSetting> UserLlmSettings => Set<UserLlmSetting>();
    public DbSet<UserProviderKey> UserProviderKeys => Set<UserProviderKey>();
    public DbSet<AiOperation> AiOperations => Set<AiOperation>();
    public DbSet<UserLlmLimit> UserLlmLimits => Set<UserLlmLimit>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ProfileExperience> ProfileExperiences => Set<ProfileExperience>();
    public DbSet<ProfileEducation> ProfileEducations => Set<ProfileEducation>();
    public DbSet<ProfileCertification> ProfileCertifications => Set<ProfileCertification>();
    public DbSet<ProfileProject> ProfileProjects => Set<ProfileProject>();
    public DbSet<ProfileBullet> ProfileBullets => Set<ProfileBullet>();
    public DbSet<ProfileBulletTranslation> ProfileBulletTranslations => Set<ProfileBulletTranslation>();
    public DbSet<ProfileTranslation> ProfileTranslations => Set<ProfileTranslation>();
    public DbSet<ProfileSkill> ProfileSkills => Set<ProfileSkill>();
    public DbSet<ProfileLanguage> ProfileLanguages => Set<ProfileLanguage>();
    public DbSet<ProfileReference> ProfileReferences => Set<ProfileReference>();
    public DbSet<TailoredCvDraft> TailoredCvDrafts => Set<TailoredCvDraft>();

    // Null outside a request (startup, seeding), so the user filters below then match nothing.
    public string? CurrentUserId => httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Strings keep rows readable in the DB browser and survive reordering an enum.
        // CEFR levels still compare correctly as text (A1 < A2 < B1 ...).
        configurationBuilder.Properties<Enum>().HaveConversion<string>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Every query is scoped to the signed-in user, so a resolver that forgets to filter can't leak data.
        // Child rows are filtered through their listing.
        modelBuilder.Entity<Listing>().HasQueryFilter(l => l.UserId == CurrentUserId);
        modelBuilder.Entity<ListingSkill>().HasQueryFilter(s => s.Listing.UserId == CurrentUserId);
        modelBuilder.Entity<ListingLanguage>().HasQueryFilter(l => l.Listing.UserId == CurrentUserId);
        modelBuilder.Entity<ListingLocation>().HasQueryFilter(l => l.Listing.UserId == CurrentUserId);
        modelBuilder.Entity<TrainingProgram>().HasQueryFilter(t => t.Listing.UserId == CurrentUserId);
        modelBuilder.Entity<Company>().HasQueryFilter(c => c.UserId == CurrentUserId);
        modelBuilder.Entity<CompanyLocation>().HasQueryFilter(l => l.Company.UserId == CurrentUserId);
        modelBuilder.Entity<Contact>().HasQueryFilter(c => c.UserId == CurrentUserId);
        modelBuilder.Entity<ListingContact>().HasQueryFilter(c => c.Listing.UserId == CurrentUserId);
        modelBuilder.Entity<Industry>().HasQueryFilter(i => i.UserId == CurrentUserId);
        modelBuilder.Entity<DashboardWidget>().HasQueryFilter(w => w.UserId == CurrentUserId);
        modelBuilder.Entity<JobRole>().HasQueryFilter(r => r.UserId == CurrentUserId);
        modelBuilder.Entity<JobRoleCategory>().HasQueryFilter(c => c.UserId == CurrentUserId);
        modelBuilder.Entity<Skill>().HasQueryFilter(s => s.UserId == CurrentUserId);
        modelBuilder.Entity<Location>().HasQueryFilter(l => l.UserId == CurrentUserId);
        modelBuilder.Entity<Document>().HasQueryFilter(d => d.UserId == CurrentUserId);
        modelBuilder.Entity<DocumentContent>().HasQueryFilter(c => c.Document.UserId == CurrentUserId);
        modelBuilder.Entity<StatusChange>().HasQueryFilter(s => s.UserId == CurrentUserId);
        modelBuilder.Entity<Interview>().HasQueryFilter(i => i.UserId == CurrentUserId);
        modelBuilder.Entity<ParseJob>().HasQueryFilter(j => j.UserId == CurrentUserId);
        modelBuilder.Entity<LlmUsage>().HasQueryFilter(u => u.UserId == CurrentUserId);
        modelBuilder.Entity<UserLlmSetting>().HasQueryFilter(s => s.UserId == CurrentUserId);
        modelBuilder.Entity<UserProviderKey>().HasQueryFilter(k => k.UserId == CurrentUserId);
        modelBuilder.Entity<AiOperation>().HasQueryFilter(o => o.UserId == CurrentUserId);
        modelBuilder.Entity<UserLlmLimit>().HasQueryFilter(l => l.UserId == CurrentUserId);
        modelBuilder.Entity<Profile>().HasQueryFilter(p => p.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileWorkMode>().HasQueryFilter(w => w.Profile.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileEmploymentType>().HasQueryFilter(t => t.Profile.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileExperience>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileEducation>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileCertification>().HasQueryFilter(c => c.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileProject>().HasQueryFilter(p => p.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileBullet>().HasQueryFilter(b => b.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileBulletTranslation>().HasQueryFilter(t => t.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileTranslation>().HasQueryFilter(t => t.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileSkill>().HasQueryFilter(s => s.Profile.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileLanguage>().HasQueryFilter(l => l.Profile.UserId == CurrentUserId);
        modelBuilder.Entity<ProfileReference>().HasQueryFilter(r => r.UserId == CurrentUserId);
        modelBuilder.Entity<TailoredCvDraft>().HasQueryFilter(d => d.UserId == CurrentUserId);
    }
}
