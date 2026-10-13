using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace JobHunt.Api.Features.TailoredCvDrafts;

// Pure function, no DB/DI, so the layout can be tested by rendering a TailoredCvContent directly.
// Deliberately plain and single-column: a standard CV layout that an ATS can parse reliably.
// Visual polish is a later proposal.
public static class TailoredCvPdfRenderer
{
    private const string Bullet = "• ";

    public static byte[] Render(TailoredCvContent content)
    {
        TailoredCvPdfFonts.EnsureRegistered();

        return QuestPDF.Fluent.Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontFamily(TailoredCvPdfFonts.FamilyName).FontSize(10));
                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });

                page.Content().Column(column =>
                {
                    column.Spacing(8);
                    AddContact(column, content);

                    if (!string.IsNullOrWhiteSpace(content.Headline))
                        column.Item().Text(content.Headline).FontSize(12).SemiBold();

                    AddTextSection(column, "Summary", content.Summary);
                    AddExperienceSection(column, content.Experiences);
                    AddEducationSection(column, content.Educations);
                    AddCertificationSection(column, content.Certifications);
                    AddProjectSection(column, content.Projects);
                    AddSkillsSection(column, content.Skills);
                    AddLanguagesSection(column, content.Languages);
                    AddLinksSection(column, content.Links);
                });
            });
        }).GeneratePdf();
    }

    private static void AddContact(ColumnDescriptor column, TailoredCvContent content)
    {
        if (!string.IsNullOrWhiteSpace(content.FullName))
            column.Item().Text(content.FullName).FontSize(16).Bold();

        var parts = new[] { content.Email, content.Phone, content.Location }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var line = string.Join("  |  ", parts);
        if (!string.IsNullOrWhiteSpace(line))
            column.Item().Text(line);
    }

    private static void AddTextSection(ColumnDescriptor column, string heading, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        AddHeading(column, heading);
        column.Item().Text(text);
    }

    private static void AddExperienceSection(ColumnDescriptor column, List<TailoredCvExperience>? experiences)
    {
        if (experiences is not { Count: > 0 })
            return;
        AddHeading(column, "Experience");
        foreach (var experience in experiences)
        {
            column.Item().Column(entry =>
            {
                entry.Spacing(2);
                entry.Item().Text(text =>
                {
                    text.Span($"{experience.Title}, {experience.Employer}").SemiBold();
                    text.Span("  —  ");
                    text.Span(FormatRange(experience.StartMonth, experience.EndMonth));
                });
                AddBullets(entry, experience.Bullets);
            });
        }
    }

    private static void AddEducationSection(ColumnDescriptor column, List<TailoredCvEducation>? educations)
    {
        if (educations is not { Count: > 0 })
            return;
        AddHeading(column, "Education");
        foreach (var education in educations)
        {
            column.Item().Column(entry =>
            {
                entry.Spacing(2);
                var degreeField = string.Join(", ",
                    new[] { education.Degree, education.Field }.Where(p => !string.IsNullOrWhiteSpace(p)));
                var title = string.IsNullOrWhiteSpace(degreeField) ? education.School : $"{degreeField}, {education.School}";
                entry.Item().Text(text =>
                {
                    text.Span(title).SemiBold();
                    text.Span("  —  ");
                    text.Span(FormatRange(education.StartMonth, education.EndMonth));
                });
                AddBullets(entry, education.Bullets);
            });
        }
    }

    private static void AddCertificationSection(ColumnDescriptor column, List<TailoredCvCertification>? certifications)
    {
        if (certifications is not { Count: > 0 })
            return;
        AddHeading(column, "Certifications");
        foreach (var certification in certifications)
        {
            column.Item().Column(entry =>
            {
                entry.Spacing(2);
                entry.Item().Text(text =>
                {
                    var title = string.IsNullOrWhiteSpace(certification.Issuer)
                        ? certification.Name
                        : $"{certification.Name}, {certification.Issuer}";
                    text.Span(title).SemiBold();
                    if (certification.IssuedMonth is { } issued)
                    {
                        text.Span("  —  ");
                        text.Span(FormatMonth(issued));
                        if (certification.ExpiresMonth is { } expires)
                        {
                            text.Span(" – ");
                            text.Span(FormatMonth(expires));
                        }
                    }
                    else if (certification.ExpiresMonth is { } expiry)
                    {
                        text.Span($"  —  Expires {FormatMonth(expiry)}");
                    }
                });
                if (!string.IsNullOrWhiteSpace(certification.Url))
                    entry.Item().Text(certification.Url);
                AddBullets(entry, certification.Bullets);
            });
        }
    }

    private static void AddProjectSection(ColumnDescriptor column, List<TailoredCvProject>? projects)
    {
        if (projects is not { Count: > 0 })
            return;
        AddHeading(column, "Projects");
        foreach (var project in projects)
        {
            column.Item().Column(entry =>
            {
                entry.Spacing(2);
                entry.Item().Text(text =>
                {
                    text.Span(project.Name).SemiBold();
                    var range = FormatRange(project.StartMonth, project.EndMonth);
                    if (!string.IsNullOrEmpty(range))
                    {
                        text.Span("  —  ");
                        text.Span(range);
                    }
                });
                if (!string.IsNullOrWhiteSpace(project.Url))
                    entry.Item().Text(project.Url);
                AddBullets(entry, project.Bullets);
            });
        }
    }

    private static void AddSkillsSection(ColumnDescriptor column, List<TailoredCvSkill>? skills)
    {
        if (skills is not { Count: > 0 })
            return;
        AddHeading(column, "Skills");
        // Plain, comma-separated names: no bars/dots for level, which an ATS can't read anyway.
        column.Item().Text(string.Join(", ", skills.Select(s => s.Name)));
    }

    private static void AddLanguagesSection(ColumnDescriptor column, List<TailoredCvLanguage>? languages)
    {
        if (languages is not { Count: > 0 })
            return;
        AddHeading(column, "Languages");
        column.Item().Text(string.Join(", ", languages.Select(FormatLanguage)));
    }

    private static void AddLinksSection(ColumnDescriptor column, List<TailoredCvLink>? links)
    {
        if (links is not { Count: > 0 })
            return;
        AddHeading(column, "Links");
        foreach (var link in links)
            column.Item().Text($"{link.Label}: {link.Url}");
    }

    private static void AddBullets(ColumnDescriptor entry, List<TailoredCvBullet> bullets)
    {
        foreach (var bullet in bullets)
            entry.Item().Text($"{Bullet}{bullet.Text}");
    }

    private static void AddHeading(ColumnDescriptor column, string text) =>
        column.Item().PaddingTop(4).BorderBottom(1).PaddingBottom(2)
            .Text(text.ToUpperInvariant()).FontSize(11).Bold();

    private static string FormatRange(DateOnly? start, DateOnly? end)
    {
        if (start is null && end is null)
            return "";
        var startText = start is { } s ? FormatMonth(s) : "";
        var endText = end is { } e ? FormatMonth(e) : "Present";
        return $"{startText} – {endText}";
    }

    private static string FormatMonth(DateOnly date) => date.ToString("MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatLanguage(TailoredCvLanguage language) =>
        language.Level is { } level ? $"{language.Language} ({level})" : language.Language.ToString();
}
