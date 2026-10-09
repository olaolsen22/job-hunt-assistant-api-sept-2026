using System.Text.Json;
using JobHunt.Api.Tests.Features.Listings;

namespace JobHunt.Api.Tests.Features.Contacts;

public class ContactTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string AddContact = """
        mutation ($companyId: Int!, $contact: ContactInput!) {
          addContact(input: { companyId: $companyId, contact: $contact }) {
            contact { id name title email phone url notes }
            errors { __typename ... on DuplicateContactError { existingContactId } }
          }
        }
        """;

    private const string UpdateContact = """
        mutation ($id: Int!, $contact: ContactInput!) {
          updateContact(input: { id: $id, contact: $contact }) {
            contact { id name email }
            errors { __typename }
          }
        }
        """;

    private const string DeleteContact = """
        mutation ($id: Int!) { deleteContact(input: { id: $id }) { id errors { __typename } } }
        """;

    private const string SaveListing = """
        mutation ($listing: ListingInput!) {
          createListing(input: { listing: $listing }) {
            listing { id company { id contacts { id name email phone } } contacts { contact { id name email phone } } }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    private const string UpdateListing = """
        mutation ($id: Int!, $listing: ListingInput!) {
          updateListing(input: { id: $id, listing: $listing }) {
            listing { id company { contacts { id } } contacts { contact { id } } }
            errors { __typename }
          }
        }
        """;

    private const string ListingContacts = """
        query ($id: Int!) { listingById(id: $id) { contacts { contact { id } } } }
        """;

    [Fact]
    public async Task AddContact_SavesFieldsAndRejectsDuplicateName()
    {
        var client = await factory.GetOwnerClientAsync();
        var companyId = await CompanyIdAsync(client);

        var added = await AddAsync(client, companyId, new { name = " Kari Nordmann ", email = "kari@acme.no", phone = "900 00 000" });
        Assert.Equal("Kari Nordmann", added.GetProperty("contact").GetProperty("name").GetString());
        Assert.Equal("kari@acme.no", added.GetProperty("contact").GetProperty("email").GetString());

        var duplicate = await AddAsync(client, companyId, new { name = "kari nordmann" });
        var error = duplicate.GetProperty("errors")[0];
        Assert.Equal("DuplicateContactError", error.GetProperty("__typename").GetString());
        Assert.Equal(added.GetProperty("contact").GetProperty("id").GetInt32(), error.GetProperty("existingContactId").GetInt32());
    }

    [Fact]
    public async Task AddContact_RejectsBadEmailAndUrl()
    {
        var client = await factory.GetOwnerClientAsync();
        var companyId = await CompanyIdAsync(client);

        var badEmail = await AddAsync(client, companyId, new { name = "Kari", email = "kari.acme.no" });
        var badUrl = await AddAsync(client, companyId, new { name = "Kari", url = "javascript:alert(1)" });

        Assert.Equal("InvalidContactError", badEmail.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("InvalidContactError", badUrl.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task UpdateContact_ReplacesFields()
    {
        var client = await factory.GetOwnerClientAsync();
        var companyId = await CompanyIdAsync(client);
        var id = (await AddAsync(client, companyId, new { name = "Kari", email = "kari@acme.no" }))
            .GetProperty("contact").GetProperty("id").GetInt32();

        var data = await client.GraphQLAsync(UpdateContact, new { id, contact = new { name = "Kari Nordmann" } });
        var contact = data.GetProperty("updateContact").GetProperty("contact");

        Assert.Equal("Kari Nordmann", contact.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, contact.GetProperty("email").ValueKind);
    }

    [Fact]
    public async Task Contacts_AreHiddenFromOtherUsers()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var id = (await AddAsync(owner, await CompanyIdAsync(owner), new { name = "Kari" }))
            .GetProperty("contact").GetProperty("id").GetInt32();

        var update = await guest.GraphQLAsync(UpdateContact, new { id, contact = new { name = "Hacked" } });
        var delete = await guest.GraphQLAsync(DeleteContact, new { id });

        Assert.Equal("ContactNotFoundError", update.GetProperty("updateContact").GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal("ContactNotFoundError", delete.GetProperty("deleteContact").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task ListingContacts_CreateThenReuseAndFillEmptyFields()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");

        var first = await CreateAsync(client, company, [new { name = "Ola Hansen", phone = "900 00 000" }]);
        var second = await CreateAsync(client, company, [new { name = "ola hansen", email = "ola@acme.no", phone = "111" }]);

        var contacts = second.GetProperty("company").GetProperty("contacts");
        Assert.Equal(1, contacts.GetArrayLength());
        Assert.Equal("900 00 000", contacts[0].GetProperty("phone").GetString());
        Assert.Equal("ola@acme.no", contacts[0].GetProperty("email").GetString());
        Assert.Equal(contacts[0].GetProperty("id").GetInt32(),
            first.GetProperty("contacts")[0].GetProperty("contact").GetProperty("id").GetInt32());
        Assert.Equal(contacts[0].GetProperty("id").GetInt32(),
            second.GetProperty("contacts")[0].GetProperty("contact").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task UpdateListing_EmptyContactsUnlinksAndNullKeeps()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        var url = ListingTests.UniqueUrl();
        var listing = await CreateAsync(client, company, [new { name = "Ola" }], url);
        var id = listing.GetProperty("id").GetInt32();

        var kept = await client.GraphQLAsync(UpdateListing, new { id, listing = ListingTests.NewListing(company, url: url) });
        Assert.Equal(1, kept.GetProperty("updateListing").GetProperty("listing").GetProperty("contacts").GetArrayLength());

        var cleared = await client.GraphQLAsync(UpdateListing, new { id, listing = WithContacts(company, [], url) });
        var after = cleared.GetProperty("updateListing").GetProperty("listing");
        Assert.Equal(0, after.GetProperty("contacts").GetArrayLength());
        Assert.Equal(1, after.GetProperty("company").GetProperty("contacts").GetArrayLength());
    }

    [Fact]
    public async Task DeleteContact_UnlinksButKeepsListings()
    {
        var client = await factory.GetOwnerClientAsync();
        var company = ListingTests.Unique("Acme");
        var first = await CreateAsync(client, company, [new { name = "Ola" }]);
        var second = await CreateAsync(client, company, [new { name = "Ola" }]);
        var contactId = first.GetProperty("contacts")[0].GetProperty("contact").GetProperty("id").GetInt32();

        await client.GraphQLAsync(DeleteContact, new { id = contactId });

        foreach (var listing in new[] { first, second })
        {
            var data = await client.GraphQLAsync(ListingContacts, new { id = listing.GetProperty("id").GetInt32() });
            Assert.Equal(0, data.GetProperty("listingById").GetProperty("contacts").GetArrayLength());
        }
    }

    [Fact]
    public async Task CreateListing_RejectsContactListedTwice()
    {
        var client = await factory.GetOwnerClientAsync();
        var data = await client.GraphQLAsync(SaveListing,
            new { listing = WithContacts(ListingTests.Unique("Acme"), [new { name = "Ola" }, new { name = "OLA" }]) });
        Assert.Equal("InvalidListingError",
            data.GetProperty("createListing").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    private static Dictionary<string, object?> WithContacts(string company, object[] contacts, string? url = null)
    {
        var listing = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(ListingTests.NewListing(company, url: url)))!;
        listing["contacts"] = contacts;
        return listing;
    }

    private static async Task<JsonElement> CreateAsync(HttpClient client, string company, object[] contacts, string? url = null)
    {
        var data = await client.GraphQLAsync(SaveListing, new { listing = WithContacts(company, contacts, url) });
        return data.GetProperty("createListing").GetProperty("listing");
    }

    private static async Task<JsonElement> AddAsync(HttpClient client, int companyId, object contact) =>
        (await client.GraphQLAsync(AddContact, new { companyId, contact })).GetProperty("addContact");

    private static async Task<int> CompanyIdAsync(HttpClient client)
    {
        var listing = await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique("Acme")));
        return listing.GetProperty("company").GetProperty("id").GetInt32();
    }
}
