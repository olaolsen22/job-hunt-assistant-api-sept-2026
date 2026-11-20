using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobHunt.Api.Features.Documents;
using JobHunt.Api.Tests.Features.Listings;

namespace JobHunt.Api.Tests.Features.Documents;

public class DocumentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n%fake but good enough\n%%EOF");

    private const string UploadDocument = """
        mutation ($listingId: Int!, $kind: DocumentKind!, $file: Upload!) {
          uploadDocument(input: { listingId: $listingId, kind: $kind, file: $file }) {
            document { id kind fileName originalFileName sizeBytes contentHash sentAt }
            errors { __typename ... on Error { message } }
          }
        }
        """;

    private const string DeleteDocument = """
        mutation ($id: Int!) {
          deleteDocument(input: { id: $id }) { id errors { __typename } }
        }
        """;

    private const string DeleteListing = """
        mutation ($id: Int!) {
          deleteListing(input: { id: $id }) { id errors { __typename } }
        }
        """;

    [Fact]
    public async Task Upload_StoresPdfWithGeneratedName_AndDownloads()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "Nordlys Tech AS");

        var payload = await UploadAsync(client, listingId, "COVER_LETTER", Pdf, "my letter (final).pdf");

        var document = payload.GetProperty("document");
        Assert.Equal("COVER_LETTER", document.GetProperty("kind").GetString());
        Assert.Matches(@"^Nordlys-Tech-AS-[0-9a-f]{32}_Backend-Developer_CoverLetter_\d{4}-\d{2}-\d{2}\.pdf$",
            document.GetProperty("fileName").GetString());
        Assert.Equal("my letter (final).pdf", document.GetProperty("originalFileName").GetString());
        Assert.Equal(Pdf.Length, document.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Pdf)), document.GetProperty("contentHash").GetString());

        var response = await client.GetAsync($"/api/documents/{document.GetProperty("id").GetInt32()}");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/pdf", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal(DocumentEndpoints.DownloadName(null, document.GetProperty("fileName").GetString()!), response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal(Pdf, await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("NORWEGIAN", "Butikkmedarbeidere")]
    [InlineData("ENGLISH", "Retail-Worker")]
    [InlineData(null, "Retail-Worker")]
    public async Task Upload_NamesNonEnglishListingByItsOwnTitle(string? postingLanguage, string role)
    {
        var client = await factory.GetOwnerClientAsync();
        var listing = ListingTests.NewListing(ListingTests.Unique("Gutta"), title: "Butikkmedarbeidere",
            jobRoleName: "Retail Worker", postingLanguage: postingLanguage);
        var listingId = (await ListingTests.CreateAsync(client, listing)).GetProperty("id").GetInt32();

        var document = (await UploadAsync(client, listingId, "CV", Pdf, "cv.pdf")).GetProperty("document");

        Assert.Contains($"_{role}_Cv_", document.GetProperty("fileName").GetString());
    }

    [Theory]
    [InlineData("Jane Doe", "Jane-Doe_Acme_Dev_Cv.pdf")]
    [InlineData(null, "Acme_Dev_Cv.pdf")]
    [InlineData("  ", "Acme_Dev_Cv.pdf")]
    public void DownloadName_DropsDateAndPrefixesProfileName(string? fullName, string expected) =>
        Assert.Equal(expected, DocumentEndpoints.DownloadName(fullName, "Acme_Dev_Cv_2026-10-14.pdf"));

    [Fact]
    public async Task Upload_RejectsNonPdf()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "Acme");

        var payload = await UploadAsync(client, listingId, "CV", Encoding.ASCII.GetBytes("<html>"), "cv.pdf");

        Assert.Equal("InvalidDocumentError", payload.GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Listing_ExposesItsDocuments()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "Acme");
        await UploadAsync(client, listingId, "CV", Pdf, "cv.pdf");

        var data = await client.GraphQLAsync("""
            query ($id: Int!) { listingById(id: $id) { documents { kind originalFileName } } }
            """, new { id = listingId });

        var document = Assert.Single(data.GetProperty("listingById").GetProperty("documents").EnumerateArray());
        Assert.Equal("CV", document.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task DeleteListing_WithUnsentDocument_DeletesDocument()
    {
        var client = await factory.GetOwnerClientAsync();
        var listingId = await CreateListingAsync(client, "Acme");
        var id = await UploadIdAsync(client, listingId);

        var data = await client.GraphQLAsync(DeleteListing, new { id = listingId });

        Assert.Equal(listingId, data.GetProperty("deleteListing").GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/documents/{id}")).StatusCode);
    }

    [Fact]
    public async Task Documents_AreInvisibleToOtherUsers()
    {
        var owner = await factory.GetOwnerClientAsync();
        var guest = await factory.GetGuestClientAsync();
        var listingId = await CreateListingAsync(owner, "Secret AS");
        var id = await UploadIdAsync(owner, listingId);

        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/documents/{id}")).StatusCode);
        var upload = await UploadAsync(guest, listingId, "CV", Pdf, "cv.pdf");
        Assert.Equal("ListingNotFoundError", upload.GetProperty("errors")[0].GetProperty("__typename").GetString());
        var delete = await guest.GraphQLAsync(DeleteDocument, new { id });
        Assert.Equal("DocumentNotFoundError",
            delete.GetProperty("deleteDocument").GetProperty("errors")[0].GetProperty("__typename").GetString());
    }

    [Fact]
    public async Task Download_WhenAnonymous_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("/api/documents/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static async Task<int> CreateListingAsync(HttpClient client, string company) =>
        (await ListingTests.CreateAsync(client, ListingTests.NewListing(ListingTests.Unique(company))))
            .GetProperty("id").GetInt32();

    internal static async Task<int> UploadIdAsync(HttpClient client, int listingId, string kind = "CV") =>
        (await UploadAsync(client, listingId, kind, Pdf, "cv.pdf"))
            .GetProperty("document").GetProperty("id").GetInt32();

    // The GraphQL multipart request spec: the operation as JSON, a map from file part to variable, then the file.
    private static async Task<JsonElement> UploadAsync(
        HttpClient client, int listingId, string kind, byte[] bytes, string fileName)
    {
        using var content = new MultipartFormDataContent
        {
            { JsonContent.Create(new { query = UploadDocument, variables = new { listingId, kind, file = (object?)null } }), "operations" },
            { new StringContent("""{ "0": ["variables.file"] }"""), "map" },
            { new ByteArrayContent(bytes), "0", fileName },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql") { Content = content };
        // Hot Chocolate rejects multipart requests without it; a cross-site form post can't set custom headers.
        request.Headers.Add("GraphQL-Preflight", "1");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (body.TryGetProperty("errors", out var errors))
            Assert.Fail(errors.ToString());
        return body.GetProperty("data").GetProperty("uploadDocument");
    }
}
