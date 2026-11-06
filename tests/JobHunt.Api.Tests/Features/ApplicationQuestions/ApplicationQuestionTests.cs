using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Tests.Features.Listings;
using JobHunt.Api.Tests.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.ApplicationQuestions;

public class ApplicationQuestionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Add = """
        mutation ($listingId: Int!, $question: String!, $charLimit: Int, $answer: String, $notes: String) {
          addApplicationQuestion(input: { listingId: $listingId, question: $question, charLimit: $charLimit,
            answer: $answer, notes: $notes }) {
            applicationQuestion { id question charLimit answer notes }
            errors { __typename ... on Error { message } }
          }
        }
        """;
    private const string Update = """
        mutation ($id: Int!, $question: String!, $charLimit: Int, $answer: String, $notes: String) {
          updateApplicationQuestion(input: { id: $id, question: $question, charLimit: $charLimit,
            answer: $answer, notes: $notes }) {
            applicationQuestion { id question charLimit answer notes }
            errors { __typename }
          }
        }
        """;
    private const string Generate = """
        mutation ($questionId: Int!) {
          generateApplicationAnswer(input: { questionId: $questionId }) {
            result { status error errorCode candidate { answer length overLimit } }
            errors { __typename }
          }
        }
        """;
    private const string Preview = """
        mutation ($questionId: Int!, $json: String!) {
          previewApplicationAnswer(input: { questionId: $questionId, json: $json }) {
            candidate { answer length overLimit }
            errors { __typename }
          }
        }
        """;

    [Fact]
    public async Task AddUpdateDelete_NormalizesAndListsOnTheListing()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);

        // \r\n counts as one character, as in a browser textarea: 13 raw characters fit a limit of 12.
        var added = (await client.GraphQLAsync(Add, new
        {
            listingId, question = "  Why us?  ", charLimit = 12, answer = "Line one\r\nTwo", notes = "  Mention Vue  ",
        })).GetProperty("addApplicationQuestion").GetProperty("applicationQuestion");
        Assert.Equal("Why us?", added.GetProperty("question").GetString());
        Assert.Equal("Line one\nTwo", added.GetProperty("answer").GetString());
        Assert.Equal("Mention Vue", added.GetProperty("notes").GetString());
        var id = added.GetProperty("id").GetInt32();
        var updated = (await client.GraphQLAsync(Update, new { id, question = "Why us?", charLimit = (int?)null, answer = "Rewritten" }))
            .GetProperty("updateApplicationQuestion").GetProperty("applicationQuestion");
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("charLimit").ValueKind);
        Assert.Equal(JsonValueKind.Null, updated.GetProperty("notes").ValueKind);

        var listing = await client.GraphQLAsync("""
            query ($id: Int!) { listingById(id: $id) { applicationQuestions(order: { id: ASC }) { id answer } } }
            """, new { id = listingId });
        Assert.Equal(id, listing.GetProperty("listingById").GetProperty("applicationQuestions")[0].GetProperty("id").GetInt32());

        var deleted = await client.GraphQLAsync("mutation ($id: Int!) { deleteApplicationQuestion(input: { id: $id }) { id } }", new { id });
        Assert.Equal(id, deleted.GetProperty("deleteApplicationQuestion").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Add_InvalidInput_IsRejected()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);

        foreach (var input in new object[]
        {
            new { listingId, question = "   " },
            new { listingId, question = "Why us?", charLimit = 0 },
            new { listingId, question = "Why us?", charLimit = 10_001 },
            new { listingId, question = "Why us?", charLimit = 5, answer = "Too long" },
            new { listingId, question = "Why us?", notes = new string('n', 2001) },
        })
        {
            var payload = (await client.GraphQLAsync(Add, input)).GetProperty("addApplicationQuestion");
            Assert.Equal("InvalidApplicationQuestionError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        }
    }

    [Fact]
    public async Task OtherUsersQuestions_AreNotFound()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var listingId = await CreateListingAsync(owner);
        var id = await AddAsync(owner, listingId, "Why us?");

        var add = (await guest.GraphQLAsync(Add, new { listingId, question = "Mine?" })).GetProperty("addApplicationQuestion");
        Assert.Equal("ListingNotFoundError", add.GetProperty("errors")[0].GetProperty("__typename").GetString());
        var update = (await guest.GraphQLAsync(Update, new { id, question = "Hijack" })).GetProperty("updateApplicationQuestion");
        Assert.Equal("ApplicationQuestionNotFoundError", update.GetProperty("errors")[0].GetProperty("__typename").GetString());
        var generate = (await guest.GraphQLAsync(Generate, new { questionId = id })).GetProperty("generateApplicationAnswer");
        Assert.Equal("ApplicationQuestionNotFoundError", generate.GetProperty("errors")[0].GetProperty("__typename").GetString());
        var delete = (await guest.GraphQLAsync("mutation ($id: Int!) { deleteApplicationQuestion(input: { id: $id }) { errors { __typename } } }", new { id }))
            .GetProperty("deleteApplicationQuestion");
        Assert.Equal("ApplicationQuestionNotFoundError", delete.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Generate_SendsQuestionNotesAndOtherAnswers_AndNeverSaves()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        await AddAsync(client, listingId, "OTHER_QUESTION", answer: "OTHER_ANSWER");
        var id = await AddAsync(client, listingId, "QUESTION_TEXT", charLimit: 500, notes: "QUESTION_NOTES");
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"answer":"Short answer."}"""));

        var result = (await client.GraphQLAsync(Generate, new { questionId = id }))
            .GetProperty("generateApplicationAnswer").GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        var candidate = result.GetProperty("candidate");
        Assert.Equal("Short answer.", candidate.GetProperty("answer").GetString());
        Assert.Equal(13, candidate.GetProperty("length").GetInt32());
        Assert.False(candidate.GetProperty("overLimit").GetBoolean());
        var prompt = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        foreach (var expected in new[] { "QUESTION_TEXT", "QUESTION_NOTES", "OTHER_QUESTION", "OTHER_ANSWER", "Build services", "at most 500 characters" })
            Assert.Contains(expected, prompt);
        var saved = await client.GraphQLAsync("query ($id: Int!) { listingById(id: $id) { applicationQuestions(order: { id: DESC }) { answer } } }",
            new { id = listingId });
        Assert.Equal(JsonValueKind.Null, saved.GetProperty("listingById").GetProperty("applicationQuestions")[0].GetProperty("answer").ValueKind);
    }

    [Fact]
    public async Task Generate_SendsWorkMonthsAndLocations_ButNoPrivateDetails()
    {
        var client = await factory.GetOwnerClientAsync();
        await client.GraphQLAsync("""
            mutation {
              updateProfile(input: { fullName: "Kari Nordmann", location: { city: "Bergen", countryCode: "NO" },
                workAuthorization: "PRIVATE_AUTH", nationality: "PRIVATE_NATIONALITY", hobbies: [], links: [] }) { errors { __typename } }
              updateProfileAddress(input: { streetAddress: "PRIVATE_STREET", postalCode: "PRIVATE_POSTAL" }) { errors { __typename } }
              updateProfilePreferences(input: { jobRoleNames: [], workModes: [HYBRID], employmentTypes: [],
                minSalary: 987654, salaryCurrency: "NOK", noticePeriodMonths: 3 }) { errors { __typename } }
              saveLocation(input: { city: "Trondheim", countryCode: "NO", isTarget: true }) { errors { __typename } }
            }
            """);
        // Overlapping jobs count once: Jan 2020 to Jun 2022 is 30 months, not 24 + 18.
        var skill = ListingTests.Unique("Svelte");
        await ProfileEntryTests.AddExperienceAsync(client, employer: "First", start: "2020-01-01", end: "2021-12-01", skills: [skill]);
        await ProfileEntryTests.AddExperienceAsync(client, employer: "Second", start: "2021-01-01", end: "2022-06-01", skills: [skill]);
        var listingId = await CreateListingAsync(client);
        var id = await AddAsync(client, listingId, "How many years have you worked with Svelte?");
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, """{"answer":"Two and a half years."}"""));

        await client.GraphQLAsync(Generate, new { questionId = id });

        var prompt = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        Assert.Contains($"{{\"name\":\"{skill}\",\"workMonths\":30}}", prompt);
        Assert.Contains("\"homeLocation\":\"Bergen, Norway\"", prompt);
        Assert.Contains("Trondheim, Norway", prompt);
        Assert.Contains("\"workModes\":[\"HYBRID\"]", prompt);
        Assert.Contains("[fill in: salary expectation]", prompt);
        Assert.DoesNotContain("PRIVATE_", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("987654", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Generate_OverLimit_RetriesOnceWithTheMeasuredLength()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var id = await AddAsync(client, listingId, "Why us?", charLimit: 20);
        var answers = new Queue<string>([new string('a', 30), "Fits the limit."]);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new { answer = answers.Dequeue() })));
        var before = await UsageCountAsync();

        var candidate = (await client.GraphQLAsync(Generate, new { questionId = id }))
            .GetProperty("generateApplicationAnswer").GetProperty("result").GetProperty("candidate");

        Assert.Equal("Fits the limit.", candidate.GetProperty("answer").GetString());
        Assert.False(candidate.GetProperty("overLimit").GetBoolean());
        Assert.Contains("That answer is 30 characters", factory.Llm.LastMessages[^1].Text);
        Assert.Equal(before + 2, await UsageCountAsync());
    }

    [Fact]
    public async Task Generate_StillOverLimit_ReturnsTheAnswerFlagged()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var id = await AddAsync(client, listingId, "Why us?", charLimit: 20);
        var answers = new Queue<string>([new string('a', 30), new string('b', 25)]);
        factory.Llm.Respond = () => new ChatResponse(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new { answer = answers.Dequeue() })));

        var candidate = (await client.GraphQLAsync(Generate, new { questionId = id }))
            .GetProperty("generateApplicationAnswer").GetProperty("result").GetProperty("candidate");

        Assert.Equal(25, candidate.GetProperty("length").GetInt32());
        Assert.True(candidate.GetProperty("overLimit").GetBoolean());
    }

    [Fact]
    public async Task ExternalPrompt_AndPreview()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var id = await AddAsync(client, listingId, "Why us?", charLimit: 10, answer: "MY_DRAFT", notes: "QUESTION_NOTES");

        var prompt = (await client.GraphQLAsync("""
            query ($questionId: Int!) { applicationAnswerExternalPrompt(questionId: $questionId) { version instructions } }
            """, new { questionId = id })).GetProperty("applicationAnswerExternalPrompt");
        Assert.Equal(3, prompt.GetProperty("version").GetInt32());
        foreach (var expected in new[] { "QUESTION_NOTES", "\"draft\":\"MY_DRAFT\"", "Build services", "\"schemaVersion\": 3", "at most 10 characters" })
            Assert.Contains(expected, prompt.GetProperty("instructions").GetString());

        var flagged = (await client.GraphQLAsync(Preview, new { questionId = id, json = """{"schemaVersion":3,"answer":" Longer than ten "}""" }))
            .GetProperty("previewApplicationAnswer").GetProperty("candidate");
        Assert.Equal("Longer than ten", flagged.GetProperty("answer").GetString());
        Assert.True(flagged.GetProperty("overLimit").GetBoolean());

        foreach (var json in new[] { """{"answer":"No version"}""", """{"schemaVersion":2,"answer":"Stale prompt"}""", """{"schemaVersion":3,"answer":"  "}""", "not json" })
        {
            var invalid = (await client.GraphQLAsync(Preview, new { questionId = id, json })).GetProperty("previewApplicationAnswer");
            Assert.Equal("InvalidApplicationQuestionError", invalid.GetProperty("errors")[0].GetProperty("__typename").GetString());
        }
    }

    private async Task<int> UsageCountAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().LlmUsages
            .IgnoreQueryFilters().CountAsync(u => u.Operation == "application-answer");
    }

    private static async Task<int> AddAsync(HttpClient client, int listingId, string question, int? charLimit = null,
        string? answer = null, string? notes = null) =>
        (await client.GraphQLAsync(Add, new { listingId, question, charLimit, answer, notes }))
            .GetProperty("addApplicationQuestion").GetProperty("applicationQuestion").GetProperty("id").GetInt32();

    private static async Task<int> CreateListingAsync(HttpClient client)
    {
        var listing = new
        {
            title = "Backend developer", companyName = "Question company", jobRoleName = "Developer",
            description = "Build services", url = $"https://questions.example/{Guid.NewGuid()}", postedOn = "2026-10-01",
            deadlineType = "UNSPECIFIED", skills = Array.Empty<object>(), languages = Array.Empty<object>(),
            locations = Array.Empty<object>(),
        };
        var data = await client.GraphQLAsync("""
            mutation ($listing: ListingInput!) { createListing(input: { listing: $listing }) { listing { id } } }
            """, new { listing });
        return data.GetProperty("createListing").GetProperty("listing").GetProperty("id").GetInt32();
    }
}
