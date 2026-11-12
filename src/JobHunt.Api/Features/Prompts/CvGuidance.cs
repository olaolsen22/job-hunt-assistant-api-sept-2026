using JobHunt.Api.Features.TailoredCvDrafts;

namespace JobHunt.Api.Features.Prompts;

// Prompt rules shared by the CV, cover letter, answer and summary prompts.
internal static class CvGuidance
{
    // Recruiters look for contribution and results, in the candidate's own words (docs/norwegian-cv-formatting.md §10).
    internal static string BulletSelection(TailoredCvTemplate template) => $"""
        {BulletStrength(template)} When companyResearch.kind is CONSULTANCY or BROKER, also prefer client or stakeholder work,
        learning a new domain, and clarifying requirements.
        """;

    internal static string BulletStrength(TailoredCvTemplate template, bool rewrite = false) => $"""
        The CV must fit {(template == TailoredCvTemplate.Concise ? "one page" : "1-2 pages")}: prefer the few strongest, most specific bullets, especially ones showing the
        candidate's own contribution, a problem solved, or a result, over near-duplicates, generic duties, or bare
        lists of technologies. Prefer bullets matching the kind of work the listing emphasises, for example a
        frontend role: user-facing features, components and state, accessibility, testing, performance, work with
        design and product; an API role: endpoints, integrations, data flows, authentication; a platform role:
        deployment, automation, monitoring, operations; a senior role: technical decisions, ownership, mentoring,
        coordination; a service or retail role: customer contact, payments, hygiene and safety routines, pace,
        teamwork. {(rewrite ? RewrittenOtherField : OtherField)}
        """;

    // A software developer applying for a café or shop job still has relevant evidence in their IT roles, but
    // not in the technical detail; recruiters there look for people skills and reliability.
    internal const string OtherField = """
        When the listing is outside the field of most of the candidate's experience (for example a service, retail,
        care, or warehouse job for a software developer), aim the CV at that job and write for a reader outside the
        candidate's field. From those other entries prefer bullets showing work the listing values, such as contact
        with customers, clients, or users, teamwork across roles, training or helping others, taking responsibility,
        and working to routines or deadlines. Where you shorten or summarise such a bullet, keep only that plain fact:
        cut technology and tool names, IT jargon such as UI, UX, frontend, backend, deployment, or development
        process, and technical results and metrics, which that listing does not care about; this overrides any rule
        here about keeping such facts. For example, "Effektiviserte utviklingsprosessen i samarbeid med produkt-,
        design- og mobilteamene, ca. 30 % raskere leveranser" becomes "Samarbeidet med produkt-, design- og
        mobilteamene". Prefer no bullet over one that only makes sense to IT people. Keep the source's words for who
        was served: users, clients, or colleagues are not customers unless the bullet says so.
        """;

    // With rewrite on, IT work is translated for the reader instead of dropped: a job held for years still shows
    // responsibility and reliability. TailoredCvWording.KeepsSourceFacts keeps the translation to the source's facts.
    internal const string RewrittenOtherField = """
        When the listing is outside the field of most of the candidate's experience (for example a service, retail,
        care, or warehouse job for a software developer), aim the CV at that job and write for a reader outside the
        candidate's field. Translate the other entries rather than dropping them: rewrite their selected bullets in
        everyday words so that reader understands what the work was, who it served, and what the candidate was
        responsible for, preferring work such as contact with customers, clients, or users, teamwork across roles,
        training or helping others, taking responsibility, solving problems, and working to routines or deadlines.
        Leave out technology and tool names, IT jargon, and technical metrics. For example, "Har vedlikeholdt en
        intern backend-applikasjon utviklet i C# og .NET ved å rette feil" becomes "Holdt et internt dataprogram i
        drift og rettet feil". Keep the source's words for who was served: users, clients, or colleagues are not
        customers unless the bullet says so.
        """;

    // Listings stress themes without naming them as skills; show supported evidence, not just keywords.
    internal const string Theme = """
        Identify the themes the listing emphasises, such as AI-powered features, LLMs, cloud, accessibility,
        customer service, or food hygiene, and
        actively surface supported evidence of them; never invent it. Keep using AI tools, such as coding
        assistants, distinct from building AI features or machine learning; never present one as the other.
        An UNSOLICITED listing (open application, no posting) has no requirements: take the themes from its job role and
        companyResearch, the employer's described work and stack, instead. For OPEN_INVITATION the description is the
        work profile the employer invites open applications for.
        """;

    // A CV is read as the candidate's overall profile too: a listing-only keyword list hides their core stack.
    internal static string SkillSelection(TailoredCvTemplate template) => $"""
        Select the skills the listing asks for or that support its themes, plus the candidate's core professional
        stack (skills used in work or projects, higher level first) even when the listing does not name them. Keep
        a skill only when it matches the role, shows useful breadth, or shows a relevant strength; usually about
        {(template == TailoredCvTemplate.Concise ? "8-12" : "12-20")} skills. Never drop foundational skills such as Git, SQL, testing, or CI/CD as too common when the
        listing asks for or implies them. Include a skill not used in work or projects only when the listing asks
        for it. Leave out ordinary office tools unless the listing requires them, and skills unrelated to the role.
        Order by relevance, most relevant first: required by the listing, then nice to have or theme-related, then
        core supporting skills; within each group skills used in work first, then in projects, then higher level.
        For a listing outside the candidate's field, select only skills it asks for or that transfer to it, such as
        service skills, licences, and certifications, and leave out the technical stack.
        """;

    // A missing job or school leaves a visible gap in the timeline.
    // An unrelated bullet forced onto every entry reads as padding; title and dates alone still close the gap.
    internal const string Timeline = """
        Keep every work experience and education entry so the CV shows a complete timeline: when such an entry has
        bullets, select at least its most relevant one even if it is only loosely related, unless the listing is
        outside the candidate's field and none of its bullets shows, in plain words, work that listing values; then
        select none and the entry shows only its title and dates.
        """;

    // A translated bullet explains an IT job to a non-IT reader better than title and dates alone.
    internal const string RewrittenTimeline = """
        Keep every work experience and education entry so the CV shows a complete timeline: when such an entry has
        bullets, select at least its most relevant one, rewritten in plain words when the listing is outside the
        candidate's field.
        """;

    // A capped concise CV easily drops a requirement the profile does show; recruiters screen for those first.
    internal const string RequirementCoverage = """
        Before picking a second bullet on the same theme, pick the bullet that shows a listing requirement or
        required skill not yet covered, so every requirement the profile supports is shown somewhere in the CV.
        """;

    // The summary is read first; a claim the body doesn't back, or a key strength it omits, reads as padding.
    internal const string SummaryConsistency = """
        The summary and the CV body must agree: name only strengths, technologies, and domains that the selected
        bullets or skills show, and reflect the strongest selected evidence for the listing's main requirements.
        """;

    // Models tend to treat language courses as irrelevant education, although they are the evidence for a
    // listing's language requirement.
    internal const string LanguageEvidence = """
        When the listing requires or mentions a language, such as Norwegian, treat courses, education, and
        certifications in that language as relevant evidence and never drop them.
        """;

    internal const string Voice = """
        Write in the candidate's own voice: match the person, tense, tone, and formality of their supplied text and
        reuse their wording and terms where they fit, rather than generic CV phrasing. Norwegian must read as
        natural, idiomatic bokmål, not a literal translation or anglicized CV phrasing.
        """;

    // Recruiters and hiring managers read stock phrasing as AI-written (Wikipedia's "Signs of AI writing").
    internal const string Style = """
        Avoid stock CV and AI phrasing: no em dashes, no lists of three adjectives, no "not only ... but also"
        constructions, and none of these words: spearheaded, leveraged, utilized, orchestrated, passionate,
        results-driven, innovative, seamless, cutting-edge, dynamic, resultatorientert, løsningsorientert, brennende
        engasjert, dynamisk, innovativ.
        """;

    // One-line bullets cut from the candidate's wording (TailoredCvWording checks).
    internal const string ConciseBullets = """
        Shorten each selected bullet by cutting words: keep the candidate's own verbs and terms, and drop only filler
        and repeated context. Never cut a fact the listing cares about, such as what was built, a technology, a
        domain, the scale, or a result (for a listing outside the candidate's field, technical facts are not among
        them); aim for one line of about 100 characters and use up to about 160 when cutting further would lose such
        a fact. Never add a technology, tool, number, or claim the bullet does not state. Do not shorten a bullet
        that is already short.
        """;

    // Recruiters and ATS match the listing's own terms, but a CV that parrots the listing reads as fake.
    // TailoredCvWording.KeepsSourceWords holds the result to the bullet's and the listing's words.
    internal const string ListingWording = """
        Where a bullet shows work the listing names or values, reword it with the listing's term for that work, for
        example "with product, design, and mobile teams" becomes "within cross-functional product, design, and
        mobile teams" when the listing asks for cross-functional teamwork. Keep the bullet's facts, numbers, and the
        candidate's voice; use only the bullet's and the listing's words plus at most 2 small linking or grammar
        words. Never copy a listing sentence, stack several listing phrases into one bullet, or add a term for work
        the bullet does not show. Leave a bullet as it is when no listing term fits.
        """;

    // Every line must hold up in an interview; models tend to inflate verbs, add metrics, and borrow credit.
    internal const string HonestClaims = """
        Keep every claim as strong as its source: add no numbers, percentages, or metrics; keep team results as the
        team's and the candidate's stated part as theirs (do not turn "helped" into "led"); do not present one task
        as the whole role; keep coursework, personal projects, and learning distinct from professional work. Show
        qualities through what the candidate did, not adjectives such as passionate, innovative, or results-oriented.
        """;

    // Years and self-rated skill levels are easy to turn into "senior" or "expert" claims the CV can't back up.
    internal const string Seniority = """
        Do not state a seniority level or a management role the job titles do not show; years alone are not
        seniority, and years are counted only from the experience dates. Skill levels are the candidate's own
        ratings for emphasis; never turn them into claims such as "expert" or "advanced".
        """;

    // A broad listing tempts the model to rebrand a specialist as fullstack, hiding their strongest case.
    internal const string Specialization = """
        Present the candidate with the specialization their job titles and headline show. For a broader role,
        describe them as that specialist with experience in the other areas the evidence supports (for example
        "frontend developer with backend experience in .NET"); never relabel them as fullstack, backend, or any
        other role their job titles do not show, however the listing words it. When the listing is outside that
        field and one of their job titles is in the listing's field, present them by that title instead (for example
        "butikkmedarbeider" for a shop or café job) and mention their main field briefly at most.
        """;

    // The headline is the CV's first line and the cheapest per-application adjustment (NAV, NTNU).
    internal const string Headline = """
        headline: one short title line of at most 80 characters, starting from the profile's own headline. Name
        the candidate's specialization as their job titles show it and, where the evidence supports it, the
        listing's role family or main technology (for example "Frontend developer | React and TypeScript", or
        "Butikkmedarbeider | Kundeservice og kasse" when a listing outside their main field matches another job title). No
        seniority the job titles do not show, no adjectives, no company name. null keeps the current headline.
        """;

    // A listing skill the candidate lacks may be bridged by real adjacent work, never implied by a similar tool.
    internal const string Gap = """
        When the listing asks for a technology or credential the candidate lacks, never claim it or imply that a
        similar tool is the same; surface the closest real evidence under its own name (for example GitHub Actions
        for an Azure Pipelines listing) and name the shared capability (automated build, test, and deployment) only
        where the source supports it. An intention to learn is not a skill.
        """;

    // An entry's line shows where technologies were used; practices and AI assistants read as mistakes there.
    internal const string Technologies = """
        at most 6 of the entry's own skills that are concrete technologies (languages, frameworks, libraries,
        platforms, databases, developer tools) the selected bullets show were used, most relevant to the listing
        first. Never include practices, methods, or soft skills such as TDD, code review, CI, testing, or
        architecture, nor AI coding assistants; leave those to the paragraph. Never include a skill only because
        the listing names it, or one the entry does not have.
        """;

    internal const string ListingTerms = """
        Where a bullet describes something the listing names, you may use the listing's term for it; never add a
        term the bullets do not support.
        """;

    // Shared with the external prompt; the concise summary sits in the top third a recruiter actually reads.
    internal static string SummaryLength(TailoredCvTemplate template) => template == TailoredCvTemplate.Concise
        ? "2 short sentences, at most 220 characters so it fits three lines"
        : "2-3 short sentences, at most 280 characters so it fits three lines";

    // The summary, headline and wording rules of both external CV prompts.
    // writesBullets: the AI words the bullets too, rather than copying them.
    internal static string ExternalWriting(bool writesBullets) => $$"""
        {{Seniority}} {{Specialization}}
        Set content.{{Headline}}
        For the summary, {{(writesBullets ? "bullets, " : "")}}and paragraphs: {{Voice}} {{Style}} {{HonestClaims}}
        """;

    // Checked like concise bullets (TailoredCvWording), with a few more linking words; a failing paragraph is dropped.
    internal const string ConciseParagraph = """
        For every entry with selected bullets, also return a short paragraph of at most 2 sentences (about 250
        characters) made from those selected bullets' own words, in the language they are supplied in: you may drop
        words and add at most 4 small linking words, but no other term and no number the bullets do not state.
        """;

    // Checked by TailoredCvWording.KeepsSourceFacts instead: new words are fine, new numbers and skills are not.
    internal const string RewrittenConciseParagraph = """
        For every entry with selected bullets, also return a short paragraph of at most 2 sentences (about 250
        characters) that restates only those selected bullets' facts in plain words, in the language they are supplied
        in: no fact, number, technology, or tool the bullets do not state.
        """;

    // Concise bullet caps by recency: recruiters weigh the newest role most. Profile lists come sorted newest first.
    internal const int NewestRoleBullets = 5;
    internal const int RecentRoleBullets = 3; // the next two roles
    internal const int OtherEntryBullets = 2; // older roles, each education entry and each project
    internal const int ConciseProjects = 2;

    internal static int ConciseBulletCap(TailoredCvSection section, int experienceIndex) =>
        section != TailoredCvSection.Experience ? OtherEntryBullets
        : experienceIndex switch { 0 => NewestRoleBullets, 1 or 2 => RecentRoleBullets, _ => OtherEntryBullets };

    // A mix of entries with and without a paragraph looks unfinished when the user switches to paragraph style.
    internal const string DetailedParagraph = """
        For every entry with selected bullets, also return a concise natural paragraph based only on those bullet
        facts; rephrase or combine facts, but do not infer facts or invent achievements; never leave paragraph null
        on an entry with selected bullets.
        """;

    // The paragraph, timeline and (CONCISE) cap rules of both external CV prompts.
    internal static string Layout(TailoredCvTemplate template, bool rewrite = false)
    {
        var concise = template == TailoredCvTemplate.Concise;
        var paragraph = !concise ? DetailedParagraph : rewrite ? RewrittenConciseParagraph : ConciseParagraph;
        var caps = concise
            ? $"""
              Select at most {NewestRoleBullets} bullets for the newest experience, {RecentRoleBullets} for each of
              the next two, and {OtherEntryBullets} for each older experience and each education entry; include at most
              {ConciseProjects} projects, with at most {OtherEntryBullets} bullets each, and only those clearly relevant
              to the listing's main requirements.
              """
            : "";
        return $"""
            {paragraph} Set content.contentStyle to BULLETS by default so the user can choose the presentation for
            the whole CV. {(rewrite ? RewrittenTimeline : Timeline)} {caps}
            """.TrimEnd();
    }

    internal const string OutputRules = """
        For an entry with a location, set "location" to "City, Country" with the country name in the CV language,
        or just the country when the city is null. Copy language names and written/spoken levels from the profile.
        Skill categoryId: the id of the profile's skillCategories entry whose name and description fit the skill,
        or null for anything that fits none (licences, certifications, trade and service skills) and when unsure.
        Never invent category ids.
        Do not output the candidate's full name, contact details, home address, links, references, date of birth,
        nationality, work authorization, hobbies, or job-search preferences. The user fills contact fields in the UI.
        """;

    internal static string ContentShape(bool concise) => $$"""
        Required JSON shape:
        {
          "schemaVersion": 1,
          "content": {
            "language": "ENGLISH",
            "template": "{{(concise ? "CONCISE" : "DETAILED")}}",
            "contentStyle": "BULLETS",
            "headline": null,
            "summary": null,
            "experiences": [{"sourceEntryId": 1, "employer": "Example", "title": "Developer", "location": "Lagos, Nigeria", "startMonth": "2024-01-01", "endMonth": null, "paragraph": {{(concise ? "\"Short paragraph in the selected bullets' own words.\"" : "\"Concise paragraph based only on the selected bullet facts.\"")}}, "technologies": ["TypeScript"], "bullets": [{"sourceBulletId": 2, "text": "{{(concise ? "Shortened" : "Original")}} profile bullet"}]}],
            "educations": [{"sourceEntryId": 3, "school": "Example University", "degree": "MSc", "field": "Computer Science", "location": null, "startMonth": "2020-01-01", "endMonth": "2022-01-01", "paragraph": null, "bullets": []}],
            "certifications": [{"sourceEntryId": 4, "name": "Certification", "issuer": "Issuer", "issuedMonth": "2024-01-01", "expiresMonth": null, "url": null, "paragraph": null, "bullets": []}],
            "projects": [{"sourceEntryId": 5, "name": "Project", "kind": "PERSONAL", "url": null, "location": null, "startMonth": null, "endMonth": null, "paragraph": null, "technologies": [], "bullets": []}],
            "skills": [{"sourceSkillId": 6, "name": "TypeScript", "level": 5, "categoryId": 7}],
            "languages": [{"name": "English", "written": "NATIVE", "spoken": "NATIVE"}]
          }
        }
        """;
}
