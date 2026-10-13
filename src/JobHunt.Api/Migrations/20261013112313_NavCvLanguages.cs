using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <summary>
    /// Profile and draft languages move from (Language enum, CEFR level) to NAV's CV shape: any language name with
    /// written and spoken levels. Hand-written: EF's generated version gave every existing row Id 0. Existing rows keep
    /// their language; CEFR maps to A1-A2 Beginner, B1-B2 Good, C1-C2 Very good (Native is never inferred).
    /// Draft JSON gets the same language conversion and its "location" key renamed to "address".
    /// </summary>
    public partial class NavCvLanguages : Migration
    {
        private const string Proficiency = """
            CASE WHEN "Level" IN ('A1', 'A2') THEN 'Beginner' WHEN "Level" IN ('B1', 'B2') THEN 'Good' ELSE 'VeryGood' END
            """;

        // Draft JSON uses the API's SNAKE_CASE enum names and allows a missing level.
        private const string DraftProficiency = """
            CASE WHEN json_extract(value, '$.level') IN ('A1', 'A2') THEN 'BEGINNER'
                WHEN json_extract(value, '$.level') IN ('B1', 'B2') THEN 'GOOD'
                WHEN json_extract(value, '$.level') IS NULL THEN NULL ELSE 'VERY_GOOD' END
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE "ef_temp_ProfileLanguages" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_ProfileLanguages" PRIMARY KEY AUTOINCREMENT,
                    "ProfileId" INTEGER NOT NULL,
                    "Name" TEXT COLLATE NOCASE NOT NULL,
                    "Written" TEXT NOT NULL,
                    "Spoken" TEXT NOT NULL,
                    CONSTRAINT "FK_ProfileLanguages_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "Profiles" ("Id") ON DELETE CASCADE);
                """);
            migrationBuilder.Sql($"""
                INSERT INTO "ef_temp_ProfileLanguages" ("ProfileId", "Name", "Written", "Spoken")
                SELECT "ProfileId", "Language", {Proficiency}, {Proficiency} FROM "ProfileLanguages";
                """);
            migrationBuilder.Sql("""DROP TABLE "ProfileLanguages";""");
            migrationBuilder.Sql("""ALTER TABLE "ef_temp_ProfileLanguages" RENAME TO "ProfileLanguages";""");
            migrationBuilder.CreateIndex(
                name: "IX_ProfileLanguages_ProfileId_Name",
                table: "ProfileLanguages",
                columns: new[] { "ProfileId", "Name" },
                unique: true);

            // json() keeps the rebuilt array as JSON; a bare subquery result would be stored as a string.
            migrationBuilder.Sql($"""
                UPDATE "TailoredCvDrafts"
                SET "ContentJson" = json_set("ContentJson", '$.languages', json((
                    SELECT json_group_array(json_object(
                        'name', CASE json_extract(value, '$.language') WHEN 'NORWEGIAN' THEN 'Norwegian' ELSE 'English' END,
                        'written', {DraftProficiency},
                        'spoken', {DraftProficiency}))
                    FROM json_each("TailoredCvDrafts"."ContentJson", '$.languages'))))
                WHERE json_type("ContentJson", '$.languages') = 'array';
                """);
            migrationBuilder.Sql("""
                UPDATE "TailoredCvDrafts"
                SET "ContentJson" = json_remove(json_set("ContentJson", '$.address', json_extract("ContentJson", '$.location')), '$.location')
                WHERE json_type("ContentJson", '$.location') IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Lossy: only English/Norwegian rows survive, with spoken level mapped back to CEFR.
            migrationBuilder.Sql("""
                CREATE TABLE "ef_temp_ProfileLanguages" (
                    "ProfileId" INTEGER NOT NULL,
                    "Language" TEXT NOT NULL,
                    "Level" TEXT NOT NULL,
                    CONSTRAINT "PK_ProfileLanguages" PRIMARY KEY ("ProfileId", "Language"),
                    CONSTRAINT "FK_ProfileLanguages_Profiles_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "Profiles" ("Id") ON DELETE CASCADE);
                """);
            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO "ef_temp_ProfileLanguages" ("ProfileId", "Language", "Level")
                SELECT "ProfileId", CASE lower("Name") WHEN 'norwegian' THEN 'Norwegian' ELSE 'English' END,
                    CASE "Spoken" WHEN 'Beginner' THEN 'A2' WHEN 'Good' THEN 'B2' WHEN 'VeryGood' THEN 'C1' ELSE 'C2' END
                FROM "ProfileLanguages" WHERE lower("Name") IN ('english', 'norwegian');
                """);
            migrationBuilder.Sql("""DROP TABLE "ProfileLanguages";""");
            migrationBuilder.Sql("""ALTER TABLE "ef_temp_ProfileLanguages" RENAME TO "ProfileLanguages";""");
            migrationBuilder.Sql("""
                UPDATE "TailoredCvDrafts"
                SET "ContentJson" = json_set(json_remove("ContentJson", '$.address', '$.language', '$.dateOfBirth', '$.referencesOnRequest'),
                    '$.location', json_extract("ContentJson", '$.address'), '$.languages', json('[]'));
                """);
        }
    }
}
