using QuestPDF.Drawing;

namespace JobHunt.Api.Features.TailoredCvDrafts;

// Bundled font: same look everywhere, with æøå and a correct Unicode map for ATS text extraction.
public static class TailoredCvPdfFonts
{
    public const string FamilyName = "Source Sans 3";
    private static readonly Lazy<bool> Registration = new(() =>
    {
        Register();
        return true;
    });

    public static void EnsureRegistered() => _ = Registration.Value;

    private static void Register()
    {
        // Free for an individual/personal project. https://questpdf.com/license/
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;
        var assembly = typeof(TailoredCvPdfFonts).Assembly;
        FontManager.RegisterFontFromEmbeddedResource(assembly,
            "JobHunt.Api.Features.TailoredCvDrafts.Fonts.SourceSans3-Regular.ttf");
        FontManager.RegisterFontFromEmbeddedResource(assembly,
            "JobHunt.Api.Features.TailoredCvDrafts.Fonts.SourceSans3-Bold.ttf");
    }
}
