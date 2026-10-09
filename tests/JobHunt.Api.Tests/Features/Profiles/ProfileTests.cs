using System.Text.Json;
using JobHunt.Api.Tests.Features.Applications;

namespace JobHunt.Api.Tests.Features.Profiles;

// Tests share one factory: the owner's profile accumulates, so assertions check only what each test set.
// The guest is reserved for tests that need an untouched profile (one per class).
public class ProfileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UpdateProfile = """
        mutation ($fullName: String, $headline: String, $summary: String, $email: String, $phone: String,
                  $location: LocationInput, $dateOfBirth: LocalDate, $nationality: String, $workAuthorization: String,
                  $hobbies: [String!]!, $links: [ProfileLinkInput!]!) {
          updateProfile(input: { fullName: $fullName, headline: $headline, summary: $summary, email: $email,
                                 phone: $phone, location: $location, dateOfBirth: $dateOfBirth,
                                 nationality: $nationality, workAuthorization: $workAuthorization,
                                 hobbies: $hobbies, links: $links }) {
            profile {
              fullName headline summary email phone location { id city countryCode isTarget }
              dateOfBirth nationality workAuthorization hobbies links { kind label url }
            }
            errors { __typename }
          }
        }
        """;

    private const string UpdatePreferences = """
        mutation ($jobRoleNames: [String!]!, $workModes: [WorkMode!]!, $employmentTypes: [EmploymentType!]!,
                  $minSalary: Int, $salaryCurrency: String, $noticePeriodMonths: Int) {
          updateProfilePreferences(input: { jobRoleNames: $jobRoleNames, workModes: $workModes,
                                            employmentTypes: $employmentTypes, minSalary: $minSalary,
                                            salaryCurrency: $salaryCurrency, noticePeriodMonths: $noticePeriodMonths }) {
            profile { jobRoles { name } workModes employmentTypes minSalary salaryCurrency noticePeriodMonths }
            errors { __typename }
          }
        }
        """;

    private const string ProfileQuery = """
        {
          profile {
            fullName headline summary email phone location { city } links { kind label url } hobbies
            jobRoles { name } workModes employmentTypes minSalary salaryCurrency noticePeriodMonths
            targetLocations { city countryCode }
            experiences { id } educations { id } certifications { id } projects { id }
            skills { level } languages { level }
          }
        }
        """;

    internal static object Basics(string? fullName = "Ola Nordmann", string? phone = null, object? location = null,
        object[]? links = null, string? email = null, string? dateOfBirth = null, string? nationality = null,
        string? workAuthorization = null, string[]? hobbies = null) =>
        new
        {
            fullName, headline = (string?)null, summary = (string?)null, email, phone, location,
            dateOfBirth, nationality, workAuthorization, hobbies = hobbies ?? [], links = links ?? [],
        };

    private static object Preferences(string[]? roles = null, string[]? workModes = null, int? minSalary = null,
        string? salaryCurrency = null, int? noticePeriodMonths = null) =>
        new
        {
            jobRoleNames = roles ?? [], workModes = workModes ?? [], employmentTypes = Array.Empty<string>(),
            minSalary, salaryCurrency, noticePeriodMonths,
        };

    private static async Task<JsonElement> ProfileAsync(HttpClient client) =>
        (await client.GraphQLAsync(ProfileQuery)).GetProperty("profile");

    private static JsonElement Payload(JsonElement data, string mutation) => data.GetProperty(mutation);

    [Fact]
    public async Task FreshUser_HasEmptyProfile()
    {
        var client = await factory.GetGuestClientAsync();

        var profile = await ProfileAsync(client);

        Assert.Equal(JsonValueKind.Null, profile.GetProperty("fullName").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("location").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("minSalary").ValueKind);
        foreach (var list in new[] { "links", "hobbies", "jobRoles", "workModes", "employmentTypes", "experiences", "educations",
                     "certifications", "projects", "skills", "languages" })
            Assert.Equal(0, profile.GetProperty(list).GetArrayLength());
    }

    [Fact]
    public async Task Profiles_ArePerUser()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();

        await owner.GraphQLAsync(UpdateProfile, Basics("Owner Name"));

        Assert.NotEqual("Owner Name", (await ProfileAsync(guest)).GetProperty("fullName").GetString());
        Assert.Equal("Owner Name", (await ProfileAsync(owner)).GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task UpdateProfile_TrimsAndSaves_AndOmittedFieldClears()
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync(UpdateProfile, Basics(phone: "+47 123 45 678",
            links: [new { kind = "OTHER", label = " Blog ", url = "https://ola.dev" }], email: "ola@example.com"));

        var profile = Payload(await client.GraphQLAsync(UpdateProfile, new
        {
            fullName = " Ola Nordmann ", headline = "Frontend engineer", summary = "  I build **UIs**  ",
            email = (string?)null, phone = (string?)null, location = (object?)null,
            dateOfBirth = (string?)null, nationality = (string?)null, workAuthorization = (string?)null,
            hobbies = Array.Empty<string>(),
            links = new[] { new { kind = "OTHER", label = "Blog", url = "https://ola.dev" } },
        }), "updateProfile").GetProperty("profile");

        Assert.Equal("Ola Nordmann", profile.GetProperty("fullName").GetString());
        Assert.Equal("Frontend engineer", profile.GetProperty("headline").GetString());
        Assert.Equal("I build **UIs**", profile.GetProperty("summary").GetString());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("phone").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("email").ValueKind);
        Assert.Equal("Blog", profile.GetProperty("links")[0].GetProperty("label").GetString());
    }

    [Fact]
    public async Task UpdateProfile_SavesDetails_AndOmittedDetailsClear()
    {
        var client = await factory.GetOwnerClientAsync();

        var set = Payload(await client.GraphQLAsync(UpdateProfile, Basics(dateOfBirth: "1990-05-17",
            nationality: " Norwegian ", workAuthorization: "EU/EEA citizen")), "updateProfile").GetProperty("profile");
        var cleared = Payload(await client.GraphQLAsync(UpdateProfile, Basics()), "updateProfile").GetProperty("profile");

        Assert.Equal("1990-05-17", set.GetProperty("dateOfBirth").GetString());
        Assert.Equal("Norwegian", set.GetProperty("nationality").GetString());
        Assert.Equal("EU/EEA citizen", set.GetProperty("workAuthorization").GetString());
        foreach (var field in new[] { "dateOfBirth", "nationality", "workAuthorization" })
            Assert.Equal(JsonValueKind.Null, cleared.GetProperty(field).ValueKind);
    }

    [Fact]
    public async Task UpdateProfile_CleansHobbies()
    {
        var client = await factory.GetOwnerClientAsync();

        var profile = Payload(await client.GraphQLAsync(UpdateProfile,
            Basics(hobbies: ["Climbing", " climbing ", "", "Chess"])), "updateProfile").GetProperty("profile");

        Assert.Equal(["Climbing", "Chess"], profile.GetProperty("hobbies").EnumerateArray().Select(h => h.GetString()));
    }

    [Fact]
    public async Task UpdateProfile_TypedLinks_AllowSubdomains_AndDropLabel()
    {
        var client = await factory.GetOwnerClientAsync();

        var links = Payload(await client.GraphQLAsync(UpdateProfile, Basics(links:
        [
            new { kind = "GITHUB", label = "ignored", url = "https://github.com/ola" },
            new { kind = "LINKEDIN", label = (string?)null, url = "https://www.linkedin.com/in/ola" },
            new { kind = "PORTFOLIO", label = (string?)null, url = "https://ola.dev" },
            new { kind = "OTHER", label = "Blog", url = "https://blog.ola.dev" },
            new { kind = "OTHER", label = "Talks", url = "https://talks.ola.dev" },
        ])), "updateProfile").GetProperty("profile").GetProperty("links");

        Assert.Equal(["GITHUB", "LINKEDIN", "PORTFOLIO", "OTHER", "OTHER"],
            links.EnumerateArray().Select(l => l.GetProperty("kind").GetString()));
        Assert.Equal("", links[0].GetProperty("label").GetString());
        Assert.Equal("Talks", links[4].GetProperty("label").GetString());
    }

    [Fact]
    public async Task UpdateProfile_DoesNotTouchEntries()
    {
        var client = await factory.GetOwnerClientAsync();
        await ProfileEntryTests.AddExperienceAsync(client, "Acme AS");
        var before = (await ProfileAsync(client)).GetProperty("experiences").GetArrayLength();

        await client.GraphQLAsync(UpdateProfile, Basics());

        Assert.Equal(before, (await ProfileAsync(client)).GetProperty("experiences").GetArrayLength());
        Assert.True(before > 0);
    }

    public static TheoryData<string, object> InvalidBasics => new()
    {
        { "bad email", Basics(email: "not-an-email") },
        { "display-name email", Basics(email: "Ola <ola@example.com>") },
        { "ftp link", Basics(links: [new { kind = "OTHER", label = "Files", url = "ftp://example.com" }]) },
        { "relative link", Basics(links: [new { kind = "OTHER", label = "Site", url = "example.com" }]) },
        { "blank link label", Basics(links: [new { kind = "OTHER", label = " ", url = "https://example.com" }]) },
        { "missing link label", Basics(links: [new { kind = "OTHER", label = (string?)null, url = "https://example.com" }]) },
        { "too many links", Basics(links: [.. Enumerable.Range(0, 11).Select(i => new { kind = "OTHER", label = $"L{i}", url = "https://example.com" })]) },
        { "github on wrong host", Basics(links: [new { kind = "GITHUB", label = (string?)null, url = "https://example.com/ola" }]) },
        { "github lookalike host", Basics(links: [new { kind = "GITHUB", label = (string?)null, url = "https://notgithub.com/ola" }]) },
        { "linkedin on wrong host", Basics(links: [new { kind = "LINKEDIN", label = (string?)null, url = "https://example.com/in/ola" }]) },
        { "two github links", Basics(links: [new { kind = "GITHUB", label = (string?)null, url = "https://github.com/a" },
                                             new { kind = "GITHUB", label = (string?)null, url = "https://github.com/b" }]) },
        { "future date of birth", Basics(dateOfBirth: "2999-01-01") },
        { "long nationality", Basics(nationality: new string('a', 201)) },
        { "too many hobbies", Basics(hobbies: [.. Enumerable.Range(0, 21).Select(i => $"Hobby {i}")]) },
        { "long hobby", Basics(hobbies: [new string('a', 101)]) },
        { "long name", Basics(new string('a', 201)) },
    };

    [Theory]
    [MemberData(nameof(InvalidBasics))]
    public async Task UpdateProfile_Invalid_ReturnsInvalidProfileError_AndChangesNothing(string _, object variables)
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync(UpdateProfile, Basics("Kept"));

        var payload = Payload(await client.GraphQLAsync(UpdateProfile, variables), "updateProfile");

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
        Assert.Equal("Kept", (await ProfileAsync(client)).GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task UpdateProfile_ReusesExistingLocation_AndKeepsIsTarget()
    {
        var client = await factory.GetOwnerClientAsync();
        var oslo = (await client.GraphQLAsync("""
            mutation { saveLocation(input: { city: "Oslo", countryCode: "NO", isTarget: true }) { location { id } } }
            """)).GetProperty("saveLocation").GetProperty("location").GetProperty("id").GetInt32();

        var location = Payload(await client.GraphQLAsync(UpdateProfile,
            Basics(location: new { city = "oslo", countryCode = "NO" })), "updateProfile")
            .GetProperty("profile").GetProperty("location");

        Assert.Equal(oslo, location.GetProperty("id").GetInt32());
        Assert.True(location.GetProperty("isTarget").GetBoolean());

        var cleared = Payload(await client.GraphQLAsync(UpdateProfile, Basics()), "updateProfile").GetProperty("profile");
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("location").ValueKind);
    }

    [Fact]
    public async Task UpdateProfile_InvalidCountry_ReturnsInvalidLocationError()
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = Payload(await client.GraphQLAsync(UpdateProfile, Basics(location: new { countryCode = "XX" })), "updateProfile");

        Assert.Equal("InvalidLocationError", ApplicationTests.ErrorType(payload));
    }

    [Fact]
    public async Task TargetLocations_ComeFromSaveLocation()
    {
        var client = await factory.GetOwnerClientAsync();

        await client.GraphQLAsync("""
            mutation { saveLocation(input: { city: "Bergen", countryCode: "NO", isTarget: true }) { location { id } } }
            """);

        var targets = (await ProfileAsync(client)).GetProperty("targetLocations");
        Assert.Contains("Bergen", targets.EnumerateArray().Select(l => l.GetProperty("city").GetString()));
    }

    [Fact]
    public async Task UpdatePreferences_CollapsesRepeats_AndCreatesRoles()
    {
        var client = await factory.GetOwnerClientAsync();

        var profile = Payload(await client.GraphQLAsync(UpdatePreferences, Preferences(
            ["Frontend Developer", "frontend developer", "Design Technologist"], ["REMOTE", "HYBRID", "REMOTE"],
            700000, "nok", 3)), "updateProfilePreferences").GetProperty("profile");

        Assert.Equal(["Design Technologist", "Frontend Developer"],
            profile.GetProperty("jobRoles").EnumerateArray().Select(r => r.GetProperty("name").GetString()));
        Assert.Equal(["HYBRID", "REMOTE"], profile.GetProperty("workModes").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal("NOK", profile.GetProperty("salaryCurrency").GetString());
        Assert.Equal(700000, profile.GetProperty("minSalary").GetInt32());
        Assert.Equal(3, profile.GetProperty("noticePeriodMonths").GetInt32());

        var roles = await client.GraphQLAsync("""{ jobRoles(search: "Design Technologist") { nodes { listingCount } } }""");
        Assert.Equal(0, roles.GetProperty("jobRoles").GetProperty("nodes")[0].GetProperty("listingCount").GetInt32());
    }

    [Fact]
    public async Task UpdatePreferences_FullyReplaces()
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync(UpdatePreferences, Preferences(["Frontend Developer"], ["REMOTE"], 700000, "NOK"));

        var profile = Payload(await client.GraphQLAsync(UpdatePreferences, Preferences(["Backend Developer"])),
            "updateProfilePreferences").GetProperty("profile");

        Assert.Equal(["Backend Developer"], profile.GetProperty("jobRoles").EnumerateArray().Select(r => r.GetProperty("name").GetString()));
        Assert.Equal(0, profile.GetProperty("workModes").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("minSalary").ValueKind);
    }

    public static TheoryData<string, object> InvalidPreferences => new()
    {
        { "salary without currency", Preferences(minSalary: 700000) },
        { "unknown currency", Preferences(minSalary: 1, salaryCurrency: "XYZ") },
        { "negative salary", Preferences(minSalary: -1, salaryCurrency: "NOK") },
        { "notice too long", Preferences(noticePeriodMonths: 13) },
        { "blank role", Preferences([" "]) },
    };

    [Theory]
    [MemberData(nameof(InvalidPreferences))]
    public async Task UpdatePreferences_Invalid_ReturnsInvalidProfileError(string _, object variables)
    {
        var client = await factory.GetOwnerClientAsync();

        var payload = Payload(await client.GraphQLAsync(UpdatePreferences, variables), "updateProfilePreferences");

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(payload));
    }

    private const string SetContentLanguage = """
        mutation ($language: Language!) {
          setProfileContentLanguage(input: { language: $language }) {
            profile { contentLanguage }
            errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task ContentLanguage_DefaultsToEnglish_AndChangesWithoutTranslations()
    {
        var client = await factory.GetOwnerClientAsync();
        var initial = (await client.GraphQLAsync("{ profile { contentLanguage } }")).GetProperty("profile");

        var changed = Payload(await client.GraphQLAsync(SetContentLanguage, new { language = "NORWEGIAN" }), "setProfileContentLanguage");
        await client.GraphQLAsync(SetContentLanguage, new { language = "ENGLISH" });

        Assert.Equal("ENGLISH", initial.GetProperty("contentLanguage").GetString());
        Assert.Equal("NORWEGIAN", changed.GetProperty("profile").GetProperty("contentLanguage").GetString());
    }

    [Fact]
    public async Task ContentLanguage_BlockedByTranslation_ButSameValueSucceeds()
    {
        var client = await factory.GetOwnerClientAsync();
        var bulletId = await ProfileBulletTranslationTests.AddBulletAsync(client);
        var translation = (await ProfileBulletTranslationTests.SetAsync(client, bulletId, "Norsk"))
            .GetProperty("profileBulletTranslation").GetProperty("id").GetInt32();

        var blocked = Payload(await client.GraphQLAsync(SetContentLanguage, new { language = "NORWEGIAN" }), "setProfileContentLanguage");
        var same = Payload(await client.GraphQLAsync(SetContentLanguage, new { language = "ENGLISH" }), "setProfileContentLanguage");
        // Other tests in this class need no translations to exist.
        await client.GraphQLAsync("mutation ($id: Int!) { deleteProfileBulletTranslation(input: { id: $id }) { id } }", new { id = translation });

        Assert.Equal("InvalidProfileError", ApplicationTests.ErrorType(blocked));
        Assert.Equal("ENGLISH", same.GetProperty("profile").GetProperty("contentLanguage").GetString());
    }
}
