using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using JobHunt.Api.Features.Skills;
using JobHunt.Api.Features.TailoredCvDrafts;
using UglyToad.PdfPig;

namespace JobHunt.Api.Tests.Features.TailoredCvDrafts;

public class TailoredCvPdfRendererTests
{
    private static TailoredCvContent FullDraft(Language language) => new(
        Summary: "Søknadsbehandler with efficient office skills.",
        Experiences:
        [
            new TailoredCvExperience("Acme AS", "Senior Dev",
                new DateOnly(2021, 3, 1), null,
                [new TailoredCvBullet("Led the migration"), new TailoredCvBullet("Mentored juniors")],
                Location: "Lagos, Nigeria"),
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
        Languages:
        [
            new TailoredCvLanguage("English", LanguageProficiency.Native, LanguageProficiency.Native),
            new TailoredCvLanguage("Filipino/Tagalog", LanguageProficiency.VeryGood, LanguageProficiency.Good),
        ],
        FullName: "Ola Nordmann",
        Headline: "Backend Developer",
        Email: "ola@example.com",
        Phone: "+47 123 45 678",
        Address: "Storgata 1, 0155 Oslo",
        Links: [new TailoredCvLink("GitHub", "https://github.com/ola")],
        Language: language,
        DateOfBirth: new DateOnly(1990, 6, 15),
        ReferencesOnRequest: true);

    [Fact]
    public void Render_NorwegianDraft_UsesNavHeadingsLabelsAndDates()
    {
        var text = ExtractText(FullDraft(Language.Norwegian));

        // NAV headings, experience before education, references last.
        AssertInOrder(text, "Sammendrag", "Arbeidsforhold", "Utdanning", "Kurs og sertifiseringer", "Prosjekter",
            "Språk", "Kompetanser", "Lenker", "Referanser");
        AssertInOrder(text, "E-post: ola@example.com", "Telefon: +47 123 45 678",
            "Adresse: Storgata 1, 0155 Oslo", "Fødselsdato: 15.06.1990", "Sammendrag");
        foreach (var value in new[] { "Mars 2021 - Nåværende", "Januar 2018 - Februar 2021", "August 2014 - Juni 2018",
                     "Mai 2022 - Mai 2025", "Januar 2020 - Juni 2020", "Acme AS | Lagos, Nigeria", "MSc, CS",
                     "Oppgis ved forespørsel." })
            Assert.Contains(value, text);

        // NAV's language block: name, then written and spoken on their own lines.
        AssertInOrder(text, "Engelsk", "Skriftlig: Førstespråk (morsmål)", "Muntlig: Førstespråk (morsmål)",
            "Filipino/Tagalog", "Skriftlig: Veldig godt", "Muntlig: Godt");
        Assert.DoesNotContain("–", text);
    }

    [Fact]
    public void Render_EnglishDraft_UsesEnglishWording()
    {
        var text = ExtractText(FullDraft(Language.English));

        AssertInOrder(text, "Summary", "Experience", "Education", "Courses and certifications", "Projects",
            "Languages", "Skills", "Links", "References");
        AssertInOrder(text, "Email: ola@example.com", "Phone: +47 123 45 678",
            "Address: Storgata 1, 0155 Oslo", "Date of birth: 15 June 1990");
        foreach (var value in new[] { "March 2021 - Present", "January 2018 - February 2021",
                     "Written: Native (first language)", "Spoken: Good", "Available on request." })
            Assert.Contains(value, text);
        Assert.Contains("•", text);
        Assert.Contains("Led the migration", text);
        // Entries and bullets keep draft order.
        AssertInOrder(text, "Senior Dev", "Led the migration", "Mentored juniors", "Old Co", "Shipped features");
    }

    [Fact]
    public void Render_DeveloperOrder_PutsLinksWithContactAndSkillsBeforeExperience()
    {
        var text = ExtractText(FullDraft(Language.Norwegian) with { SectionOrder = TailoredCvSectionOrder.Developer });

        AssertInOrder(text, "E-post: ola@example.com", "GitHub: https://github.com/ola", "Sammendrag", "Kompetanser",
            "Arbeidsforhold", "Prosjekter", "Utdanning", "Kurs og sertifiseringer", "Språk", "Referanser");
        Assert.DoesNotContain("Lenker", text);
    }

    private static readonly List<TailoredCvSkill> CategorizedSkills =
    [
        new("Docker", 3, Category: SkillCategory.CloudDevops),
        new("TypeScript", 4, Category: SkillCategory.Language),
        new("Kundeservice", 5),
        new("C#", 5, Category: SkillCategory.Language),
        new("PostgreSQL", 3, Category: SkillCategory.Database),
    ];

    [Theory]
    [InlineData(Language.Norwegian, "Programmeringsspråk: TypeScript, C#", "Databaser: PostgreSQL",
        "Sky og DevOps: Docker", "Annet: Kundeservice")]
    [InlineData(Language.English, "Languages: TypeScript, C#", "Databases: PostgreSQL",
        "Cloud & DevOps: Docker", "Other: Kundeservice")]
    public void Render_DeveloperOrder_GroupsSkillsByCategoryInDraftOrder(Language language, params string[] lines)
    {
        var text = ExtractText(FullDraft(language) with
        {
            SectionOrder = TailoredCvSectionOrder.Developer, Skills = CategorizedSkills,
        });

        // Categories in enum order, uncategorized last; skills keep the draft's relevance order.
        AssertInOrder(text, lines);
        Assert.DoesNotContain(language == Language.Norwegian ? "Rammeverk" : "Frameworks", text);
    }

    [Fact]
    public void Render_StandardOrder_IgnoresCategories()
    {
        var text = ExtractText(FullDraft(Language.Norwegian) with { Skills = CategorizedSkills });

        Assert.Contains("Docker, TypeScript, Kundeservice, C#, PostgreSQL", text);
        Assert.DoesNotContain("Programmeringsspråk", text);
    }

    [Fact]
    public void Render_EmptySections_OmitsTheirHeadings()
    {
        var content = new TailoredCvContent(FullName: "Minimal Person", Language: Language.Norwegian,
            Experiences: [new TailoredCvExperience("Acme", "Dev", new DateOnly(2021, 1, 1), null, [])]);

        var text = ExtractText(content);

        Assert.Contains("Arbeidsforhold", text);
        foreach (var heading in new[] { "Prosjekter", "Kurs og sertifiseringer", "Utdanning", "Kompetanser",
                     "Språk", "Lenker", "Referanser", "Fødselsdato" })
            Assert.DoesNotContain(heading, text);
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
    public void Render_CertificationWithOnlyExpiry_IncludesDate()
    {
        var content = new TailoredCvContent(Language: Language.Norwegian, Certifications:
            [new TailoredCvCertification("Certificate", null, null, new DateOnly(2027, 4, 1), null, [])]);

        Assert.Contains("Utløper April 2027", ExtractText(content));
    }

    [Fact]
    public void Render_ParagraphExperience_CombinesSelectedPointsWithoutBulletGlyphs()
    {
        var content = new TailoredCvContent(Experiences:
        [
            new TailoredCvExperience("Acme", "Developer", null, null,
                [new TailoredCvBullet("Built the service."), new TailoredCvBullet("Cut latency by 30%.")],
                Paragraph: "Built the service and cut latency by 30%."),
        ], ContentStyle: TailoredCvContentStyle.Paragraph);

        var text = ExtractText(content);

        Assert.Contains("Built the service and cut latency by 30%.", text);
        Assert.DoesNotContain("•", text);
    }

    [Fact]
    public void Render_ParagraphInOtherSections_RendersProseWithoutBulletGlyphs()
    {
        var content = new TailoredCvContent(
            Educations: [new TailoredCvEducation("State University", "BSc", null, null, null,
                [new TailoredCvBullet("Thesis on caching.")],
                Paragraph: "Wrote a thesis on caching.")],
            Certifications: [new TailoredCvCertification("AWS SA", null, null, null, null,
                [new TailoredCvBullet("Passed first try."), new TailoredCvBullet("Scored 900.")])],
            Projects: [new TailoredCvProject("Side Project", null, null, null,
                [new TailoredCvBullet("Shipped a CLI.")],
                Paragraph: "Shipped a small CLI tool.")],
            ContentStyle: TailoredCvContentStyle.Paragraph);

        var text = ExtractText(content);

        Assert.Contains("Wrote a thesis on caching.", text);
        Assert.Contains("Passed first try. Scored 900.", text);
        Assert.Contains("Shipped a small CLI tool.", text);
        Assert.DoesNotContain("•", text);
    }

    [Fact]
    public void Render_SectionsAtPageBoundary_KeepHeadingWithFirstEntry()
    {
        // Growing the summary one line at a time slides every later section across the page break.
        (string Heading, string FirstEntry)[] sections =
        [
            ("Experience", "Senior Dev"), ("Education", "MSc, CS"), ("Courses and certifications", "AWS SA"),
            ("Projects", "Side Project"), ("Languages", "Spoken: Native"), ("Skills", "C#"),
            ("Links", "GitHub"), ("References", "Available on request."),
        ];
        for (var lines = 1; lines <= 60; lines++)
        {
            var content = FullDraft(Language.English) with
            {
                Summary = string.Join("\n", Enumerable.Repeat("Summary line", lines)),
            };
            using var document = PdfDocument.Open(TailoredCvPdfRenderer.Render(content));
            foreach (var page in document.GetPages())
            foreach (var (heading, firstEntry) in sections.Where(s => page.Text.Contains(s.Heading, StringComparison.Ordinal)))
                Assert.True(page.Text.Contains(firstEntry, StringComparison.Ordinal),
                    $"\"{heading}\" split from \"{firstEntry}\" with {lines} summary lines");
        }
    }

    [Fact]
    public void Render_SetsTitleAndAuthorFromName_WithoutSubjectOrKeywords()
    {
        using var document = PdfDocument.Open(TailoredCvPdfRenderer.Render(FullDraft(Language.English)));

        Assert.Equal("Ola Nordmann – CV", document.Information.Title);
        Assert.Equal("Ola Nordmann", document.Information.Author);
        Assert.True(string.IsNullOrEmpty(document.Information.Subject));
        Assert.True(string.IsNullOrEmpty(document.Information.Keywords));
    }

    [Fact]
    public void Render_WithoutName_UsesPlainTitleAndNoAuthor()
    {
        using var document = PdfDocument.Open(TailoredCvPdfRenderer.Render(FullDraft(Language.English) with { FullName = null }));

        Assert.Equal("CV", document.Information.Title);
        Assert.True(string.IsNullOrEmpty(document.Information.Author));
    }

    [Fact]
    public void Render_PutsDatesOnTheTitleLine_AtTheRightEdge()
    {
        using var document = PdfDocument.Open(TailoredCvPdfRenderer.Render(FullDraft(Language.English)));
        var words = document.GetPages().First().GetWords().ToList();
        var title = words.First(w => w.Text == "Senior");
        var dateStart = words.First(w => w.Text == "March");

        Assert.Equal(title.BoundingBox.Bottom, dateStart.BoundingBox.Bottom, 1);
        Assert.True(dateStart.BoundingBox.Left > title.BoundingBox.Right);
    }

    private static void AssertInOrder(string text, params string[] values)
    {
        var indices = values.Select(v => text.IndexOf(v, StringComparison.Ordinal)).ToArray();
        for (var i = 0; i < values.Length; i++)
            Assert.True(indices[i] >= 0, $"missing \"{values[i]}\"");
        Assert.True(indices.SequenceEqual(indices.OrderBy(i => i)), $"out of order: {string.Join(", ", values)}");
    }

    private static string ExtractText(TailoredCvContent content)
    {
        var bytes = TailoredCvPdfRenderer.Render(content);
        using var document = PdfDocument.Open(bytes);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }
}
