namespace JobHunt.Api.Tests;

public class SchemaTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // schema.graphql is the contract the Nuxt UI reads and generates types from. When the schema changes,
    // this rewrites the file and fails once, so the change shows up in the diff and gets committed.
    [Fact]
    public async Task SchemaFile_IsUpToDate()
    {
        var client = await factory.GetOwnerClientAsync();
        var sdl = await client.GetStringAsync("/graphql?sdl");

        var path = Path.Combine(FindRepoRoot(), "schema.graphql");
        if (File.Exists(path) && File.ReadAllText(path) == sdl) return;

        File.WriteAllText(path, sdl);
        Assert.Fail("schema.graphql was out of date and has been regenerated. Review and commit it.");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "JobHunt.slnx")))
            dir = dir.Parent!;
        return dir.FullName;
    }
}
