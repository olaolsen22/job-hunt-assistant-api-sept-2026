using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using JobHunt.Api.Tests.Features.Documents;
using UglyToad.PdfPig;

namespace JobHunt.Api.Tests.Features.TailoredCvDrafts;

public class GenerateCvDocumentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Generate_StoresReviewedDraftAsDownloadableUnsentCv()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(client, "Generated CV");
        await SaveAsync(client, listingId, "Reviewed Søknadsbehandler with efficient office skills.");

        var payload = await GenerateAsync(client, listingId);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("errors").ValueKind);
        var document = payload.GetProperty("document");
        var id = document.GetProperty("id").GetInt32();
        Assert.Equal("CV", document.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("sentAt").ValueKind);
        Assert.Matches(@"^Generated-CV-[0-9a-f]{32}_Backend-Developer_Cv_\d{4}-\d{2}-\d{2}\.pdf$",
            document.GetProperty("fileName").GetString());
        Assert.Equal(document.GetProperty("fileName").GetString(), document.GetProperty("originalFileName").GetString());

        var response = await client.GetAsync($"/api/documents/{id}");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(document.GetProperty("fileName").GetString(), response.Content.Headers.ContentDisposition!.FileNameStar);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes.Length, document.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), document.GetProperty("contentHash").GetString());
        using var pdf = PdfDocument.Open(bytes);
        Assert.Contains("Reviewed Søknadsbehandler with efficient office skills.", string.Join("\n", pdf.GetPages().Select(p => p.Text)));
        var listing = await GetListingAsync(client, listingId);
        Assert.Equal("SAVED", listing.GetProperty("status").GetString());
        Assert.Equal(id, Assert.Single(listing.GetProperty("documents").EnumerateArray()).GetProperty("id").GetInt32());
        var guest = await factory.GetGuestClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/documents/{id}")).StatusCode);
    }

    [Fact]
    public async Task GenerateTwice_PreservesExistingUploadedAndGeneratedDocuments()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(client, "Repeated CV");
        var uploadId = await DocumentTests.UploadIdAsync(client, listingId);
        var uploadedBytes = await client.GetByteArrayAsync($"/api/documents/{uploadId}");
        await SaveAsync(client, listingId, "First saved draft");
        var first = (await GenerateAsync(client, listingId)).GetProperty("document");
        var firstId = first.GetProperty("id").GetInt32();
        var firstBytes = await client.GetByteArrayAsync($"/api/documents/{firstId}");
        await SaveAsync(client, listingId, "Second saved draft");
        var second = (await GenerateAsync(client, listingId)).GetProperty("document");
        Assert.NotEqual(firstId, second.GetProperty("id").GetInt32());
        var listing = await GetListingAsync(client, listingId);
        var documents = listing.GetProperty("documents").EnumerateArray().ToList();
        Assert.Equal(3, documents.Count);
        Assert.All(documents, d => Assert.Equal(JsonValueKind.Null, d.GetProperty("sentAt").ValueKind));
        Assert.Equal(first.GetProperty("fileName").GetString(), documents.Single(d => d.GetProperty("id").GetInt32() == firstId).GetProperty("fileName").GetString());
        Assert.Equal(firstBytes, await client.GetByteArrayAsync($"/api/documents/{firstId}"));
        Assert.Equal(uploadedBytes, await client.GetByteArrayAsync($"/api/documents/{uploadId}"));
    }

    [Fact]
    public async Task Generate_WithoutDraft_ReturnsTypedErrorWithoutCreatingDocument()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(client, "Missing draft");
        var payload = await GenerateAsync(client, listingId);
        Assert.Equal("TailoredCvDraftNotFoundError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("document").ValueKind);
        Assert.Empty((await GetListingAsync(client, listingId)).GetProperty("documents").EnumerateArray());
    }

    [Fact]
    public async Task Generate_ForeignOrUnknownListing_ReturnsListingNotFoundWithoutCreatingDocument()
    {
        var owner = await factory.GetOwnerClientAsync();
        var listingId = await DocumentTests.CreateListingAsync(owner, "Private draft");
        await SaveAsync(owner, listingId, "Private content");
        var guest = await factory.GetGuestClientAsync();
        foreach (var id in new[] { listingId, int.MaxValue })
        {
            var payload = await GenerateAsync(guest, id);
            Assert.Equal("ListingNotFoundError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
            Assert.Equal(JsonValueKind.Null, payload.GetProperty("document").ValueKind);
        }
        Assert.Empty((await GetListingAsync(owner, listingId)).GetProperty("documents").EnumerateArray());
    }

    private static async Task SaveAsync(HttpClient client, int listingId, string summary)
    {
        var data = await client.GraphQLAsync("""
            mutation ($listingId: Int!, $content: TailoredCvContentInput!) {
              saveTailoredCvDraft(input: {listingId: $listingId, content: $content}) { errors { __typename } }
            }
            """, new { listingId, content = new { summary } });
        Assert.Equal(JsonValueKind.Null, data.GetProperty("saveTailoredCvDraft").GetProperty("errors").ValueKind);
    }

    private static async Task<JsonElement> GenerateAsync(HttpClient client, int listingId) =>
        (await client.GraphQLAsync("""
            mutation ($listingId: Int!) {
              generateCvDocument(input: {listingId: $listingId}) {
                document { id kind fileName originalFileName sizeBytes contentHash sentAt }
                errors { __typename ... on Error { message } }
              }
            }
            """, new { listingId })).GetProperty("generateCvDocument");

    private static async Task<JsonElement> GetListingAsync(HttpClient client, int id) =>
        (await client.GraphQLAsync("""
            query ($id: Int!) { listingById(id: $id) { status documents { id fileName sentAt } } }
            """, new { id })).GetProperty("listingById");
}
