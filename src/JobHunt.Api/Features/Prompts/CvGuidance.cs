using JobHunt.Api.Features.Companies;
using JobHunt.Api.Features.Listings;
using JobHunt.Api.Features.TailoredCvDrafts;

namespace JobHunt.Api.Features.Prompts;

// Prompt rules shared by the CV, cover letter, answer and summary prompts.
internal static class CvGuidance
{
    // The few CV rules whose breach makes the CV unusable; every other CV rule is a preference. Fewer, clearly ranked
    // rules are followed more reliably (docs/llm-prompting-research-2026-11-21.md §2-3). The server checks some too.
    internal static string Essentials => $"""
        Essential rules (a CV that breaks one is unusable):
        - Use only entries, facts, IDs, dates, skills, and levels present in the profile; never invent any.
        - {HonestClaims.ReplaceLineEndings(" ")}
        - {Seniority.ReplaceLineEndings(" ")}
        - Present the candidate by the titles they held; for a broader target, as that specialist with experience in
          the other areas the evidence supports. Never call the candidate fullstack, backend, or any other role their
          job titles do not show, however the target words it.
        """;

    // Recruiters look for contribution and results, in the candidate's own words (docs/norwegian-cv-formatting.md §10).
    // Consultancies and brokers sell work with clients, so that evidence counts more there; sent only for them.
    internal static string BulletSelection(TailoredCvTemplate template, CompanyKind? companyKind) =>
        companyKind is CompanyKind.Consultancy or CompanyKind.Broker
            ? BulletStrength(template) + " The employer is a consultancy or broker: also prefer client or stakeholder"
              + " work, learning a new domain, and clarifying requirements."
            : BulletStrength(template);

    internal static string BulletStrength(TailoredCvTemplate template) => $"""
        {Space(template)} Prefer specific bullets showing what the candidate delivered, their own part, and a problem
        solved or a result over near-duplicates, generic duties, or bare lists of technologies, because recruiters
        assess contribution rather than keywords. Lean toward the kind of work the target emphasises, for example
        user-facing features, components, accessibility, and testing for frontend; endpoints, integrations, and data
        for backend; deployment, automation, and operations for platform; decisions, ownership, and mentoring for
        senior roles.
        """;

    // Norwegian guidance: 1-2 pages, the most space for relevant work, and "shorten older and less relevant material
    // first" without removing useful evidence (docs/CV Research §3, Tekna). Fixed per-role bullet counts cut breadth
    // such as mentoring or client work, so there are none.
    private static string Space(TailoredCvTemplate template) => $"""
        The CV should fit {(template == TailoredCvTemplate.Concise ? "one page" : "1-2 pages")}, the length Norwegian employers expect. Give relevant work the most
        space. Keep each bullet that shows a distinct capability, responsibility, or result, including breadth such
        as mentoring, client or stakeholder work, migrations, documentation, and collaboration. When space runs
        short, cut near-duplicates and generic duties first, then shorten older and less relevant roles; never remove
        useful evidence just to save space.
        """;

    // A bullet such as "Arbeidstrening via NAV" shows a role was unpaid work training, not employment; dropped as
    // weak evidence, the CV passes it off as a paid job. An honesty rule, so the prompts list it among the essentials.
    internal const string EngagementTerms = """
        A bullet that states the terms of the role, such as arbeidstrening, work training, internship, volunteer
        work, or unpaid work, is not evidence but tells the reader the role was not ordinary paid employment: always
        select it as its own bullet, never combined with others, and keep those terms in its text, even when it
        is the entry's only selected bullet.
        """;

    [Flags]
    internal enum OutOfFieldParts
    {
        Presentation = 1, Bullets = 2, Timeline = 4, Skills = 8,
        All = Presentation | Bullets | Timeline | Skills,
    }

    // A software developer applying for a café or shop job still has relevant evidence in their IT roles, but not in
    // the technical detail; those recruiters look for people skills and reliability. One block, after the rules it
    // bends, so normal CVs don't carry the case inside every rule; each built-in section takes only its parts.
    // With rewrite, IT work is translated instead of dropped, held to the source's facts by
    // TailoredCvWording.KeepsSourceFacts. Without it, title and dates alone still close the timeline gap, where an
    // unrelated bullet forced onto the entry would read as padding.
    internal static string OutOfFieldTarget(OutOfFieldParts parts, bool rewrite = false)
    {
        List<string> lines =
        [
            "If the target is outside the field of most of the candidate's experience (for example a shop, café, care, or warehouse job for a software developer), write for a reader outside that field:",
        ];
        if (parts.HasFlag(OutOfFieldParts.Presentation))
            lines.Add("- When they held a job title in the target's field (for example \"butikkmedarbeider\" for a shop or café job), present them by that title and mention their main field briefly at most.");
        if (parts.HasFlag(OutOfFieldParts.Bullets))
            lines.Add("- Prefer work such as contact with customers, clients, or users, teamwork, helping or training others, taking responsibility, and routines or deadlines, and leave out tool names, IT jargon such as UI, UX, frontend, backend, or deployment, and technical metrics; this overrides any rule here about keeping such facts. "
                + (rewrite
                    ? "Translate IT entries into everyday words rather than dropping them, for example \"Har vedlikeholdt en intern backend-applikasjon utviklet i C# og .NET ved å rette feil\" becomes \"Holdt et internt dataprogram i drift og rettet feil\"."
                    : "Where you shorten such a bullet, keep only its plain fact. Prefer no bullet over one that only makes sense to IT people.")
                + " Users, clients, or colleagues are not customers unless the bullet says so.");
        if (parts.HasFlag(OutOfFieldParts.Timeline))
            lines.Add(rewrite
                ? "- For every work experience and education entry with bullets, select at least its most relevant one, rewritten in plain words."
                : "- For a work experience or education entry none of whose bullets makes sense to that reader, select none and the entry shows only its title and dates, unless one of its bullets states the terms of the role; then select only that one.");
        if (parts.HasFlag(OutOfFieldParts.Skills))
            lines.Add("- Select only skills the target names or that transfer to it, such as service skills, licences, and certifications, and leave out the technical stack.");
        return string.Join("\n", lines);
    }

    // Listings stress themes without naming them as skills; show supported evidence, not just keywords.
    // The kind sentences go only to the listings they describe.
    internal static string Theme(ListingKind kind) => """
        Identify the themes the listing emphasises, such as AI-powered features, LLMs, cloud, accessibility,
        customer service, or food hygiene, and actively surface supported evidence of them; never invent it. Keep
        using AI tools, such as coding assistants, distinct from building AI features or machine learning; never
        present one as the other.
        """ + kind switch
    {
        ListingKind.Unsolicited => " This is an open application with no posting and no requirements: take the themes"
            + " from its job role and companyResearch, the employer's described work and stack, instead.",
        ListingKind.OpenInvitation => " The listing invites open applications: its description is the work profile the"
            + " employer invites them for.",
        _ => "",
    };

    // Non-technical recruiters and keyword filters read the list literally and don't infer HTML or CSS from React.
    // No source caps the list (docs/norwegian-cv-formatting.md §10); category ids group it, so it stays scannable.
    internal static string SkillSelection(TailoredCvTemplate template) => $"""
        Select about {(template == TailoredCvTemplate.Concise ? "15-20" : "25-30")} skills. Non-technical recruiters and keyword filters read the list literally, so
        always include the foundations of the stack the CV shows (for example HTML, CSS, and JavaScript with React,
        Vue, or Angular; SQL with databases; Git), and cover each area the profile has evidence in, such as frontend,
        backend, databases, testing, and delivery. Order by relevance to the target, most relevant first; within
        that, skills used in work first, then in projects, then higher level. Include a skill not used in work or
        projects only when the target names it, and leave out ordinary office tools unless the target requires them.
        """;

    // A missing job or school leaves a visible gap in the timeline. OutOfFieldTarget holds the exception.
    private const string Timeline = """
        Keep every work experience and education entry so the CV shows a complete timeline: when such an entry has
        bullets, select at least its most relevant one.
        """;

    // Course codes and internal module names say nothing to a reader outside the institution.
    internal const string EducationWording = """
        Prefer bullets that name the subject, project, or thesis in words a reader outside the institution
        understands over course codes or internal module names.
        """;

    // A short CV easily drops a requirement the profile does show; recruiters screen for those first.
    internal const string RequirementCoverage = """
        Before picking a second bullet on the same theme, prefer one that shows a listing requirement or required
        skill not yet covered, so the requirements the profile supports show somewhere in the CV.
        """;

    // What a Norwegian key-qualifications summary names (NTNU, Tekna): the field, the work, and a few specifics.
    internal const string SummaryContent = $"""
        Name the candidate's specialization, the kind of work they have done, and one or two tools, methods, or
        responsibilities the body supports, shown through what they built or solved rather than a bare list of
        technology names. {SummaryYears}
        """;

    // Recruiters look for years in the summary, and models miscount them from dates, so the server counts months.
    internal const string SummaryYears = """
        State how many years of work experience the candidate has in their field, in whole years rounded down:
        use experienceMonths when all their work experiences are in that field, otherwise add up the months of
        the experiences in that field. Never count years from the dates yourself, and omit years under one.
        """;

    // The summary is read first; a claim the body doesn't back, or a key strength it omits, reads as padding.
    internal const string SummaryConsistency = """
        The summary and the CV body must agree: name only strengths, technologies, and domains that the selected
        bullets or skills show, and reflect the strongest selected evidence for the target.
        """;

    // Models tend to treat language courses as irrelevant education, although they are the evidence for a
    // listing's language requirement.
    internal const string LanguageEvidence = """
        When the listing requires or mentions a language, such as Norwegian, treat courses, education, and
        certifications in that language as relevant evidence and never drop them.
        """;

    internal const string Voice = $"""
        Write in the candidate's own voice: match the person, tense, tone, and formality of their supplied text and
        reuse their wording and terms where they fit, rather than generic CV phrasing. {Bokmal}
        """;

    // For prompts that already have a more specific voice rule of their own.
    internal const string Bokmal = "Norwegian must read as natural, idiomatic bokmål, not a literal translation or anglicized CV phrasing.";

    // Recruiters and hiring managers read stock phrasing as AI-written (Wikipedia's "Signs of AI writing").
    internal const string Style = """
        Avoid stock CV and AI phrasing: no em dashes, no lists of three adjectives, no "not only ... but also"
        constructions, and none of these words: spearheaded, leveraged, utilized, orchestrated, passionate,
        results-driven, results-oriented, innovative, seamless, cutting-edge, dynamic, resultatorientert,
        løsningsorientert, brennende engasjert, dynamisk, innovativ. Show qualities through what the candidate did,
        not adjectives.
        """;

    // One-line bullets cut from the candidate's wording (TailoredCvWording checks). Up to ~160 rather than the 140 of
    // ConciseWrittenBullets: cutting words can't merge or condense, so a bullet needs more room to keep its fact.
    internal const string ConciseBullets = """
        Shorten each selected bullet by cutting words: keep the candidate's own verbs and terms, and drop only filler
        and repeated context. Never cut a fact the target cares about, such as what was built, a technology, a
        domain, the scale, or a result; aim for one line of about 100 characters and use up to about 160 when cutting
        further would lose such a fact. Never add a technology, tool, number, or claim the bullet does not state. Do not shorten a bullet
        that is already short.
        """;

    // A CONCISE CV tailored to a listing. The profile is a dumping ground, so cutting words from one bullet at a time
    // left long lists and cherry-picked fragments. The bullets are written here, merged and condensed;
    // TailoredCvWording.KeepsSourceFacts holds them to their entry's numbers and skill names.
    internal const string ConciseWrittenBullets = """
        The profile is the candidate's raw notes, not a finished CV: write the entry's CV bullets from its bullets
        instead of copying them. The reader is a hiring manager who must quickly assess the experience: each entry
        should show its key points, enough to back the CV, with each point easy to scan. Each bullet is one line of
        about 100 characters, never more than 140. Cut filler and repeated context, and condense lists of uses,
        tools, or tasks to the items that matter to the listing. Merge related bullets of the same entry into one
        bullet where that reads shorter and stronger, using the first merged bullet's ID.
        Never attach one bullet's verb or result to another bullet's work, and never merge bullets in different
        languages. Write like the candidate: keep their verbs, terms, sentence shape, tense, and tone, so each
        bullet reads as their own tightened sentence, not generic CV phrasing. Where a bullet shows work the
        listing names, you may use the listing's term for it. Add no fact, technology, tool, number, or claim the
        entry's bullets do not state. For example, "Evaluated and implemented practical uses of AI in development
        for checks before peer review, code quality reviews, boilerplate generation, unit and end-to-end test
        creation, and analysis of legacy functionality where requirements and documentation were missing" becomes
        "Implemented AI in development for checks before peer review, test creation, and analysis of undocumented
        legacy code".
        """;

    // Recruiters and ATS match the listing's own terms, but a CV that parrots the listing reads as fake. Profile
    // bullets are often long notes, so a DETAILED tailored CV also tightens them toward the listing; cutting words
    // passes TailoredCvWording.KeepsSourceWords, which holds the result to the bullet's and the listing's words.
    internal const string ListingWording = """
        Make each selected bullet direct for this listing: lead with the work and result the listing cares about,
        and cut filler, repeated context, and details the listing does not care about, so it reads in one or two
        lines. Never cut a fact the listing cares about, such as what was built, a technology, a domain, the scale,
        or a result. Where a bullet shows work the listing names or values, reword it with the listing's term for
        that work, for example "with product, design, and mobile teams" becomes "within cross-functional product,
        design, and mobile teams" when the listing asks for cross-functional teamwork. Keep the bullet's facts,
        numbers, and the candidate's voice; use only the bullet's and the listing's words plus at most 2 small
        linking or grammar words. Never copy a listing sentence, stack several listing phrases into one bullet, add
        a term for work the bullet does not show, or merge bullets. Leave a bullet that is already short and direct
        as it is.
        """;

    // Every line must hold up in an interview; models tend to inflate verbs, add metrics, and borrow credit.
    internal const string HonestClaims = """
        Keep every claim as strong as its source: add no numbers, percentages, or metrics; keep team results as the
        team's and the candidate's stated part as theirs (do not turn "helped" into "led"); do not present one task
        as the whole role; keep coursework, personal projects, and learning distinct from professional work.
        """;

    // Years and self-rated skill levels are easy to turn into "senior" or "expert" claims the CV can't back up.
    internal const string Seniority = """
        Do not state a seniority level or a management role the job titles do not show; years alone are not
        seniority. Skill levels are the candidate's own ratings for emphasis; never turn them into claims such as
        "expert" or "advanced".
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

    // The headline is the CV's first line and the cheapest per-application adjustment (NAV, NTNU). A single
    // profile-like example ("Frontend developer | React and TypeScript") was copied verbatim, so this gives a pattern.
    internal const string Headline = """
        headline: one title line of at most 80 characters, never null. Use the target's role title when the
        candidate's job titles support it (a "Software Developer" title supports "Software Engineer"); otherwise the
        title their job titles show. After a "|", name the main technology or area the evidence supports, and a
        second area when the CV shows one, in a pattern such as "<title> | <technology> and <area>" or "<title> with
        <area> experience in <technology>". No seniority the job titles do not show, no adjectives, no company name.
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
        platforms, databases, developer tools) the selected bullets show were used, most relevant to the target
        first. Never include practices, methods, or soft skills such as TDD, code review, CI, testing, or
        architecture, nor AI coding assistants; leave those to the paragraph. Never include a skill only because
        the target names it, or one the entry does not have.
        """;

    internal const string ListingTerms = """
        Where a bullet describes something the listing names, you may use the listing's term for it; never add a
        term the bullets do not support.
        """;

    // Shared with the external prompt; the concise summary sits in the top third a recruiter actually reads.
    internal static string SummaryLength(TailoredCvTemplate template) => template == TailoredCvTemplate.Concise
        ? "2 short sentences, at most 220 characters so it fits three lines"
        : "2-3 short sentences, at most 280 characters so it fits three lines";

    // The essential language rule of both external CV prompts. A paragraph checked word by word against its bullets
    // (ConciseParagraph) must stay in their language; the others are written in the CV's language.
    internal static string LanguageRule(Language target, bool paragraphsInBulletLanguage)
    {
        var code = target == Language.Norwegian ? "NORWEGIAN" : "ENGLISH";
        var start = $"Write in {TailoredCvLanguageText.Name(target)}: set \"language\" to \"{code}\" and write the headline";
        return paragraphsInBulletLanguage
            ? start + " and the summary in that language. Keep each bullet and each paragraph in the language its bullets are supplied in; the user translates them."
            : start + ", the summary, and every paragraph in that language. Keep each bullet in the language it is supplied in; the user translates it.";
    }

    // The headline and wording rules of both external CV prompts; honesty lives in Essentials.
    // writesBullets: the AI words the bullets too, rather than copying them.
    internal static string ExternalWriting(bool writesBullets) => $$"""
        Set content.{{Headline}}
        For the summary, {{(writesBullets ? "bullets, " : "")}}and paragraphs: {{Voice}} {{Style}}
        """;

    // Checked like concise bullets (TailoredCvWording), with a few more linking words; a failing paragraph is dropped.
    internal const string ConciseParagraph = """
        For every entry with selected bullets, also return a short paragraph of at most 2 sentences (about 250
        characters) made from those selected bullets' own words, in the language they are supplied in: you may drop
        words and add at most 4 small linking words, but no other term and no number the bullets do not state.
        """;

    // The tailored CV's overview needs plain words of its own, so it is checked by TailoredCvWording.KeepsSourceFacts
    // against all of the entry's bullets: no new number or listing skill.
    internal const string ConciseListingParagraph = $"""
        For every entry with selected bullets, also return a short overview paragraph of at most 2 sentences (about
        250 characters): first say plainly what the role involved, based
        on its title and all of its bullets, not only the selected ones; then what carries over to the listing. For a
        role outside the listing's field, describe the work in general terms (for example that the candidate
        developed software in a team) and focus on transferable qualities such as collaboration, customer contact,
        responsibility, or routines, without product names, tools, or technical detail. {ParagraphOnly}
        State no fact, number, technology, or tool this entry's bullets do not state.
        """;

    // Checked by TailoredCvWording.KeepsSourceFacts instead: new words are fine, new numbers and skills are not.
    internal const string RewrittenConciseParagraph = """
        For every entry with selected bullets, also return a short paragraph of at most 2 sentences (about 250
        characters) that restates only those selected bullets' facts in plain words: no fact, number, technology, or
        tool the bullets do not state.
        """;

    // A mix of entries with and without a paragraph looks unfinished when the user switches to paragraph style.
    internal const string DetailedParagraph = """
        For every entry with selected bullets, also return a concise natural paragraph based only on those bullet
        facts; rephrase or combine facts, but do not infer facts or invent achievements; never leave paragraph null
        on an entry with selected bullets.
        """;

    // Selection keeps few, loosely related bullets for a role outside the listing's field; a paragraph made only
    // from those ("work training with language course") never says what the job was.
    internal const string ListingParagraph = $"""
        For every entry with selected bullets, also return a natural overview paragraph of 2-3 sentences;
        never leave paragraph null on such an entry. First say plainly what the role involved, based on its title and all of its
        bullets, not only the selected ones; then name the selected facts and the qualities those bullets show that
        carry over to the listing. For a role outside the listing's field, describe the work in general terms (for
        example that the candidate developed software in a team) and focus on transferable qualities such as
        collaboration, customer contact, responsibility, or routines, without product names, tools, or technical
        detail. {ParagraphOnly} Rephrase or combine facts, but do not infer facts or invent achievements.
        """;

    // An earlier "never make it the main content" made the AI drop the role's only bullet, and with it the paragraph.
    private const string ParagraphOnly = """
        Inside the paragraph, mention a work-training scheme or language course only after the work itself; this
        paragraph rule never changes which bullets you select.
        """;

    // The paragraph and timeline rules of both external CV prompts.
    internal static string Layout(TailoredCvTemplate template, bool rewrite = false, bool listing = false)
    {
        var concise = template == TailoredCvTemplate.Concise;
        var paragraph = !concise ? (listing ? ListingParagraph : DetailedParagraph)
            : rewrite ? RewrittenConciseParagraph : listing ? ConciseListingParagraph : ConciseParagraph;
        return $"""
            {paragraph} Set content.contentStyle to BULLETS by default so the user can choose the presentation for
            the whole CV. {Timeline}
            """;
    }

    // Dates, places, project kinds, skill levels, language levels and entry order come from the profile on preview
    // (TailoredCvDraftValidation.ApplySourceFacts), so the prompt doesn't ask the AI to copy them.
    internal const string OutputRules = """
        Include only languages from the profile.
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
