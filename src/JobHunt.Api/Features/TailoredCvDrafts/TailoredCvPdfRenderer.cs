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
public static class TailoredCvPdfRenderer
{
    private const string Bullet = "•";

    public static byte[] Render(TailoredCvContent content)
    {
        TailoredCvPdfFonts.EnsureRegistered();
        var t = CvText.For(content.Language);

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
                    AddTextSection(column, t.Summary, content.Summary);
                    AddExperienceSection(column, content.Experiences, t);
                    AddEducationSection(column, content.Educations, t);
                    AddCertificationSection(column, content.Certifications, t);
                    AddProjectSection(column, content.Projects, t);
                    AddLanguagesSection(column, content.Languages, t);
                    AddSkillsSection(column, content.Skills, t);
                    AddLinksSection(column, content.Links, t);
                    // References belong last.
                    if (content.ReferencesOnRequest)
                        AddTextSection(column, t.References, t.ReferencesOnRequest);
                });
            });
        }).GeneratePdf();
    }

    private static void AddContact(ColumnDescriptor column, TailoredCvContent content, CvText t)
    {
        if (!string.IsNullOrWhiteSpace(content.FullName))
            column.Item().Text(content.FullName).FontSize(22).Bold();
        if (!string.IsNullOrWhiteSpace(content.Headline))
            column.Item().Text(content.Headline).Bold();

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
        entry.Item().Text(title).Bold();
        if (!string.IsNullOrWhiteSpace(organization))
            entry.Item().Text(organization);
        if (!string.IsNullOrWhiteSpace(dates))
            entry.Item().Text(dates);
    }

    private static string JoinPlace(params string?[] parts) =>
        string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static void AddTextSection(ColumnDescriptor column, string heading, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        AddHeading(column, heading);
        column.Item().Text(text);
    }

    private static void AddExperienceSection(ColumnDescriptor column, List<TailoredCvExperience>? experiences, CvText t)
    {
        if (experiences is not { Count: > 0 })
            return;
        // Keep the section label and enough room for the first role header together.
        column.Item().EnsureSpace(72).PaddingTop(12).PaddingBottom(6).Text(t.Experience).FontSize(13).Bold();
        foreach (var experience in experiences)
        {
            column.Item().PreventPageBreak().PaddingBottom(6).Column(entry =>
            {
                AddEntryHeader(entry, experience.Title, JoinPlace(experience.Employer, experience.Location),
                    t.FormatRange(experience.StartMonth, experience.EndMonth));
                if (experience.ContentStyle == TailoredCvExperienceContentStyle.Paragraph)
                    entry.Item().Text(experience.Paragraph ?? string.Join(" ", experience.Bullets.Select(b => b.Text)));
                else
                    AddBullets(entry, experience.Bullets);
            });
        }
    }

    private static void AddEducationSection(ColumnDescriptor column, List<TailoredCvEducation>? educations, CvText t)
    {
        if (educations is not { Count: > 0 })
            return;
        AddHeading(column, t.Education);
        foreach (var education in educations)
        {
            column.Item().PreventPageBreak().PaddingBottom(6).Column(entry =>
            {
                var degreeField = JoinPlace(education.Degree, education.Field);
                var dates = t.FormatRange(education.StartMonth, education.EndMonth);
                if (string.IsNullOrWhiteSpace(degreeField))
                    AddEntryHeader(entry, education.School, education.Location, dates);
                else
                    AddEntryHeader(entry, degreeField, JoinPlace(education.School, education.Location), dates);
                AddBullets(entry, education.Bullets);
            });
        }
    }

    private static void AddCertificationSection(ColumnDescriptor column, List<TailoredCvCertification>? certifications, CvText t)
    {
        if (certifications is not { Count: > 0 })
            return;
        AddHeading(column, t.Certifications);
        foreach (var certification in certifications)
        {
            column.Item().PreventPageBreak().PaddingBottom(6).Column(entry =>
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
                AddBullets(entry, certification.Bullets);
            });
        }
    }

    private static void AddProjectSection(ColumnDescriptor column, List<TailoredCvProject>? projects, CvText t)
    {
        if (projects is not { Count: > 0 })
            return;
        AddHeading(column, t.Projects);
        foreach (var project in projects)
        {
            column.Item().PreventPageBreak().PaddingBottom(6).Column(entry =>
            {
                AddEntryHeader(entry, project.Name, project.Location, t.FormatRange(project.StartMonth, project.EndMonth));
                if (!string.IsNullOrWhiteSpace(project.Url))
                    entry.Item().Text(project.Url);
                AddBullets(entry, project.Bullets);
            });
        }
    }

    // NAV's layout: the language name, then written and spoken levels on their own lines.
    private static void AddLanguagesSection(ColumnDescriptor column, List<TailoredCvLanguage>? languages, CvText t)
    {
        if (languages is not { Count: > 0 })
            return;
        AddHeading(column, t.Languages);
        foreach (var language in languages)
        {
            column.Item().PaddingBottom(6).Column(entry =>
            {
                entry.Item().Text(t.LanguageName(language.Name)).Bold();
                if (language.Written is { } written)
                    entry.Item().Text($"{t.Written}: {t.Proficiency(written)}");
                if (language.Spoken is { } spoken)
                    entry.Item().Text($"{t.Spoken}: {t.Proficiency(spoken)}");
            });
        }
    }

    private static void AddSkillsSection(ColumnDescriptor column, List<TailoredCvSkill>? skills, CvText t)
    {
        if (skills is not { Count: > 0 })
            return;
        AddHeading(column, t.Skills);
        // Plain, comma-separated names: no bars/dots for level, which an ATS can't read anyway.
        column.Item().Text(string.Join(", ", skills.Select(s => s.Name)));
    }

    private static void AddLinksSection(ColumnDescriptor column, List<TailoredCvLink>? links, CvText t)
    {
        if (links is not { Count: > 0 })
            return;
        AddHeading(column, t.Links);
        foreach (var link in links)
            column.Item().Text($"{link.Label}: {link.Url}");
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
        column.Item().PaddingTop(12).PaddingBottom(6).Text(text).FontSize(13).Bold();
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
