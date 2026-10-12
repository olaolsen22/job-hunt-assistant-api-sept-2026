using System.Text.Json;
using JobHunt.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.Profiles;

public class ProfileCvImportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    internal const string Import = """
        mutation($selection:ProfileCvImportSelectionInput!){importProfileCv(input:{selection:$selection}){
          profile{fullName headline summary email phone contentLanguage hobbies links{kind label url}
            location{id city countryCode isTarget} nationality workAuthorization minSalary noticePeriodMonths
            experiences{id employer title startMonth endMonth location{id} skills{name} bullets{id text position translations{isStale text}}}
            educations{id school startMonth bullets{id text position}}
            certifications{id name bullets{id text position}}
            projects{id name url location{id} skills{name} bullets{id text position translations{isStale text}}}
            skills{skill{name} level} languages{language level}
            translations{language summary isSummaryStale}
          }
          errors{__typename ... on Error{message}}
        }}
        """;
    private async Task<JsonElement> Save(object selection, HttpClient? client = null) =>
        (await (client ?? await factory.GetOwnerClientAsync()).GraphQLAsync(Import, new { selection })).GetProperty("importProfileCv");

    [Fact]
    public async Task ImportAddsAllKindsAndReusesBatchLookups()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var skill = "ImportSkill" + suffix;
        var location = new { city = "ImportCity" + suffix, countryCode = "NO" };
        var payload = await Save(new
        {
            contentLanguage = "ENGLISH", basics = new { fullName = " Imported Name ", location },
            experiences = new[] { new { employer = suffix, title = "Engineer", startMonth = "2020-02-17", location, skillNames = new[] { skill }, bullets = new[] { new { text = "Delivered tools" } } } },
            educations = new[] { new { school = suffix, startMonth = "2010-01-01", bullets = new[] { new { text = "Studied software" } } } },
            certifications = new[] { new { name = suffix, bullets = new[] { new { text = "Passed certification" } } } },
            projects = new[] { new { name = suffix, location, skillNames = new[] { skill }, bullets = new[] { new { text = "Built project" } } } },
            skills = new[] { new { name = skill.ToLowerInvariant(), level = 4 } },
            languages = new[] { new { language = "ENGLISH", level = "C1" } },
        });
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        var profile = payload.GetProperty("profile");
        Assert.Equal("Imported Name", profile.GetProperty("fullName").GetString());
        var experience = Find(profile, "experiences", "employer", suffix);
        var project = Find(profile, "projects", "name", suffix);
        Assert.Equal("2020-02-01", experience.GetProperty("startMonth").GetString());
        Assert.Equal(profile.GetProperty("location").GetProperty("id").GetInt32(), project.GetProperty("location").GetProperty("id").GetInt32());
        foreach (var pair in new[] { ("experiences", "employer"), ("educations", "school"), ("certifications", "name"), ("projects", "name") })
            Assert.Equal(0, Find(profile, pair.Item1, pair.Item2, suffix).GetProperty("bullets")[0].GetProperty("position").GetInt32());
        var level = profile.GetProperty("skills").EnumerateArray().Single(s => s.GetProperty("skill").GetProperty("name").GetString() == skill);
        Assert.Equal(4, level.GetProperty("level").GetInt32());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Skills.IgnoreQueryFilters().CountAsync(s => s.Name == skill));
        Assert.Equal(1, await db.Locations.IgnoreQueryFilters().CountAsync(l => l.City == location.city));
        Assert.False(await db.Companies.IgnoreQueryFilters().AnyAsync(c => c.Name == suffix));
    }

    [Fact]
    public async Task OmittedFieldsArePreservedExplicitNullClearsAndListsReplace()
    {
        await Save(new { contentLanguage = "ENGLISH", basics = new { email = "cv@example.com", phone = "123", headline = "Original" }, hobbies = new[] { "Chess" }, links = new[] { new { kind = "OTHER", label = "Blog", url = "https://example.com" } } });
        var result = (await Save(new { contentLanguage = "ENGLISH", basics = new { phone = (string?)null, headline = "New" } })).GetProperty("profile");
        Assert.Equal(JsonValueKind.Null, result.GetProperty("phone").ValueKind);
        Assert.Equal("cv@example.com", result.GetProperty("email").GetString());
        Assert.Equal("Chess", result.GetProperty("hobbies")[0].GetString());
        Assert.Equal(1, result.GetProperty("links").GetArrayLength());
        var cleared = (await Save(new { contentLanguage = "ENGLISH", hobbies = Array.Empty<string>(), links = Array.Empty<object>() })).GetProperty("profile");
        Assert.Equal(0, cleared.GetProperty("links").GetArrayLength());
        Assert.Equal(0, cleared.GetProperty("hobbies").GetArrayLength());
    }

    [Fact]
    public async Task EntryAndBulletUpdatesPreserveIdsAndTranslations()
    {
        var name = Guid.NewGuid().ToString();
        var first = (await Save(new { contentLanguage = "ENGLISH", basics = new { summary = "Source summary" },
            projects = new[] { new { name, bullets = new[] { new { text = "First" }, new { text = "Keep" } } } } })).GetProperty("profile");
        var entry = Find(first, "projects", "name", name); var id = entry.GetProperty("id").GetInt32();
        var bulletId = entry.GetProperty("bullets")[0].GetProperty("id").GetInt32();
        var keepId = entry.GetProperty("bullets")[1].GetProperty("id").GetInt32();
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync("""
            mutation($id:Int!){setProfileBulletTranslation(input:{bulletId:$id,language:NORWEGIAN,text:"Første"}){profileBulletTranslation{id}}}
            """, new { id = bulletId });
        await client.GraphQLAsync("""
            mutation{setProfileTranslation(input:{language:NORWEGIAN,summary:"Sammendrag"}){profileTranslation{language}}}
            """);
        var changed = (await Save(new { contentLanguage = "ENGLISH", basics = new { summary = "Updated source summary" },
            projects = new[] { new { id, name = name + " edited", bullets = new object[] { new { id = bulletId, text = "Edited" }, new { text = "Appended" } } } } })).GetProperty("profile");
        var updated = changed.GetProperty("projects").EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == id);
        var bullets = updated.GetProperty("bullets");
        Assert.Equal(3, bullets.GetArrayLength());
        Assert.Equal(bulletId, bullets[0].GetProperty("id").GetInt32());
        Assert.Equal(keepId, bullets[1].GetProperty("id").GetInt32());
        Assert.Equal("Keep", bullets[1].GetProperty("text").GetString());
        Assert.True(bullets[0].GetProperty("translations")[0].GetProperty("isStale").GetBoolean());
        Assert.True(changed.GetProperty("translations")[0].GetProperty("isSummaryStale").GetBoolean());
        Assert.Equal(new[] { 0, 1, 2 }, bullets.EnumerateArray().Select(b => b.GetProperty("position").GetInt32()));
        // English profiles remain importable with existing Norwegian translations.
        var untouched = (await Save(new { contentLanguage = "ENGLISH", basics = new { fullName = "Another import" } })).GetProperty("profile");
        Assert.Equal(changed.GetProperty("translations").GetRawText(), untouched.GetProperty("translations").GetRawText());
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("experiences")]
    [InlineData("educations")]
    [InlineData("certifications")]
    public async Task ForeignAndUnknownIdsCannotBeUpdated(string section)
    {
        var name = Guid.NewGuid().ToString();
        var item = section switch
        {
            "experiences" => new Dictionary<string, object?> { ["employer"] = name, ["title"] = "Engineer", ["startMonth"] = "2020-01-01" },
            "educations" => new Dictionary<string, object?> { ["school"] = name, ["startMonth"] = "2020-01-01" },
            _ => new Dictionary<string, object?> { ["name"] = name },
        };
        var selection = new Dictionary<string, object> { ["contentLanguage"] = "ENGLISH", [section] = new[] { item } };
        var profile = (await Save(selection)).GetProperty("profile");
        item["id"] = profile.GetProperty(section).EnumerateArray().Single(e => e.GetProperty(section == "experiences" ? "employer" : section == "educations" ? "school" : "name").GetString() == name).GetProperty("id").GetInt32();
        var guest = await factory.GetGuestClientAsync();
        Assert.Equal("ProfileEntryNotFoundError", Error(await Save(selection, guest)));
        item["id"] = int.MaxValue;
        Assert.Equal("ProfileEntryNotFoundError", Error(await Save(selection)));
    }

    [Fact]
    public async Task InvalidLaterSelectionRollsBackBasicsAndLookups()
    {
        var client = await factory.GetOwnerClientAsync();
        var before = (await client.GraphQLAsync("{profile{headline projects{id}}}" )).GetRawText();
        var suffix = Guid.NewGuid().ToString();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = await Save(new { contentLanguage = "ENGLISH", basics = new { headline = "Must rollback", location = new { city = suffix, countryCode = "NO" } },
            projects = new[] { new { name = suffix, skillNames = new[] { suffix } } },
            skills = new[] { new { name = "Missing proficiency", level = (int?)null } } });
        Assert.Equal("InvalidProfileImportError", Error(result));
        Assert.Contains("skills[0].level", result.GetProperty("errors")[0].GetProperty("message").GetString());
        Assert.Equal(before, (await client.GraphQLAsync("{profile{headline projects{id}}}" )).GetRawText());
        Assert.False(await db.Skills.IgnoreQueryFilters().AnyAsync(s => s.Name == suffix));
        Assert.False(await db.Locations.IgnoreQueryFilters().AnyAsync(l => l.City == suffix));
    }

    [Fact]
    public async Task DuplicateTargetsAndWrongParentBulletsAreRejected()
    {
        var name = Guid.NewGuid().ToString();
        var first = (await Save(new { contentLanguage = "ENGLISH", projects = new[] { new { name, bullets = new[] { new { text = "One" } } } } })).GetProperty("profile");
        var entry = Find(first, "projects", "name", name); var id = entry.GetProperty("id").GetInt32();
        var bullet = entry.GetProperty("bullets")[0].GetProperty("id").GetInt32();
        Assert.Equal("InvalidProfileImportError", Error(await Save(new { contentLanguage = "ENGLISH", projects = new[] { new { id, name }, new { id, name } } })));
        Assert.Equal("InvalidProfileImportError", Error(await Save(new { contentLanguage = "ENGLISH", projects = new[] { new { id, name, bullets = new[] { new { id = bullet, text = "One" }, new { id = bullet, text = "Again" } } } } })));
        Assert.Equal("ProfileEntryNotFoundError", Error(await Save(new { contentLanguage = "ENGLISH", projects = new[] { new { name = "Wrong parent", bullets = new[] { new { id = bullet, text = "Hijack" } } } } })));
    }

    [Fact]
    public async Task CombinedBulletLimitIsEnforcedWithoutChangingEntry()
    {
        var name = Guid.NewGuid().ToString();
        var first = (await Save(new { contentLanguage = "ENGLISH", projects = new[] { new { name, bullets = Enumerable.Range(0, 20).Select(i => new { text = $"Bullet {i}" }).ToArray() } } })).GetProperty("profile");
        var entry = Find(first, "projects", "name", name); var id = entry.GetProperty("id").GetInt32();
        var result = await Save(new { contentLanguage = "ENGLISH", projects = new[] { new { id, name = "Must not change", bullets = new[] { new { text = "21st" } } } } });
        Assert.Equal("InvalidProfileImportError", Error(result));
        var current = (await (await factory.GetOwnerClientAsync()).GraphQLAsync("{profile{projects{id name bullets{id text position}}}}" )).GetProperty("profile").GetProperty("projects").EnumerateArray().Single(p => p.GetProperty("id").GetInt32() == id);
        Assert.Equal(name, current.GetProperty("name").GetString());
        Assert.Equal(20, current.GetProperty("bullets").GetArrayLength());
    }

    [Fact]
    public async Task NorwegianProfileIsNotSilentlySwitched()
    {
        using var isolated = new ApiFactory();
        var guest = await isolated.GetGuestClientAsync();
        await guest.GraphQLAsync("mutation{setProfileContentLanguage(input:{language:NORWEGIAN}){profile{contentLanguage}}}");
        var result = (await guest.GraphQLAsync(Import, new { selection = new { contentLanguage = "ENGLISH", basics = new { fullName = "Do not save" } } })).GetProperty("importProfileCv");
        Assert.Equal("InvalidProfileImportError", Error(result));
        var profile = (await guest.GraphQLAsync("{profile{contentLanguage fullName}}" )).GetProperty("profile");
        Assert.Equal("NORWEGIAN", profile.GetProperty("contentLanguage").GetString());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("fullName").ValueKind);
    }

    [Theory]
    [InlineData("{\"contentLanguage\":\"ENGLISH\",\"experiences\":[{\"employer\":\"X\",\"title\":\"Y\",\"startMonth\":null}]}")]
    [InlineData("{\"contentLanguage\":\"ENGLISH\",\"languages\":[{\"language\":\"ENGLISH\",\"level\":null}]}")]
    [InlineData("{\"contentLanguage\":\"ENGLISH\",\"basics\":{\"email\":\"invalid\"}}")]
    [InlineData("{\"contentLanguage\":\"ENGLISH\",\"projects\":[{\"name\":\"p\",\"url\":\"ftp://example.com\"}]}")]
    [InlineData("{\"contentLanguage\":\"ENGLISH\",\"links\":[{\"kind\":\"LINKEDIN\",\"url\":\"https://example.com\"}]}")]
    [InlineData("{\"contentLanguage\":\"ENGLISH\",\"links\":null}")]
    public async Task InvalidSelectionsFailWithReviewableErrors(string json)
    {
        using var document = JsonDocument.Parse(json);
        var result = await Save(document.RootElement);
        Assert.Equal("InvalidProfileImportError", Error(result));
    }

    [Fact]
    public async Task UnselectedSensitiveDetailsPreferencesTargetsAndEntriesStayUnchanged()
    {
        using var isolated = new ApiFactory();
        var client = await isolated.GetOwnerClientAsync();
        await client.GraphQLAsync("""
            mutation{
              updateProfile(input:{fullName:"Original",email:"keep@example.com",nationality:"Keep nationality",workAuthorization:"Keep authorization",hobbies:[],links:[]}){profile{id}}
              updateProfilePreferences(input:{jobRoleNames:["Frontend Developer"],workModes:[REMOTE],employmentTypes:[PERMANENT],minSalary:600000,salaryCurrency:"NOK",noticePeriodMonths:2}){profile{id}}
              saveLocation(input:{city:"Keep target",countryCode:"NO",isTarget:true}){location{id}}
              addProfileReference(input:{name:"Keep referee",relationship:"Colleague"}){profileReference{id}}
            }
            """);
        var first = (await client.GraphQLAsync(Import, new { selection = new { contentLanguage = "ENGLISH", projects = new[] { new { name = "Keep project", skillNames = new[] { "ExistingLevel" } } }, skills = new[] { new { name = "ExistingLevel", level = 5 } } } })).GetProperty("importProfileCv");
        Assert.Equal(JsonValueKind.Null, first.GetProperty("errors").ValueKind);
        const string query = "{profile{email nationality workAuthorization minSalary noticePeriodMonths jobRoles{name} workModes employmentTypes targetLocations{city} references{id name} projects{id name}}}";
        var before = (await client.GraphQLAsync(query)).GetRawText();
        var imported = (await client.GraphQLAsync(Import, new { selection = new { contentLanguage = "ENGLISH", basics = new { headline = "New headline" },
            experiences = new[] { new { employer = "New employer", title = "Developer", startMonth = "2022-01-01", skillNames = new[] { "existinglevel", "NewTagOnly" } } } } })).GetProperty("importProfileCv");
        Assert.Equal(JsonValueKind.Null, imported.GetProperty("errors").ValueKind);
        Assert.Equal(before, (await client.GraphQLAsync(query)).GetRawText());
        var skills = imported.GetProperty("profile").GetProperty("skills");
        Assert.Equal(5, skills.EnumerateArray().Single(s => s.GetProperty("skill").GetProperty("name").GetString() == "ExistingLevel").GetProperty("level").GetInt32());
        Assert.Equal(1, skills.EnumerateArray().Single(s => s.GetProperty("skill").GetProperty("name").GetString() == "NewTagOnly").GetProperty("level").GetInt32());
    }

    [Theory]
    [InlineData("experiences", "employer")]
    [InlineData("educations", "school")]
    [InlineData("certifications", "name")]
    [InlineData("projects", "name")]
    public async Task EachKindCanUpdateWithoutRewritingItsBullets(string section, string nameField)
    {
        var name = Guid.NewGuid().ToString();
        var item = new Dictionary<string, object?> { [nameField] = name, ["bullets"] = new[] { new { text = "Keep bullet" } } };
        if (section is "experiences" or "educations") item["startMonth"] = "2018-01-01";
        if (section == "experiences") { item["title"] = "Engineer"; item["skillNames"] = new[] { "SharedImportTag" }; }
        if (section == "projects") item["skillNames"] = new[] { "SharedImportTag" };
        var selection = new Dictionary<string, object> { ["contentLanguage"] = "ENGLISH", [section] = new[] { item } };
        var added = (await Save(selection)).GetProperty("profile");
        var entry = Find(added, section, nameField, name);
        var id = entry.GetProperty("id").GetInt32();
        var bullets = entry.GetProperty("bullets").GetRawText();
        item["id"] = id; item[nameField] = name + " changed"; item.Remove("bullets");
        var updated = (await Save(selection)).GetProperty("profile").GetProperty(section).EnumerateArray().Single(e => e.GetProperty("id").GetInt32() == id);
        Assert.Equal(name + " changed", updated.GetProperty(nameField).GetString());
        Assert.Equal(bullets, updated.GetProperty("bullets").GetRawText());
    }

    [Fact]
    public async Task DocumentationSaveExampleExecutes()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "JobHunt.slnx"))) root = root.Parent!;
        var docs = File.ReadAllText(Path.Combine(root.FullName, "docs/profile-cv-import.md"));
        var exampleSection = docs.IndexOf("Example variables adding a project:", StringComparison.Ordinal);
        var start = docs.IndexOf("```json\n", exampleSection, StringComparison.Ordinal) + 8;
        var end = docs.IndexOf("```", start, StringComparison.Ordinal);
        using var variables = JsonDocument.Parse(docs[start..end]);
        var result = await Save(variables.RootElement.GetProperty("selection"));
        Assert.Equal(JsonValueKind.Null, result.GetProperty("errors").ValueKind);
    }

    private static JsonElement Find(JsonElement profile, string section, string field, string value) =>
        profile.GetProperty(section).EnumerateArray().Single(e => e.GetProperty(field).GetString() == value);
    private static string? Error(JsonElement result) => result.GetProperty("errors")[0].GetProperty("__typename").GetString();
}
