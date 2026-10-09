using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "Profiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hobbies",
                table: "Profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Nationality",
                table: "Profiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkAuthorization",
                table: "Profiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "ProfileProjects",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "ProfileExperiences",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "ProfileEducations",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProfileReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Relationship = table.Column<string>(type: "TEXT", nullable: false),
                    Company = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    Phone = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileReferences_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileReferences_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileProjects_LocationId",
                table: "ProfileProjects",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileExperiences_LocationId",
                table: "ProfileExperiences",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileEducations_LocationId",
                table: "ProfileEducations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileReferences_ProfileId_Position",
                table: "ProfileReferences",
                columns: new[] { "ProfileId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileReferences_UserId",
                table: "ProfileReferences",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProfileEducations_Locations_LocationId",
                table: "ProfileEducations",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProfileExperiences_Locations_LocationId",
                table: "ProfileExperiences",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ProfileProjects_Locations_LocationId",
                table: "ProfileProjects",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Existing links are free-label links, which is what the Other kind is.
            migrationBuilder.Sql("""
                UPDATE Profiles
                SET Links = (SELECT json_group_array(json_insert(value, '$.Kind', 'Other')) FROM json_each(Profiles.Links))
                WHERE json_array_length(Links) > 0;
                """);

            // Skills already tagged on an entry join the profile's skills at the lowest level (see ProfileEntryMutations).
            migrationBuilder.Sql("""
                INSERT INTO ProfileSkills (ProfileId, SkillId, Level)
                SELECT t.ProfileId, t.SkillId, 1 FROM (
                    SELECT e.ProfileId, es.SkillId FROM ProfileExperienceSkills es JOIN ProfileExperiences e ON e.Id = es.ExperienceId
                    UNION
                    SELECT p.ProfileId, ps.SkillId FROM ProfileProjectSkills ps JOIN ProfileProjects p ON p.Id = ps.ProjectId
                ) t
                WHERE NOT EXISTS (SELECT 1 FROM ProfileSkills k WHERE k.ProfileId = t.ProfileId AND k.SkillId = t.SkillId);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProfileEducations_Locations_LocationId",
                table: "ProfileEducations");

            migrationBuilder.DropForeignKey(
                name: "FK_ProfileExperiences_Locations_LocationId",
                table: "ProfileExperiences");

            migrationBuilder.DropForeignKey(
                name: "FK_ProfileProjects_Locations_LocationId",
                table: "ProfileProjects");

            migrationBuilder.DropTable(
                name: "ProfileReferences");

            migrationBuilder.DropIndex(
                name: "IX_ProfileProjects_LocationId",
                table: "ProfileProjects");

            migrationBuilder.DropIndex(
                name: "IX_ProfileExperiences_LocationId",
                table: "ProfileExperiences");

            migrationBuilder.DropIndex(
                name: "IX_ProfileEducations_LocationId",
                table: "ProfileEducations");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "Hobbies",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "Nationality",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "WorkAuthorization",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "ProfileProjects");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "ProfileExperiences");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "ProfileEducations");
        }
    }
}
