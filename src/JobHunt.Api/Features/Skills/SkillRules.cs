namespace JobHunt.Api.Features.Skills;

// One definition of "a skill" for the listing parser and the skill cleanup, so the parser stops creating what
// the cleanup would remove.
public static class SkillRules
{
    private const string Definition = """
        A skill is something a CV lists by name in its skills section. In software and IT that means a named
        language, framework, library, product, platform, tool or certification (C#, Spring Boot, PostgreSQL,
        Grafana, GitHub Actions, OpenShift) or an established named method or technique (Scrum, TDD, CI/CD, REST, OOP, RAG).
        In other fields: concrete trade and service skills, licences and certifications ("Truckførerbevis", "Kundeservice").
        Not skills:
        - broad fields and role areas ("Backend Development", "Frontend Development", "API Development", "Fullstack",
          "Software Development", "systemutvikling", "Data Analysis")
        - generic activities and responsibilities ("Debugging", "Monitoring", "Problem solving", "Agentic workflows")
        - qualities and concerns in plain words ("Observability", "Secure data handling", "Scalability", "Performance")
        - personality traits ("Team player", "Selvgående", "Strukturert")
        Test: would an experienced hiring manager accept it as a concrete, checkable line in a CV's skills section?
        A topic the text mentions is not a skill; the named tool behind it is (Grafana, not "Monitoring").
        """;

    public static string Describe(string indent = "") =>
        string.Join("\n", Definition.Split('\n').Select(line => indent + line));
}
