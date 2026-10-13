using JobHunt.Api.Features.TailoredCvDrafts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace JobHunt.Api.Tests.Features.TailoredCvDrafts;

public class TailoredCvPdfFontsTests
{
    [Fact]
    public void EnsureRegistered_LetsQuestPdfRenderWithTheBundledFont()
    {
        TailoredCvPdfFonts.EnsureRegistered();

        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(style => style.FontFamily(TailoredCvPdfFonts.FamilyName));
                page.Content().Text("Søknadsbehandler").Bold();
            });
        }).GeneratePdf();

        Assert.True(bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8));
    }
}
