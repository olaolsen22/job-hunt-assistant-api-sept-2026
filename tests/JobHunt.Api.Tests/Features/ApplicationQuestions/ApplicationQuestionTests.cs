using System.Text.Json;
using JobHunt.Api.Data;
using JobHunt.Api.Features.ApplicationQuestions;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Tests.Features.Listings;
using JobHunt.Api.Tests.Features.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace JobHunt.Api.Tests.Features.ApplicationQuestions;

public class ApplicationQuestionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Honesty rules hold for every question, so they sit with the essentials, once, not under one question type.
    [Fact]
    public void Instructions_ListHonestyRulesAsEssentials_WithoutAFinalChecklist()
    {
        var instructions = ApplicationAnswerPrompt.Instructions(Language.English, null);

        var types = instructions.IndexOf("Question types:", StringComparison.Ordinal);
        foreach (var rule in new[] { "never round up", "never as a regular job", "[fill in: salary expectation]",
                     "add no productivity gains", "criticize former employers" })
        {
            Assert.True(instructions.IndexOf(rule, StringComparison.Ordinal) is var at && at >= 0 && at < types, rule);
            Assert.Equal(2, instructions.Split(rule).Length);
        }
        Assert.DoesNotContain("Before returning, check", instructions);
    }

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
        mutation ($listingId: Int!) {
          generateApplicationAnswers(input: { listingId: $listingId }) {
            result { status error errorCode candidates { questionId answer length overLimit } }
            errors { __typename }
          }
        }
        """;
    private const string Preview = """
        mutation ($listingId: Int!, $json: String!) {
          previewApplicationAnswers(input: { listingId: $listingId, json: $json }) {
            candidates { questionId answer length overLimit }
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
        var generate = (await guest.GraphQLAsync(Generate, new { listingId })).GetProperty("generateApplicationAnswers");
        Assert.Equal("ListingNotFoundError", generate.GetProperty("errors")[0].GetProperty("__typename").GetString());
        var preview = (await guest.GraphQLAsync(Preview, new { listingId, json = "{}" })).GetProperty("previewApplicationAnswers");
        Assert.Equal("ListingNotFoundError", preview.GetProperty("errors")[0].GetProperty("__typename").GetString());
        var delete = (await guest.GraphQLAsync("mutation ($id: Int!) { deleteApplicationQuestion(input: { id: $id }) { errors { __typename } } }", new { id }))
            .GetProperty("deleteApplicationQuestion");
        Assert.Equal("ApplicationQuestionNotFoundError", delete.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Generate_AnswersTheWholeFormInOneCall_AndNeverSaves()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var first = await AddAsync(client, listingId, "WHY_US_QUESTION", answer: "MY_DRAFT");
        var second = await AddAsync(client, listingId, "EXPERIENCE_QUESTION", charLimit: 500, notes: "QUESTION_NOTES");
        factory.Llm.Respond = () => Reply((second, "Experience answer."), (first, "Short answer."));
        var before = await UsageCountAsync();

        var result = (await client.GraphQLAsync(Generate, new { listingId }))
            .GetProperty("generateApplicationAnswers").GetProperty("result");

        Assert.Equal("SUCCEEDED", result.GetProperty("status").GetString());
        var candidates = result.GetProperty("candidates");
        Assert.Equal(first, candidates[0].GetProperty("questionId").GetInt32());
        Assert.Equal("Short answer.", candidates[0].GetProperty("answer").GetString());
        Assert.Equal(13, candidates[0].GetProperty("length").GetInt32());
        Assert.Equal(second, candidates[1].GetProperty("questionId").GetInt32());
        Assert.False(candidates[1].GetProperty("overLimit").GetBoolean());
        Assert.Equal(before + 1, await UsageCountAsync());
        // The task is restated after the data.
        Assert.EndsWith("return one JSON object only.", factory.Llm.LastMessages[1].Text.TrimEnd());
        Assert.Contains("Reminder: answer every question", factory.Llm.LastMessages[1].Text);
        var prompt = string.Join("\n", factory.Llm.LastMessages.Select(m => m.Text));
        foreach (var expected in new[]
        {
            "WHY_US_QUESTION", "\"draft\":\"MY_DRAFT\"", "EXPERIENCE_QUESTION", "QUESTION_NOTES", "Build services",
            "\"lengthTarget\":\"425-450 characters, never more than 500\"",
        })
            Assert.Contains(expected, prompt);
        var saved = await client.GraphQLAsync("query ($id: Int!) { listingById(id: $id) { applicationQuestions(order: { id: ASC }) { answer } } }",
            new { id = listingId });
        var questions = saved.GetProperty("listingById").GetProperty("applicationQuestions");
        Assert.Equal("MY_DRAFT", questions[0].GetProperty("answer").GetString());
        Assert.Equal(JsonValueKind.Null, questions[1].GetProperty("answer").ValueKind);
    }

    [Fact]
    public async Task Generate_NoQuestionsOrAnIncompleteReply_Fails()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var empty = (await client.GraphQLAsync(Generate, new { listingId })).GetProperty("generateApplicationAnswers");
        Assert.Equal("InvalidApplicationQuestionError", empty.GetProperty("errors")[0].GetProperty("__typename").GetString());

        var first = await AddAsync(client, listingId, "Why us?");
        await AddAsync(client, listingId, "Experience?");
        factory.Llm.Respond = () => Reply((first, "Only one answer."));
        var result = (await client.GraphQLAsync(Generate, new { listingId }))
            .GetProperty("generateApplicationAnswers").GetProperty("result");
        Assert.Equal("FAILED", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("candidates").ValueKind);
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
        factory.Llm.Respond = () => Reply((id, "Two and a half years."));

        await client.GraphQLAsync(Generate, new { listingId });

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
    public async Task Generate_OverLimit_RetriesOnlyThoseAnswersOnce()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var limited = await AddAsync(client, listingId, "Why us?", charLimit: 20);
        var open = await AddAsync(client, listingId, "Experience?");
        var replies = new Queue<ChatResponse>([
            Reply((limited, new string('a', 30)), (open, "Kept as is.")),
            Reply((limited, "Fits the limit.")),
        ]);
        factory.Llm.Respond = () => replies.Dequeue();
        var before = await UsageCountAsync();

        var candidates = (await client.GraphQLAsync(Generate, new { listingId }))
            .GetProperty("generateApplicationAnswers").GetProperty("result").GetProperty("candidates");

        Assert.Equal("Fits the limit.", candidates[0].GetProperty("answer").GetString());
        Assert.False(candidates[0].GetProperty("overLimit").GetBoolean());
        Assert.Equal("Kept as is.", candidates[1].GetProperty("answer").GetString());
        var retry = factory.Llm.LastMessages[^1].Text;
        Assert.Contains($"questionId {limited}: 30 characters", retry);
        Assert.DoesNotContain($"questionId {open}:", retry);
        Assert.Equal(before + 2, await UsageCountAsync());
    }

    [Fact]
    public async Task Generate_StillOverLimit_ReturnsTheShorterAnswerFlagged()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var id = await AddAsync(client, listingId, "Why us?", charLimit: 20);
        var replies = new Queue<ChatResponse>([Reply((id, new string('a', 30))), Reply((id, new string('b', 25)))]);
        factory.Llm.Respond = () => replies.Dequeue();

        var candidate = (await client.GraphQLAsync(Generate, new { listingId }))
            .GetProperty("generateApplicationAnswers").GetProperty("result").GetProperty("candidates")[0];

        Assert.Equal(25, candidate.GetProperty("length").GetInt32());
        Assert.True(candidate.GetProperty("overLimit").GetBoolean());
    }

    [Fact]
    public async Task ExternalPrompt_AndPreview()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client);
        var id = await AddAsync(client, listingId, "Why us?", charLimit: 10, answer: "MY_DRAFT", notes: "QUESTION_NOTES");
        var other = await AddAsync(client, listingId, "OTHER_QUESTION");

        var prompt = (await client.GraphQLAsync("""
            query ($listingId: Int!) { applicationAnswerExternalPrompt(listingId: $listingId) { version instructions } }
            """, new { listingId })).GetProperty("applicationAnswerExternalPrompt");
        Assert.Equal(6, prompt.GetProperty("version").GetInt32());
        foreach (var expected in new[] { "QUESTION_NOTES", "\"draft\":\"MY_DRAFT\"", "OTHER_QUESTION", "Build services", "\"schemaVersion\": 6", "never more than 10" })
            Assert.Contains(expected, prompt.GetProperty("instructions").GetString());

        var json = $$"""{"schemaVersion":6,"answers":[{"questionId":{{id}},"answer":" Longer than ten "},{"questionId":{{other}},"answer":"Other."}]}""";
        var candidates = (await client.GraphQLAsync(Preview, new { listingId, json }))
            .GetProperty("previewApplicationAnswers").GetProperty("candidates");
        Assert.Equal("Longer than ten", candidates[0].GetProperty("answer").GetString());
        Assert.True(candidates[0].GetProperty("overLimit").GetBoolean());
        Assert.Equal(other, candidates[1].GetProperty("questionId").GetInt32());

        foreach (var invalid in new[]
        {
            $$"""{"answers":[{"questionId":{{id}},"answer":"A"},{"questionId":{{other}},"answer":"B"}]}""",
            $$"""{"schemaVersion":3,"answers":[{"questionId":{{id}},"answer":"A"},{"questionId":{{other}},"answer":"B"}]}""",
            $$"""{"schemaVersion":6,"answers":[{"questionId":{{id}},"answer":"  "},{"questionId":{{other}},"answer":"B"}]}""",
            $$"""{"schemaVersion":6,"answers":[{"questionId":{{id}},"answer":"A"}]}""",
            $$"""{"schemaVersion":6,"answers":[{"questionId":{{id}},"answer":"A"},{"questionId":{{id}},"answer":"B"}]}""",
            $$"""{"schemaVersion":6,"answers":[{"questionId":{{id}},"answer":"A"},{"questionId":{{other}},"answer":"B"},{"questionId":-1,"answer":"C"}]}""",
            "not json",
        })
        {
            var payload = (await client.GraphQLAsync(Preview, new { listingId, json = invalid })).GetProperty("previewApplicationAnswers");
            Assert.Equal("InvalidApplicationQuestionError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        }
    }

    private static ChatResponse Reply(params (int QuestionId, string Answer)[] answers) =>
        new(new ChatMessage(ChatRole.Assistant, JsonSerializer.Serialize(new
        {
            answers = answers.Select(a => new { questionId = a.QuestionId, answer = a.Answer }),
        })));

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
