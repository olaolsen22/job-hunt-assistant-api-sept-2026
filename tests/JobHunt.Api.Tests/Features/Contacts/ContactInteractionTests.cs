using System.Text.Json;
using JobHunt.Api.Tests.Features.Listings;

namespace JobHunt.Api.Tests.Features.Contacts;

public class ContactInteractionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AddInteraction = """
        mutation ($contactId: Int!, $interaction: ContactInteractionInput!) {
          addContactInteraction(input: { contactId: $contactId, interaction: $interaction }) {
            contactInteraction { id occurredAt direction channel note followUpOn contact { lastInteractionAt followUpOn } }
            errors { __typename }
          }
        }
        """;

    private const string UpdateInteraction = """
        mutation ($id: Int!, $interaction: ContactInteractionInput!) {
          updateContactInteraction(input: { id: $id, interaction: $interaction }) {
            contactInteraction { id note contact { followUpOn } }
            errors { __typename }
          }
        }
        """;

    private const string DeleteInteraction = """
        mutation ($id: Int!) { deleteContactInteraction(input: { id: $id }) { id errors { __typename } } }
        """;

    private const string Contacts = """
        query ($search: String) {
          contacts(search: $search, first: 20) {
            nodes { id name lastInteractionAt followUpOn }
          }
        }
        """;

    private const string ContactById = """
        query ($id: Int!) {
          contactById(id: $id) { id name lastInteractionAt followUpOn interactions(order: [{ occurredAt: DESC }]) { id note } }
        }
        """;

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static object Interaction(DateTimeOffset occurredAt, string direction = "SENT", string? followUpOn = null,
        string? note = null) =>
        new { occurredAt, direction, channel = "LINKED_IN", note, followUpOn };

    private static string Day(int daysFromNow) => DateOnly.FromDateTime(Now.UtcDateTime.AddDays(daysFromNow)).ToString("yyyy-MM-dd");

    private static async Task<(int Id, string Name)> ContactAsync(HttpClient client)
    {
        var listing = await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Consult")));
        var name = ListingTests.Unique("Kari");
        var data = await client.GraphQLAsync("""
            mutation ($companyId: Int!, $name: String!) {
              addContact(input: { companyId: $companyId, contact: { name: $name } }) { contact { id } }
            }
            """, new { companyId = listing.GetProperty("company").GetProperty("id").GetInt32(), name });
        return (data.GetProperty("addContact").GetProperty("contact").GetProperty("id").GetInt32(), name);
    }

    private static async Task<JsonElement> AddAsync(HttpClient client, int contactId, object interaction) =>
        (await client.GraphQLAsync(AddInteraction, new { contactId, interaction })).GetProperty("addContactInteraction");

    // Found through the people list, then read in full like a detail page would.
    private static async Task<JsonElement> FindAsync(HttpClient client, string name)
    {
        var node = Assert.Single((await client.GraphQLAsync(Contacts, new { search = name }))
            .GetProperty("contacts").GetProperty("nodes").EnumerateArray());
        var contact = (await client.GraphQLAsync(ContactById, new { id = node.GetProperty("id").GetInt32() })).GetProperty("contactById");
        Assert.Equal(node.GetProperty("followUpOn").ToString(), contact.GetProperty("followUpOn").ToString());
        return contact;
    }

    [Fact]
    public async Task Interactions_KeepTheContactsLatestDateAndFollowUp()
    {
        var client = await factory.GetOwnerClientAsync();
        var (contactId, name) = await ContactAsync(client);

        var sent = (await AddAsync(client, contactId, Interaction(Now.AddDays(-5), followUpOn: Day(3), note: "Asked about .NET")))
            .GetProperty("contactInteraction");
        Assert.Equal("LINKED_IN", sent.GetProperty("channel").GetString());
        Assert.Equal(Day(3), sent.GetProperty("contact").GetProperty("followUpOn").GetString());

        // Logged late: an older interaction doesn't take over from the newer one.
        await AddAsync(client, contactId, Interaction(Now.AddDays(-9), followUpOn: Day(-2)));
        var contact = await FindAsync(client, name);
        Assert.Equal(Day(3), contact.GetProperty("followUpOn").GetString());

        // A reply with no follow-up date clears it.
        var reply = (await AddAsync(client, contactId, Interaction(Now.AddDays(-1), direction: "RECEIVED")))
            .GetProperty("contactInteraction");
        contact = await FindAsync(client, name);
        Assert.Equal(JsonValueKind.Null, contact.GetProperty("followUpOn").ValueKind);
        Assert.Equal(reply.GetProperty("occurredAt").GetString(), contact.GetProperty("lastInteractionAt").GetString());
        Assert.Equal(3, contact.GetProperty("interactions").GetArrayLength());
        Assert.Equal(reply.GetProperty("id").GetInt32(), contact.GetProperty("interactions")[0].GetProperty("id").GetInt32());

        // Deleting the reply brings the earlier follow-up back.
        var deleted = await client.GraphQLAsync(DeleteInteraction, new { id = reply.GetProperty("id").GetInt32() });
        Assert.Equal(reply.GetProperty("id").GetInt32(), deleted.GetProperty("deleteContactInteraction").GetProperty("id").GetInt32());
        Assert.Equal(Day(3), (await FindAsync(client, name)).GetProperty("followUpOn").GetString());

        var updated = (await client.GraphQLAsync(UpdateInteraction, new
        {
            id = sent.GetProperty("id").GetInt32(), interaction = Interaction(Now.AddDays(-5), followUpOn: Day(7), note: "Edited"),
        })).GetProperty("updateContactInteraction").GetProperty("contactInteraction");
        Assert.Equal("Edited", updated.GetProperty("note").GetString());
        Assert.Equal(Day(7), updated.GetProperty("contact").GetProperty("followUpOn").GetString());
    }

    [Fact]
    public async Task Contacts_FiltersFollowUpsDue()
    {
        var client = await factory.GetOwnerClientAsync();
        var (due, dueName) = await ContactAsync(client);
        var (later, _) = await ContactAsync(client);
        await AddAsync(client, due, Interaction(Now.AddDays(-10), followUpOn: Day(-1)));
        await AddAsync(client, later, Interaction(Now.AddDays(-10), followUpOn: Day(30)));

        // Inline where: a whole ContactFilterInput variable is priced past the cost limit.
        const string dueBy = """
            query ($today: LocalDate!) {
              contacts(where: { followUpOn: { lte: $today } }, order: [{ followUpOn: ASC }], first: 50) { nodes { id } }
            }
            """;
        var nodes = (await client.GraphQLAsync(dueBy, new { today = Day(0) }))
            .GetProperty("contacts").GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("id").GetInt32()).ToList();

        Assert.Contains(due, nodes);
        Assert.DoesNotContain(later, nodes);
        Assert.Equal(dueName, (await FindAsync(client, dueName)).GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("FUTURE")]
    [InlineData("FOLLOW_UP_BEFORE")]
    [InlineData("LONG_NOTE")]
    public async Task AddInteraction_RejectsBadInput(string problem)
    {
        var client = await factory.GetOwnerClientAsync();
        var (contactId, name) = await ContactAsync(client);
        var interaction = problem switch
        {
            "FUTURE" => Interaction(Now.AddDays(1)),
            "FOLLOW_UP_BEFORE" => Interaction(Now.AddDays(-3), followUpOn: Day(-5)),
            _ => Interaction(Now.AddDays(-1), note: new string('a', 10_001)),
        };

        var result = await AddAsync(client, contactId, interaction);

        Assert.Equal("InvalidContactInteractionError", result.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Empty((await FindAsync(client, name)).GetProperty("interactions").EnumerateArray());
    }

    [Fact]
    public async Task Interactions_AreHiddenFromOtherUsers()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var (contactId, name) = await ContactAsync(owner);
        var id = (await AddAsync(owner, contactId, Interaction(Now.AddDays(-1))))
            .GetProperty("contactInteraction").GetProperty("id").GetInt32();

        Assert.Equal("ContactNotFoundError",
            (await AddAsync(guest, contactId, Interaction(Now.AddDays(-1)))).GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("ContactInteractionNotFoundError", (await guest.GraphQLAsync(UpdateInteraction, new { id, interaction = Interaction(Now) }))
            .GetProperty("updateContactInteraction").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("ContactInteractionNotFoundError", (await guest.GraphQLAsync(DeleteInteraction, new { id }))
            .GetProperty("deleteContactInteraction").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Empty((await guest.GraphQLAsync(Contacts, new { search = name }))
            .GetProperty("contacts").GetProperty("nodes").EnumerateArray());
        Assert.Equal(JsonValueKind.Null,
            (await guest.GraphQLAsync(ContactById, new { id = contactId })).GetProperty("contactById").ValueKind);
        Assert.Single((await FindAsync(owner, name)).GetProperty("interactions").EnumerateArray());
    }

    [Fact]
    public async Task DeleteContact_RemovesItsInteractions()
    {
        var client = await factory.GetOwnerClientAsync();
        var (contactId, _) = await ContactAsync(client);
        var id = (await AddAsync(client, contactId, Interaction(Now.AddDays(-1))))
            .GetProperty("contactInteraction").GetProperty("id").GetInt32();

        await client.GraphQLAsync("mutation ($id: Int!) { deleteContact(input: { id: $id }) { id } }", new { id = contactId });

        Assert.Equal("ContactInteractionNotFoundError", (await client.GraphQLAsync(DeleteInteraction, new { id }))
            .GetProperty("deleteContactInteraction").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }
}
