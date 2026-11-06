using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.TailoredCvDrafts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JobHunt.Api.Features.CoverLetters;

// Pure function, like the CV renderer. Layout: docs/norwegian-cover-letter.md §3.
// Source Sans 3 because it embeds with a correct Unicode map.
public static class CoverLetterPdfRenderer
{
    private const float BlockGap = 18;

    public static byte[] Render(CoverLetterContent content)
    {
        TailoredCvPdfFonts.EnsureRegistered();
        var norwegian = content.Language == Language.Norwegian;
        var name = content.FullName;

        return QuestPDF.Fluent.Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2.54f, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontFamily(TailoredCvPdfFonts.FamilyName).FontSize(11)
                    .LineHeight(1.27f).FontColor(Colors.Black));

                page.Content().Column(column =>
                {
                    column.Item().Column(sender =>
                    {
                        AddLine(sender, name);
                        foreach (var line in CoverLetterValidation.AddressLines(content.Address)) AddLine(sender, line);
                        AddLine(sender, content.Phone is { } phone ? $"{(norwegian ? "Telefon" : "Phone")}: {phone}" : null);
                        AddLine(sender, content.Email is { } email ? $"{(norwegian ? "E-post" : "Email")}: {email}" : null);
                        AddLine(sender, DateLine(content));
                    });

                    var recipient = new[] { content.Organization, content.RecipientName }
                        .Concat(CoverLetterValidation.AddressLines(content.RecipientAddress)).OfType<string>().ToList();
                    if (recipient.Count > 0)
                        column.Item().PaddingTop(BlockGap).Column(block =>
                        {
                            foreach (var line in recipient) AddLine(block, line);
                        });

                    if (content.Subject is { } subject)
                        column.Item().PaddingTop(BlockGap).Text(subject).FontSize(12).Bold();

                    column.Item().PaddingTop(10).Column(body =>
                    {
                        body.Spacing(10);
                        if (content.Greeting is { } greeting) body.Item().Text(greeting);
                        foreach (var paragraph in content.Paragraphs) body.Item().Text(paragraph);
                        // Sign-off and typed name together; Tekna says a typed name is enough for PDF/upload.
                        body.Item().Column(signOff =>
                        {
                            AddLine(signOff, content.Closing);
                            AddLine(signOff, name);
                        });
                    });
                });
            });
        })
        .WithMetadata(new DocumentMetadata
        {
            Title = norwegian ? "Søknad" : "Cover letter",
            Author = name,
        })
        .GeneratePdf();
    }

    // "15. oktober 2026" / "15 October 2026".
    internal static string? DateLine(CoverLetterContent content) => content.Date is not { } date ? null
        : content.Language == Language.Norwegian
            ? $"{date.Day}. {NorwegianMonths[date.Month - 1]} {date.Year}"
            : $"{date.Day} {EnglishMonths[date.Month - 1]} {date.Year}";

    private static readonly string[] NorwegianMonths =
        ["januar", "februar", "mars", "april", "mai", "juni", "juli", "august", "september", "oktober", "november", "desember"];
    private static readonly string[] EnglishMonths =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private static void AddLine(ColumnDescriptor column, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text)) column.Item().Text(text);
    }
}
