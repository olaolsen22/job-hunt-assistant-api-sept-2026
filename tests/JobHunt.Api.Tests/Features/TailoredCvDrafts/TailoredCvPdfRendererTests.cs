using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.TailoredCvDrafts;
using UglyToad.PdfPig;

namespace JobHunt.Api.Tests.Features.TailoredCvDrafts;

public class TailoredCvPdfRendererTests
{
    [Fact]
    public void Render_FullDraft_KeepsSectionAndEntryOrder()
    {
        var content = new TailoredCvContent(
            Summary: "Søknadsbehandler with efficient office skills.",
            Experiences:
            [
                new TailoredCvExperience("Acme AS", "Senior Dev",
                    new DateOnly(2021, 3, 1), null,
                    [new TailoredCvBullet("Led the migration"), new TailoredCvBullet("Mentored juniors")]),
                new TailoredCvExperience("Old Co", "Dev",
                    new DateOnly(2018, 1, 1), new DateOnly(2021, 2, 1),
                    [new TailoredCvBullet("Shipped features")]),
            ],
            Educations: [new TailoredCvEducation("NTNU", "MSc", "CS",
                new DateOnly(2014, 8, 1), new DateOnly(2018, 6, 1), [new TailoredCvBullet("Completed thesis")])],
            Certifications: [new TailoredCvCertification("AWS SA", "Amazon",
                new DateOnly(2022, 5, 1), new DateOnly(2025, 5, 1), "https://cert.example/1", [new TailoredCvBullet("Passed assessment")])],
            Projects: [new TailoredCvProject("Side Project", "https://side.example",
                new DateOnly(2020, 1, 1), new DateOnly(2020, 6, 1), [new TailoredCvBullet("Built prototype")])],
            Skills: [new TailoredCvSkill("C#", null), new TailoredCvSkill("GraphQL", null)],
            Languages: [new TailoredCvLanguage(Language.Norwegian, CefrLevel.C2)],
            FullName: "Ola Nordmann",
            Headline: "Backend Developer",
            Email: "ola@example.com",
            Phone: "+47 123 45 678",
            Location: "Oslo",
            Links: [new TailoredCvLink("GitHub", "https://github.com/ola")]);

        var text = ExtractText(content);

        // Section order.
        var indices = new[] { "SUMMARY", "EXPERIENCE", "EDUCATION", "CERTIFICATIONS", "PROJECTS", "SKILLS", "LANGUAGES", "LINKS" }
            .Select(heading => text.IndexOf(heading)).ToArray();
        Assert.True(indices.All(i => i >= 0), "every heading should be present");
        Assert.True(indices.SequenceEqual(indices.OrderBy(i => i)), "headings should appear in order");

        // Entries and bullets keep draft order.
        Assert.True(text.IndexOf("Senior Dev") < text.IndexOf("Dev, Old Co") || text.IndexOf("Senior Dev") < text.IndexOf("Old Co"));
        Assert.True(text.IndexOf("Led the migration") < text.IndexOf("Mentored juniors"));
        Assert.True(text.IndexOf("Mentored juniors") < text.IndexOf("Shipped features"));

        // Field presence.
        Assert.Contains("Ola Nordmann", text);
        Assert.Contains("Backend Developer", text);
        Assert.Contains("Oslo", text);
        Assert.Contains("NTNU", text);
        Assert.Contains("AWS SA", text);
        Assert.Contains("Side Project", text);
        Assert.Contains("C#", text);
        Assert.Contains("GitHub", text);
        Assert.Contains("https://github.com/ola", text);
        foreach (var value in new[] { "Søknadsbehandler with efficient office skills.", "ola@example.com",
                     "+47 123 45 678", "Acme AS", "Old Co", "03/2021 – Present", "01/2018 – 02/2021",
                     "MSc", "CS", "08/2014 – 06/2018", "Completed thesis", "Amazon",
                     "05/2022 – 05/2025", "https://cert.example/1", "Passed assessment",
                     "https://side.example", "01/2020 – 06/2020", "Built prototype", "GraphQL", "Norwegian (C2)" })
            Assert.Contains(value, text);
    }

    [Fact]
    public void Render_EmptySections_OmitsTheirHeadings()
    {
        var content = new TailoredCvContent(FullName: "Minimal Person",
            Experiences: [new TailoredCvExperience("Acme", "Dev", new DateOnly(2021, 1, 1), null, [])]);

        var text = ExtractText(content);

        Assert.DoesNotContain("PROJECTS", text);
        Assert.DoesNotContain("CERTIFICATIONS", text);
        Assert.DoesNotContain("EDUCATION", text);
        Assert.DoesNotContain("SKILLS", text);
        Assert.DoesNotContain("LANGUAGES", text);
        Assert.DoesNotContain("LINKS", text);
    }

    [Fact]
    public void Render_NonAsciiAndLigatureText_ExtractsExactly()
    {
        var content = new TailoredCvContent(
            FullName: "Søknadsbehandler Tester",
            Summary: "Delivered efficient office tooling.");

        var text = ExtractText(content);

        Assert.Contains("Søknadsbehandler", text);
        Assert.Contains("efficient office", text);
    }

    [Fact]
    public void Render_ContactDetails_AppearBeforeFirstSectionHeading()
    {
        var content = new TailoredCvContent(
            FullName: "Ola Nordmann",
            Email: "ola@example.com",
            Phone: "+47 123 45 678",
            Location: "Oslo",
            Summary: "A short summary.");

        var text = ExtractText(content);
        var firstHeadingIndex = text.IndexOf("SUMMARY");

        Assert.True(firstHeadingIndex >= 0);
        Assert.True(text.IndexOf("Ola Nordmann") < firstHeadingIndex);
        Assert.True(text.IndexOf("ola@example.com") < firstHeadingIndex);
        Assert.True(text.IndexOf("+47 123 45 678") < firstHeadingIndex);
        Assert.True(text.IndexOf("Oslo") < firstHeadingIndex);
    }

    [Fact]
    public void Render_OpenEndedExperience_FormatsDateRangeWithPresent()
    {
        var content = new TailoredCvContent(
            FullName: "Ola Nordmann",
            Experiences: [new TailoredCvExperience("Acme", "Dev", new DateOnly(2021, 3, 1), null, [])]);

        var text = ExtractText(content);

        Assert.Contains("03/2021 – Present", text);
    }

    [Fact]
    public void Render_CertificationWithOnlyExpiry_IncludesDate()
    {
        var content = new TailoredCvContent(Certifications:
            [new TailoredCvCertification("Certificate", null, null, new DateOnly(2027, 4, 1), null, [])]);

        Assert.Contains("Expires 04/2027", ExtractText(content));
    }

    private static string ExtractText(TailoredCvContent content)
    {
        var bytes = TailoredCvPdfRenderer.Render(content);
        using var document = PdfDocument.Open(bytes);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }
}
