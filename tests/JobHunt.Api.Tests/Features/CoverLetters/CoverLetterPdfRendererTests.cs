using JobHunt.Api.Features.CoverLetters;
using JobHunt.Api.Features.Listings;
using UglyToad.PdfPig;

namespace JobHunt.Api.Tests.Features.CoverLetters;

public class CoverLetterPdfRendererTests
{
    private static readonly string Paragraph = string.Join(" ", Enumerable.Repeat("Jeg har utviklet ordre-API-er i .NET.", 8));

    private static CoverLetterContent Letter(Language language) => new(language,
        FullName: "Alex Berg", Address: "Storgata 1, 0155 Oslo", Phone: "+47 00000000", Email: "alex@example.com",
        Date: new DateOnly(2026, 10, 15), Organization: "Eksempel AS", RecipientName: "Kari Nordmann",
        RecipientAddress: "Postboks 1\n0101 Oslo", Subject: "Søknad på stilling som programvareutvikler",
        Paragraphs: [Paragraph, Paragraph, Paragraph, Paragraph], Closing: "Vennlig hilsen");

    [Fact]
    public void Render_NorwegianLetter_FollowsTheTraditionalLayoutOnOnePage()
    {
        using var pdf = PdfDocument.Open(CoverLetterPdfRenderer.Render(Letter(Language.Norwegian)));

        Assert.Equal(1, pdf.NumberOfPages);
        var text = pdf.GetPage(1).Text;
        // A one-line address prints as street line then postcode line; the date follows the contact lines.
        var lines = pdf.GetPage(1).GetWords().GroupBy(w => Math.Round(w.BoundingBox.Bottom))
            .OrderByDescending(g => g.Key).Select(g => string.Join(" ", g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text))).ToList();
        Assert.Equal(["Alex Berg", "Storgata 1", "0155 Oslo", "Telefon: +47 00000000", "E-post: alex@example.com",
            "15. oktober 2026"], lines.Take(6));
        AssertInOrder(text, "15. oktober 2026", "Eksempel AS", "Kari Nordmann", "Postboks 1", "Søknad på stilling som programvareutvikler",
            "Jeg har utviklet", "Vennlig hilsen");
        Assert.Equal("Alex Berg", pdf.Information.Author);
    }

    [Fact]
    public void Render_EnglishLetter_UsesEnglishLabelsAndDate()
    {
        using var pdf = PdfDocument.Open(CoverLetterPdfRenderer.Render(Letter(Language.English) with { Greeting = "Dear Kari," }));
        var text = pdf.GetPage(1).Text;

        Assert.Contains("Phone: +47 00000000", text);
        AssertInOrder(text, "Email: alex@example.com", "15 October 2026");
        AssertInOrder(text, "Dear Kari,", "Jeg har utviklet");
    }

    private static void AssertInOrder(string text, params string[] parts)
    {
        var index = 0;
        foreach (var part in parts)
        {
            var found = text.IndexOf(part, index, StringComparison.Ordinal);
            Assert.True(found >= 0, $"'{part}' missing or out of order.");
            index = found + part.Length;
        }
    }
}
