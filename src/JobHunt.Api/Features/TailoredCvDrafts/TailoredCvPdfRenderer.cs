using System.Globalization;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.Profiles;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JobHunt.Api.Features.TailoredCvDrafts;

// Pure function, no DB/DI, so the layout can be tested by rendering a TailoredCvContent directly.
// Follows docs/norwegian-cv-formatting.md and NAV's CV headings: one column, left aligned, black on white,
// 11 pt body, labelled contact lines, and title / organization / date lines per entry so extracted text stays clear.
// The draft's SectionOrder picks NAV's standard order or the developer order.
public static class TailoredCvPdfRenderer
{
    private const string Bullet = "•";

    public static byte[] Render(TailoredCvContent content)
    {
        TailoredCvPdfFonts.EnsureRegistered();
        var t = CvText.For(content.Language);

        var name = content.FullName?.Trim();

        return QuestPDF.Fluent.Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(2, Unit.Centimetre);
                page.MarginVertical(1.7f, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontFamily(TailoredCvPdfFonts.FamilyName).FontSize(11)
                    .LineHeight(1.27f).FontColor(Colors.Black));
                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });

                page.Content().Column(column =>
                {
                    column.Spacing(4);
                    AddContact(column, content, t);
                    if (content.SectionOrder == TailoredCvSectionOrder.Developer)
                    {
                        // Profile links read as labelled contact lines, like E-post and Telefon.
                        foreach (var link in content.Links ?? [])
                            AddLabelledLine(column, link.Label, link.Url);
                        AddTextSection(column, t.Summary, content.Summary);
                        AddSkillsSection(column, content.Skills, t);
                        AddExperienceSection(column, content.ContentStyle, content.Experiences, t);
                        AddProjectSection(column, content.ContentStyle, content.Projects, t);
                        AddEducationSection(column, content.ContentStyle, content.Educations, t);
                        AddCertificationSection(column, content.ContentStyle, content.Certifications, t);
                        AddLanguagesSection(column, content.Languages, t);
                    }
                    else
                    {
                        AddTextSection(column, t.Summary, content.Summary);
                        AddExperienceSection(column, content.ContentStyle, content.Experiences, t);
                        AddEducationSection(column, content.ContentStyle, content.Educations, t);
                        AddCertificationSection(column, content.ContentStyle, content.Certifications, t);
                        AddProjectSection(column, content.ContentStyle, content.Projects, t);
                        AddLanguagesSection(column, content.Languages, t);
                        AddSkillsSection(column, content.Skills, t);
                        AddLinksSection(column, content.Links, t);
                    }
                    // References belong last.
                    if (content.ReferencesOnRequest)
                        AddTextSection(column, t.References, t.ReferencesOnRequest);
                });
            });
        })
        // Document properties are read by some parsers/viewers; no subject/keywords, they don't help ATS matching.
        .WithMetadata(new DocumentMetadata
        {
            Title = string.IsNullOrEmpty(name) ? "CV" : $"{name} – CV",
            Author = name,
        })
        .GeneratePdf();
    }

    private static void AddContact(ColumnDescriptor column, TailoredCvContent content, CvText t)
    {
        if (!string.IsNullOrWhiteSpace(content.FullName))
            column.Item().Text(content.FullName).FontSize(22).Bold();
        if (!string.IsNullOrWhiteSpace(content.Headline))
            column.Item().Text(content.Headline).Bold();
        // Small gap between the name/headline block and the contact lines.
        column.Item().Height(4);

        // Plain labelled lines rather than icons or separators, so each value extracts unambiguously.
        AddLabelledLine(column, t.Email, content.Email);
        AddLabelledLine(column, t.Phone, content.Phone);
        AddLabelledLine(column, t.Address, content.Address);
        AddLabelledLine(column, t.DateOfBirth, content.DateOfBirth is { } born ? t.FormatDate(born) : null);
    }

    private static void AddLabelledLine(ColumnDescriptor column, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            column.Item().Text($"{label}: {value}");
    }

    private static void AddEntryHeader(ColumnDescriptor entry, string title, string? organization, string? dates)
    {
        // Dates share the title's baseline at the right edge; a long title wraps in its own cell, never under the dates.
        entry.Item().Row(row =>
        {
            row.RelativeItem().Text(title).Bold();
            if (!string.IsNullOrWhiteSpace(dates))
                row.AutoItem().PaddingLeft(12).Text(dates);
        });
        if (!string.IsNullOrWhiteSpace(organization))
            entry.Item().Text(organization);
        // Small gap between an entry's title lines and its content.
        entry.Item().Height(3);
    }

    // " | " between organization and location: locations hold commas themselves ("Lagos, Nigeria").
    private static string JoinPlace(params string?[] parts) => Join(" | ", parts);

    private static string Join(string separator, string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    // Heading (~35 pt) plus two body lines (~28 pt): less room than this left on the page starts the section on the next one.
    private const float MinTextSectionStart = 64;

    private static void AddTextSection(ColumnDescriptor column, string heading, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        column.Item().EnsureSpace(MinTextSectionStart).Column(section =>
        {
            AddHeading(section, heading);
            section.Item().Text(text);
        });
    }

    // Each entry is kept whole, and the heading shares the first entry's block, so a section heading
    // never ends a page on its own and an entry never splits across pages.
    private static void AddEntrySection<T>(ColumnDescriptor column, string heading, List<T>? items,
        Action<ColumnDescriptor, T> addEntry, float entrySpacing = 6)
    {
        if (items is not { Count: > 0 })
            return;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var isFirst = i == 0;
            column.Item().PreventPageBreak().PaddingBottom(entrySpacing).Column(entry =>
            {
                if (isFirst)
                    AddHeading(entry, heading);
                addEntry(entry, item);
            });
        }
    }

    private static void AddExperienceSection(ColumnDescriptor column, TailoredCvContentStyle style, List<TailoredCvExperience>? experiences, CvText t) =>
        AddEntrySection(column, t.Experience, experiences, (entry, experience) =>
        {
            AddEntryHeader(entry, experience.Title, JoinPlace(experience.Employer, experience.Location),
                t.FormatRange(experience.StartMonth, experience.EndMonth));
            AddContent(entry, style, experience.Paragraph, experience.Bullets);
        });

    private static void AddEducationSection(ColumnDescriptor column, TailoredCvContentStyle style, List<TailoredCvEducation>? educations, CvText t) =>
        AddEntrySection(column, t.Education, educations, (entry, education) =>
        {
            var degreeField = Join(", ", [education.Degree, education.Field]);
            var dates = t.FormatRange(education.StartMonth, education.EndMonth);
            if (string.IsNullOrWhiteSpace(degreeField))
                AddEntryHeader(entry, education.School, education.Location, dates);
            else
                AddEntryHeader(entry, degreeField, JoinPlace(education.School, education.Location), dates);
            AddContent(entry, style, education.Paragraph, education.Bullets);
        });

    private static void AddCertificationSection(ColumnDescriptor column, TailoredCvContentStyle style, List<TailoredCvCertification>? certifications, CvText t) =>
        AddEntrySection(column, t.Certifications, certifications, (entry, certification) =>
        {
            var dates = (certification.IssuedMonth, certification.ExpiresMonth) switch
            {
                ({ } issued, { } expires) => $"{t.FormatMonth(issued)} - {t.FormatMonth(expires)}",
                ({ } issued, null) => t.FormatMonth(issued),
                (null, { } expiry) => $"{t.Expires} {t.FormatMonth(expiry)}",
                _ => null,
            };
            AddEntryHeader(entry, certification.Name, certification.Issuer, dates);
            if (!string.IsNullOrWhiteSpace(certification.Url))
                entry.Item().Text(certification.Url);
            AddContent(entry, style, certification.Paragraph, certification.Bullets);
        });

    private static void AddProjectSection(ColumnDescriptor column, TailoredCvContentStyle style, List<TailoredCvProject>? projects, CvText t) =>
        AddEntrySection(column, t.Projects, projects, (entry, project) =>
        {
            AddEntryHeader(entry, project.Name, project.Location, t.FormatRange(project.StartMonth, project.EndMonth));
            if (!string.IsNullOrWhiteSpace(project.Url))
                entry.Item().Text(project.Url);
            AddContent(entry, style, project.Paragraph, project.Bullets);
        });

    // NAV's layout: the language name, then written and spoken levels on their own lines.
    private static void AddLanguagesSection(ColumnDescriptor column, List<TailoredCvLanguage>? languages, CvText t) =>
        AddEntrySection(column, t.Languages, languages, (entry, language) =>
        {
            entry.Item().Text(t.LanguageName(language.Name)).Bold();
            if (language.Written is { } written)
                entry.Item().Text($"{t.Written}: {t.Proficiency(written)}");
            if (language.Spoken is { } spoken)
                entry.Item().Text($"{t.Spoken}: {t.Proficiency(spoken)}");
        });

    // Plain, comma-separated names: no bars/dots for level, which an ATS can't read anyway.
    private static void AddSkillsSection(ColumnDescriptor column, List<TailoredCvSkill>? skills, CvText t)
    {
        if (skills is { Count: > 0 })
            AddTextSection(column, t.Skills, string.Join(", ", skills.Select(s => s.Name)));
    }

    // Link lines sit at the column's normal spacing rather than entry spacing.
    private static void AddLinksSection(ColumnDescriptor column, List<TailoredCvLink>? links, CvText t) =>
        AddEntrySection(column, t.Links, links, (entry, link) => entry.Item().Text($"{link.Label}: {link.Url}"),
            entrySpacing: 0);

    // A paragraph falls back to the joined bullet texts when none was written.
    private static void AddContent(ColumnDescriptor entry, TailoredCvContentStyle style, string? paragraph,
        List<TailoredCvBullet> bullets)
    {
        if (style == TailoredCvContentStyle.Paragraph)
        {
            var text = paragraph ?? string.Join(" ", bullets.Select(b => b.Text));
            if (!string.IsNullOrWhiteSpace(text))
                entry.Item().Text(text);
        }
        else
            AddBullets(entry, bullets);
    }

    private static void AddBullets(ColumnDescriptor entry, List<TailoredCvBullet> bullets)
    {
        foreach (var bullet in bullets)
            entry.Item().PaddingBottom(3).Row(row =>
            {
                row.ConstantItem(12).Text(Bullet);
                row.RelativeItem().Text(bullet.Text);
            });
    }

    private static void AddHeading(ColumnDescriptor column, string text) =>
        // The rule is a border, not text: nothing is added to the PDF text layer for an ATS to trip over.
        column.Item().PaddingTop(12).PaddingBottom(4).BorderBottom(0.75f).BorderColor(Colors.Grey.Darken1)
            .PaddingBottom(2).Text(text).FontSize(13).Bold();
}

// Fixed CV wording per language. Norwegian follows NAV: capitalised months ("Juni 2015") and "Nåværende".
internal sealed record CvText(
    string Summary, string Experience, string Education, string Certifications, string Projects,
    string Languages, string Skills, string Links, string References, string ReferencesOnRequest,
    string Email, string Phone, string Address, string DateOfBirth,
    string Present, string Expires, string Written, string Spoken,
    string[] Months, string[] Levels, bool NumericDates, Dictionary<string, string> LanguageNames)
{
    private static readonly CvText English = new(
        "Summary", "Experience", "Education", "Courses and certifications", "Projects",
        "Languages", "Skills", "Links", "References", "Available on request.",
        "Email", "Phone", "Address", "Date of birth",
        "Present", "Expires", "Written", "Spoken",
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
        ["Beginner", "Good", "Very good", "Native (first language)"],
        NumericDates: false,
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Norsk"] = "Norwegian", ["Engelsk"] = "English", ["Svensk"] = "Swedish", ["Dansk"] = "Danish",
            ["Tysk"] = "German", ["Fransk"] = "French", ["Spansk"] = "Spanish",
        });

    private static readonly CvText Norwegian = new(
        "Sammendrag", "Arbeidsforhold", "Utdanning", "Kurs og sertifiseringer", "Prosjekter",
        "Språk", "Kompetanser", "Lenker", "Referanser", "Oppgis ved forespørsel.",
        "E-post", "Telefon", "Adresse", "Fødselsdato",
        "Nåværende", "Utløper", "Skriftlig", "Muntlig",
        ["Januar", "Februar", "Mars", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Desember"],
        ["Nybegynner", "Godt", "Veldig godt", "Førstespråk (morsmål)"],
        NumericDates: true,
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Norwegian"] = "Norsk", ["English"] = "Engelsk", ["Swedish"] = "Svensk", ["Danish"] = "Dansk",
            ["German"] = "Tysk", ["French"] = "Fransk", ["Spanish"] = "Spansk",
        });

    public static CvText For(Language language) => language == Language.Norwegian ? Norwegian : English;

    public string FormatMonth(DateOnly date) => $"{Months[date.Month - 1]} {date.Year}";

    public string FormatDate(DateOnly date) => NumericDates
        ? date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
        : $"{date.Day} {Months[date.Month - 1]} {date.Year}";

    public string FormatRange(DateOnly? start, DateOnly? end)
    {
        if (start is null && end is null)
            return "";
        var startText = start is { } s ? FormatMonth(s) : "";
        var endText = end is { } e ? FormatMonth(e) : Present;
        return $"{startText} - {endText}";
    }

    public string Proficiency(LanguageProficiency level) => Levels[(int)level];

    // Common names stored in the other language read naturally; anything else prints as entered.
    public string LanguageName(string name) => LanguageNames.GetValueOrDefault(name.Trim(), name);
}
