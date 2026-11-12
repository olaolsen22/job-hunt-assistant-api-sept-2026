using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.Applications;
using JobHunt.Api.Features.BaseCvs;
using JobHunt.Api.Features.Companies;
using JobHunt.Api.Features.Contacts;
using JobHunt.Api.Features.Industries;
using JobHunt.Api.Features.JobRoles;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Locations;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.Skills;
using JobHunt.Api.Features.TailoredCvDrafts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace JobHunt.Api.Features.Auth;

public static class GuestDataSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var config = services.GetRequiredService<IConfiguration>();
        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var db = services.GetRequiredService<AppDbContext>();
        var time = services.GetRequiredService<TimeProvider>();

        if (!config.GetValue("Seed:GuestDemoData", true)) return;

        var guestEmail = config["Seed:GuestEmail"];
        if (string.IsNullOrWhiteSpace(guestEmail)) return;

        var guest = await userManager.FindByEmailAsync(guestEmail);
        if (guest is null) return;

        var userId = guest.Id;
        if (await db.Listings.IgnoreQueryFilters().AnyAsync(l => l.UserId == userId))
            return;

        var now = time.GetUtcNow().UtcDateTime;
        SeedData(db, userId, now);
        await db.SaveChangesAsync();
    }

    private static void SeedData(AppDbContext db, string userId, DateTime now)
    {
        // --- Industries ---
        var techIndustry = new Industry { UserId = userId, Name = "Technology", NormalizedName = Names.Normalize("Technology") };
        var consultingIndustry = new Industry { UserId = userId, Name = "Consulting", NormalizedName = Names.Normalize("Consulting") };
        var financeIndustry = new Industry { UserId = userId, Name = "Finance", NormalizedName = Names.Normalize("Finance") };
        var mediaIndustry = new Industry { UserId = userId, Name = "Media", NormalizedName = Names.Normalize("Media") };
        db.Industries.AddRange(techIndustry, consultingIndustry, financeIndustry, mediaIndustry);

        // --- Companies ---
        var nordvik = NewCompany(userId, "Nordvik Solutions", techIndustry);
        var fjordtech = NewCompany(userId, "Fjordtech AS", techIndustry);
        var brygge = NewCompany(userId, "Brygge Digital", mediaIndustry);
        var vestland = NewCompany(userId, "Vestland Consulting", consultingIndustry);
        var havData = NewCompany(userId, "HavData", techIndustry);
        var polarisBank = NewCompany(userId, "Polaris Bank", financeIndustry);
        var skyfjord = NewCompany(userId, "Skyfjord Cloud", techIndustry);
        var trollvik = NewCompany(userId, "Trollvik Media", mediaIndustry);
        db.Companies.AddRange(nordvik, fjordtech, brygge, vestland, havData, polarisBank, skyfjord, trollvik);

        // --- Locations (seeded by LookupSeeder) ---
        var oslo = db.Locations.IgnoreQueryFilters()
            .First(l => l.UserId == userId && l.NormalizedCity == Names.Normalize("Oslo"));
        var bergen = db.Locations.IgnoreQueryFilters()
            .First(l => l.UserId == userId && l.NormalizedCity == Names.Normalize("Bergen"));

        // --- Job roles (seeded by LookupSeeder) ---
        JobRole FindRole(string name) => db.JobRoles.IgnoreQueryFilters()
            .First(r => r.UserId == userId && r.NormalizedName == Names.Normalize(name));

        var frontendRole = FindRole("Frontend Developer");
        var backendRole = FindRole("Backend Developer");
        var fullstackRole = FindRole("Full Stack Developer");
        var devopsRole = FindRole("DevOps Engineer");

        // --- Skills (seeded by LookupSeeder) ---
        Skill FindSkill(string name) => db.Skills.IgnoreQueryFilters()
            .First(s => s.UserId == userId && s.NormalizedName == Names.Normalize(name));

        var typescript = FindSkill("TypeScript");
        var react = FindSkill("React");
        var csharp = FindSkill("C#");
        var dotnet = FindSkill(".NET");
        var docker = FindSkill("Docker");
        var vuejs = FindSkill("Vue.js");

        // --- Listings ---
        var baseDate = now.AddDays(-45);

        var listings = new[]
        {
            NewListing(userId, "Senior Frontend Developer", ListingKind.Vacancy, nordvik, frontendRole, Seniority.Senior,
                EmploymentType.Permanent, Schedule.FullTime, WorkMode.Hybrid,
                ApplicationStatus.Interviewing, baseDate, now),

            NewListing(userId, "Backend Developer – .NET", ListingKind.Vacancy, fjordtech, backendRole, Seniority.Mid,
                EmploymentType.Permanent, Schedule.FullTime, WorkMode.Onsite,
                ApplicationStatus.Applied, baseDate.AddDays(5), now),

            NewListing(userId, "Full Stack Developer", ListingKind.Vacancy, brygge, fullstackRole, Seniority.Mid,
                EmploymentType.Permanent, Schedule.FullTime, WorkMode.Hybrid,
                ApplicationStatus.Saved, baseDate.AddDays(10), now),

            NewListing(userId, "DevOps Engineer", ListingKind.Vacancy, vestland, devopsRole, Seniority.Senior,
                EmploymentType.Contract, Schedule.FullTime, WorkMode.Remote,
                ApplicationStatus.Offer, baseDate.AddDays(-10), now),

            NewListing(userId, "Frontend Developer – React", ListingKind.Vacancy, havData, frontendRole, Seniority.Mid,
                EmploymentType.Permanent, Schedule.FullTime, WorkMode.Onsite,
                ApplicationStatus.Rejected, baseDate.AddDays(2), now),

            NewListing(userId, "Junior Full Stack Developer", ListingKind.Vacancy, polarisBank, fullstackRole, Seniority.Junior,
                EmploymentType.Permanent, Schedule.FullTime, WorkMode.Hybrid,
                ApplicationStatus.Applied, baseDate.AddDays(15), now),

            NewListing(userId, "Cloud Platform Engineer", ListingKind.Vacancy, skyfjord, devopsRole, Seniority.Senior,
                EmploymentType.Permanent, Schedule.FullTime, WorkMode.Remote,
                ApplicationStatus.Saved, baseDate.AddDays(20), now),

            NewListing(userId, "Senior Backend Developer", ListingKind.Vacancy, trollvik, backendRole, Seniority.Senior,
                EmploymentType.FixedTerm, Schedule.FullTime, WorkMode.Hybrid,
                ApplicationStatus.Applied, baseDate.AddDays(8), now),

            NewListing(userId, "Frontend Tech Lead", ListingKind.Vacancy, nordvik, frontendRole, Seniority.Lead,
                EmploymentType.Permanent, Schedule.FullTime, WorkMode.Onsite,
                ApplicationStatus.Saved, baseDate.AddDays(25), now),

            NewListing(userId, "UX-Focused Frontend Developer", ListingKind.OpenInvitation, brygge, frontendRole, Seniority.Mid,
                EmploymentType.Permanent, Schedule.PartTime, WorkMode.Hybrid,
                ApplicationStatus.Withdrawn, baseDate.AddDays(-5), now),
        };

        // Favorite two listings
        listings[0].FavoritedAt = now.AddDays(-10);
        listings[3].FavoritedAt = now.AddDays(-15);

        // Archive the rejected one
        listings[4].ArchivedAt = now.AddDays(-3);

        db.Listings.AddRange(listings);

        // --- Status changes ---
        foreach (var listing in listings)
            db.StatusChanges.AddRange(BuildStatusHistory(userId, listing, now));

        // --- Profile (LookupSeeder already created it; update with details) ---
        var profile = db.Profiles.IgnoreQueryFilters().First(p => p.UserId == userId);

        profile.FullName = "Kari Nordmann";
        profile.Headline = "Full Stack Developer | .NET & Vue.js";
        profile.Summary = "Experienced developer with a strong background in building modern web applications. Skilled in both frontend and backend development, with particular expertise in Vue.js and .NET. Passionate about clean code, user experience, and continuous learning.";
        profile.Email = "kari.nordmann@example.com";
        profile.Phone = "+47 900 00 000";
        profile.ContentLanguage = Language.English;
        profile.Hobbies = ["Hiking", "Photography", "Open source"];
        profile.Links =
        [
            new ProfileLink { Kind = LinkKind.Github, Url = "https://github.com/karinordmann" },
            new ProfileLink { Kind = LinkKind.Linkedin, Url = "https://linkedin.com/in/karinordmann" },
        ];
        profile.Location = oslo;

        // --- Experience entries ---
        var exp1 = new ProfileExperience
        {
            UserId = userId, Profile = profile, Employer = "Nordvik Solutions", Title = "Senior Frontend Developer",
            StartMonth = new DateOnly(2022, 3, 1), Location = oslo,
            Bullets =
            [
                new ProfileBullet { UserId = userId, Text = "Led migration from AngularJS to Vue 3, reducing bundle size by 40%", Position = 0 },
                new ProfileBullet { UserId = userId, Text = "Implemented component library used across 3 product teams", Position = 1 },
                new ProfileBullet { UserId = userId, Text = "Mentored two junior developers through structured pair programming", Position = 2 },
            ],
        };
        var exp2 = new ProfileExperience
        {
            UserId = userId, Profile = profile, Employer = "Brygge Digital", Title = "Full Stack Developer",
            StartMonth = new DateOnly(2019, 8, 1), EndMonth = new DateOnly(2022, 2, 1), Location = oslo,
            Bullets =
            [
                new ProfileBullet { UserId = userId, Text = "Built RESTful APIs in ASP.NET Core serving 50k daily active users", Position = 0 },
                new ProfileBullet { UserId = userId, Text = "Designed and implemented real-time notification system using SignalR", Position = 1 },
            ],
        };
        var exp3 = new ProfileExperience
        {
            UserId = userId, Profile = profile, Employer = "Vestland Consulting", Title = "Junior Developer",
            StartMonth = new DateOnly(2017, 6, 1), EndMonth = new DateOnly(2019, 7, 1), Location = bergen,
            Bullets =
            [
                new ProfileBullet { UserId = userId, Text = "Developed client-facing dashboards with React and D3.js", Position = 0 },
                new ProfileBullet { UserId = userId, Text = "Contributed to internal CI/CD pipeline setup using Azure DevOps", Position = 1 },
            ],
        };
        db.ProfileExperiences.AddRange(exp1, exp2, exp3);

        // --- Education entries ---
        var edu1 = new ProfileEducation
        {
            UserId = userId, Profile = profile, School = "University of Oslo",
            Degree = "Master of Science", Field = "Software Engineering",
            StartMonth = new DateOnly(2015, 8, 1), EndMonth = new DateOnly(2017, 6, 1),
        };
        var edu2 = new ProfileEducation
        {
            UserId = userId, Profile = profile, School = "University of Bergen",
            Degree = "Bachelor of Science", Field = "Computer Science",
            StartMonth = new DateOnly(2012, 8, 1), EndMonth = new DateOnly(2015, 6, 1),
        };
        db.ProfileEducations.AddRange(edu1, edu2);

        // --- Profile skills ---
        db.ProfileSkills.AddRange(
            new ProfileSkill { Profile = profile, Skill = typescript, Level = 4 },
            new ProfileSkill { Profile = profile, Skill = react, Level = 3 },
            new ProfileSkill { Profile = profile, Skill = vuejs, Level = 4 },
            new ProfileSkill { Profile = profile, Skill = csharp, Level = 3 },
            new ProfileSkill { Profile = profile, Skill = dotnet, Level = 3 },
            new ProfileSkill { Profile = profile, Skill = docker, Level = 2 });

        // --- Base CV ---
        var baseCv = new BaseCv
        {
            UserId = userId,
            Name = "General Tech CV",
            NormalizedName = Names.Normalize("General Tech CV"),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.BaseCvs.Add(baseCv);

        var cvContent = new TailoredCvContent(
            FullName: "Kari Nordmann",
            Headline: "Full Stack Developer | .NET & Vue.js",
            Email: "kari.nordmann@example.com",
            Phone: "+47 900 00 000",
            Summary: "Experienced developer with a strong background in building modern web applications.",
            Experiences:
            [
                new TailoredCvExperience("Nordvik Solutions", "Senior Frontend Developer",
                    new DateOnly(2022, 3, 1), null,
                    [new TailoredCvBullet("Led migration from AngularJS to Vue 3, reducing bundle size by 40%"),
                     new TailoredCvBullet("Implemented component library used across 3 product teams")],
                    Location: "Oslo"),
                new TailoredCvExperience("Brygge Digital", "Full Stack Developer",
                    new DateOnly(2019, 8, 1), new DateOnly(2022, 2, 1),
                    [new TailoredCvBullet("Built RESTful APIs in ASP.NET Core serving 50k daily active users"),
                     new TailoredCvBullet("Designed and implemented real-time notification system using SignalR")],
                    Location: "Oslo"),
                new TailoredCvExperience("Vestland Consulting", "Junior Developer",
                    new DateOnly(2017, 6, 1), new DateOnly(2019, 7, 1),
                    [new TailoredCvBullet("Developed client-facing dashboards with React and D3.js")],
                    Location: "Bergen"),
            ],
            Educations:
            [
                new TailoredCvEducation("University of Oslo", "Master of Science", "Software Engineering",
                    new DateOnly(2015, 8, 1), new DateOnly(2017, 6, 1), []),
                new TailoredCvEducation("University of Bergen", "Bachelor of Science", "Computer Science",
                    new DateOnly(2012, 8, 1), new DateOnly(2015, 6, 1), []),
            ],
            Skills:
            [
                new TailoredCvSkill("TypeScript", 4),
                new TailoredCvSkill("Vue.js", 4),
                new TailoredCvSkill("React", 3),
                new TailoredCvSkill("C#", 3),
                new TailoredCvSkill(".NET", 3),
                new TailoredCvSkill("Docker", 2),
            ],
            Links: [new TailoredCvLink("GitHub", "https://github.com/karinordmann")]);

        var version = new BaseCvVersion
        {
            UserId = userId,
            BaseCv = baseCv,
            Language = Language.English,
            Number = 1,
            CreatedAt = now,
        };
        version.Content = cvContent;
        db.BaseCvVersions.Add(version);

        // --- Contacts ---
        var contact1 = new Contact
        {
            UserId = userId, Company = nordvik, Name = "Erik Hansen",
            NormalizedName = Names.Normalize("Erik Hansen"),
            Title = "Engineering Manager", Email = "erik.hansen@nordvik.example.com",
        };
        var contact2 = new Contact
        {
            UserId = userId, Company = fjordtech, Name = "Ingrid Larsen",
            NormalizedName = Names.Normalize("Ingrid Larsen"),
            Title = "Tech Lead", Phone = "+47 911 22 333",
        };
        var contact3 = new Contact
        {
            UserId = userId, Company = vestland, Name = "Morten Berg",
            NormalizedName = Names.Normalize("Morten Berg"),
            Title = "Recruiter", Email = "morten.berg@vestland.example.com",
        };
        var contact4 = new Contact
        {
            UserId = userId, Company = polarisBank, Name = "Silje Vik",
            NormalizedName = Names.Normalize("Silje Vik"),
            Title = "HR Coordinator",
        };
        db.Contacts.AddRange(contact1, contact2, contact3, contact4);

        // --- Contact interactions ---
        AddInteraction(db, userId, contact1, now.AddDays(-20), ContactDirection.Sent, ContactChannel.LinkedIn, "Connected on LinkedIn and asked about open positions");
        AddInteraction(db, userId, contact1, now.AddDays(-15), ContactDirection.Received, ContactChannel.Email, "Replied with details about the Senior Frontend role");
        AddInteraction(db, userId, contact2, now.AddDays(-12), ContactDirection.Sent, ContactChannel.Email, "Sent application and follow-up question about tech stack");
        AddInteraction(db, userId, contact3, now.AddDays(-30), ContactDirection.Received, ContactChannel.Phone, "Phone screen about available consulting positions");
        AddInteraction(db, userId, contact3, now.AddDays(-25), ContactDirection.Sent, ContactChannel.Email, "Sent CV and cover letter");
        AddInteraction(db, userId, contact4, now.AddDays(-8), ContactDirection.Received, ContactChannel.Email, "Confirmation of application received");

        contact1.Refresh();
        contact2.Refresh();
        contact3.Refresh();
        contact4.Refresh();
    }

    private static Company NewCompany(string userId, string name, Industry industry) => new()
    {
        UserId = userId,
        Name = name,
        NormalizedName = Names.Normalize(name),
        Industry = industry,
    };

    private static Listing NewListing(string userId, string title, ListingKind kind, Company company,
        JobRole jobRole, Seniority seniority, EmploymentType employmentType, Schedule schedule,
        WorkMode workMode, ApplicationStatus status, DateTime baseDate, DateTime now) => new()
    {
        UserId = userId,
        Title = title,
        NormalizedTitle = Names.Normalize(title),
        Kind = kind,
        Company = company,
        JobRole = jobRole,
        Seniority = seniority,
        EmploymentType = employmentType,
        Schedule = schedule,
        WorkMode = workMode,
        Status = status,
        PostingLanguage = Language.English,
        DeadlineType = DeadlineType.Asap,
        CreatedAt = baseDate,
        UpdatedAt = now,
    };

    private static List<StatusChange> BuildStatusHistory(string userId, Listing listing, DateTime now)
    {
        var changes = new List<StatusChange>();
        var baseTime = listing.CreatedAt;

        if (listing.Status == ApplicationStatus.Saved)
            return changes;

        changes.Add(new StatusChange
        {
            UserId = userId, Listing = listing, Status = ApplicationStatus.Applied,
            OccurredAt = baseTime.AddDays(1), CreatedAt = baseTime.AddDays(1),
        });
        listing.StatusChangedAt = baseTime.AddDays(1);

        if (listing.Status == ApplicationStatus.Applied)
            return changes;

        if (listing.Status == ApplicationStatus.Interviewing)
        {
            changes.Add(new StatusChange
            {
                UserId = userId, Listing = listing, Status = ApplicationStatus.Interviewing,
                OccurredAt = baseTime.AddDays(10), CreatedAt = baseTime.AddDays(10),
            });
            listing.StatusChangedAt = baseTime.AddDays(10);
        }
        else if (listing.Status == ApplicationStatus.Rejected)
        {
            changes.Add(new StatusChange
            {
                UserId = userId, Listing = listing, Status = ApplicationStatus.Rejected,
                OccurredAt = baseTime.AddDays(14), CreatedAt = baseTime.AddDays(14),
                Note = "Position filled internally",
            });
            listing.StatusChangedAt = baseTime.AddDays(14);
        }
        else if (listing.Status == ApplicationStatus.Offer)
        {
            changes.Add(new StatusChange
            {
                UserId = userId, Listing = listing, Status = ApplicationStatus.Interviewing,
                OccurredAt = baseTime.AddDays(7), CreatedAt = baseTime.AddDays(7),
            });
            changes.Add(new StatusChange
            {
                UserId = userId, Listing = listing, Status = ApplicationStatus.Offer,
                OccurredAt = baseTime.AddDays(21), CreatedAt = baseTime.AddDays(21),
                Note = "Verbal offer received, waiting for written contract",
            });
            listing.StatusChangedAt = baseTime.AddDays(21);
        }
        else if (listing.Status == ApplicationStatus.Withdrawn)
        {
            changes.Add(new StatusChange
            {
                UserId = userId, Listing = listing, Status = ApplicationStatus.Withdrawn,
                OccurredAt = baseTime.AddDays(5), CreatedAt = baseTime.AddDays(5),
                Note = "Decided to focus on other opportunities",
            });
            listing.StatusChangedAt = baseTime.AddDays(5);
        }

        return changes;
    }

    private static void AddInteraction(AppDbContext db, string userId, Contact contact,
        DateTime occurredAt, ContactDirection direction, ContactChannel channel, string note)
    {
        db.ContactInteractions.Add(new ContactInteraction
        {
            UserId = userId,
            Contact = contact,
            OccurredAt = occurredAt,
            Direction = direction,
            Channel = channel,
            Note = note,
            CreatedAt = occurredAt,
        });
    }
}
