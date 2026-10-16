using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSkillCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SkillCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SkillCategories_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SkillCategoryTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SkillCategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SkillCategoryTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SkillCategoryTranslations_SkillCategories_SkillCategoryId",
                        column: x => x.SkillCategoryId,
                        principalTable: "SkillCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Skills",
                type: "INTEGER",
                nullable: true);

            // Every existing user gets the former fixed categories (same rows as StarterSkillCategories), and each
            // skill and draft skill keeps its category: SortOrder doubles as the old enum's declaration order.
            migrationBuilder.Sql(StarterCte + """
                INSERT INTO SkillCategories (UserId, Name, NormalizedName, Description, SortOrder)
                SELECT u.Id, s.Name, s.NormalizedName, s.Description, s.SortOrder
                FROM AspNetUsers u CROSS JOIN starter s;
                """);
            migrationBuilder.Sql(StarterCte + """
                INSERT INTO SkillCategoryTranslations (SkillCategoryId, Language, Name)
                SELECT c.Id, 'Norwegian', s.Norwegian
                FROM SkillCategories c JOIN starter s ON s.SortOrder = c.SortOrder;
                """);
            migrationBuilder.Sql(StarterCte + """
                UPDATE Skills SET CategoryId = (
                    SELECT c.Id FROM SkillCategories c JOIN starter s ON s.SortOrder = c.SortOrder
                    WHERE c.UserId = Skills.UserId AND s.Stored = Skills.Category)
                WHERE Category IS NOT NULL;
                """);
            // Draft JSON: skills[].category ("CLOUD_DEVOPS") becomes skills[].categoryId, keeping the list order.
            migrationBuilder.Sql(StarterCte + """
                UPDATE TailoredCvDrafts SET ContentJson = json_set(ContentJson, '$.skills', (
                    SELECT json_group_array(json(CASE
                        WHEN json_type(k.value, '$.category') IS NULL THEN k.value
                        ELSE json_set(json_remove(k.value, '$.category'), '$.categoryId', (
                            SELECT c.Id FROM SkillCategories c JOIN starter s ON s.SortOrder = c.SortOrder
                            WHERE c.UserId = TailoredCvDrafts.UserId AND s.Json = json_extract(k.value, '$.category')))
                        END))
                    FROM (SELECT value FROM json_each(TailoredCvDrafts.ContentJson, '$.skills') ORDER BY key) k))
                WHERE json_type(ContentJson, '$.skills') = 'array';
                """);

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Skills");

            migrationBuilder.CreateIndex(
                name: "IX_Skills_CategoryId",
                table: "Skills",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SkillCategories_UserId_NormalizedName",
                table: "SkillCategories",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SkillCategoryTranslations_SkillCategoryId_Language",
                table: "SkillCategoryTranslations",
                columns: new[] { "SkillCategoryId", "Language" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Skills_SkillCategories_CategoryId",
                table: "Skills",
                column: "CategoryId",
                principalTable: "SkillCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        // Literal rather than read from StarterSkillCategories, so later edits there can't change this migration.
        // Stored = the old enum as saved in Skills.Category; Json = the same value in draft JSON.
        private const string StarterCte = """
            WITH starter(SortOrder, Stored, Json, Name, NormalizedName, Norwegian, Description) AS (VALUES
                (0, 'Language', 'LANGUAGE', 'Languages', 'LANGUAGES', 'Programmeringsspråk', 'programming, query and markup languages (C#, TypeScript, SQL, HTML/CSS); SQL belongs here, not under databases'),
                (1, 'Framework', 'FRAMEWORK', 'Frameworks', 'FRAMEWORKS', 'Rammeverk', 'frameworks and libraries (ASP.NET Core, EF Core, Vue, Nuxt, React)'),
                (2, 'Database', 'DATABASE', 'Databases', 'DATABASES', 'Databaser', 'database products and data stores (PostgreSQL, SQLite, Redis, MongoDB)'),
                (3, 'CloudDevops', 'CLOUD_DEVOPS', 'Cloud & DevOps', 'CLOUD & DEVOPS', 'Sky og DevOps', 'hosting, CI/CD, containers and infrastructure (Azure, AWS, Docker, GitHub Actions, Kubernetes)'),
                (4, 'Testing', 'TESTING', 'Testing', 'TESTING', 'Testing', 'test frameworks and tools (xUnit, NUnit, Jest, Vitest, Playwright, Cypress); TDD is a method, not testing'),
                (5, 'Ai', 'AI', 'AI & ML', 'AI & ML', 'KI og maskinlæring', 'AI, LLM and machine learning (OpenAI/Gemini APIs, Microsoft.Extensions.AI, LangChain, RAG, prompt engineering, PyTorch)'),
                (6, 'Tool', 'TOOL', 'Tools', 'TOOLS', 'Verktøy', 'developer tools and software (Git, Visual Studio, Figma, Jira, Postman)'),
                (7, 'Method', 'METHOD', 'Methods', 'METHODS', 'Metoder', 'practices and ways of working (Scrum, TDD, API design, code review)'))

            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Skills_SkillCategories_CategoryId",
                table: "Skills");

            migrationBuilder.DropTable(
                name: "SkillCategoryTranslations");

            migrationBuilder.DropTable(
                name: "SkillCategories");

            migrationBuilder.DropIndex(
                name: "IX_Skills_CategoryId",
                table: "Skills");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Skills");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Skills",
                type: "TEXT",
                nullable: true);
        }
    }
}
