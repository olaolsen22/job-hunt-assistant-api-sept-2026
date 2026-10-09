using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileBullets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfileBullets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ExperienceId = table.Column<int>(type: "INTEGER", nullable: true),
                    EducationId = table.Column<int>(type: "INTEGER", nullable: true),
                    CertificationId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileBullets", x => x.Id);
                    table.CheckConstraint("CK_ProfileBullets_OneEntry", "(\"ExperienceId\" IS NOT NULL) + (\"EducationId\" IS NOT NULL) + (\"CertificationId\" IS NOT NULL) + (\"ProjectId\" IS NOT NULL) = 1");
                    table.ForeignKey(
                        name: "FK_ProfileBullets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileBullets_ProfileCertifications_CertificationId",
                        column: x => x.CertificationId,
                        principalTable: "ProfileCertifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileBullets_ProfileEducations_EducationId",
                        column: x => x.EducationId,
                        principalTable: "ProfileEducations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileBullets_ProfileExperiences_ExperienceId",
                        column: x => x.ExperienceId,
                        principalTable: "ProfileExperiences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileBullets_ProfileProjects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "ProfileProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileBullets_CertificationId_Position",
                table: "ProfileBullets",
                columns: new[] { "CertificationId", "Position" },
                filter: "\"CertificationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileBullets_EducationId_Position",
                table: "ProfileBullets",
                columns: new[] { "EducationId", "Position" },
                filter: "\"EducationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileBullets_ExperienceId_Position",
                table: "ProfileBullets",
                columns: new[] { "ExperienceId", "Position" },
                filter: "\"ExperienceId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileBullets_ProjectId_Position",
                table: "ProfileBullets",
                columns: new[] { "ProjectId", "Position" },
                filter: "\"ProjectId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileBullets_UserId",
                table: "ProfileBullets",
                column: "UserId");

            // Move the text before the columns go: the description first, then the highlights in their old order.
            // Positions are renumbered from 0 per entry, so they stay consecutive when a blank was skipped.
            // Education, project and certification have no highlights; certification never had a description.
            migrationBuilder.Sql("""
                INSERT INTO ProfileBullets (UserId, Text, Position, ExperienceId)
                SELECT UserId, Text, ROW_NUMBER() OVER (PARTITION BY EntryId ORDER BY Ord) - 1, EntryId
                FROM (
                    SELECT UserId, TRIM(Description, ' ' || char(9) || char(10) || char(13)) AS Text, Id AS EntryId, -1 AS Ord
                    FROM ProfileExperiences
                    WHERE TRIM(COALESCE(Description, ''), ' ' || char(9) || char(10) || char(13)) <> ''
                    UNION ALL
                    SELECT e.UserId, TRIM(j.value, ' ' || char(9) || char(10) || char(13)), e.Id, j.key
                    FROM ProfileExperiences e, json_each(e.Highlights) j
                    WHERE json_valid(e.Highlights) AND TRIM(j.value, ' ' || char(9) || char(10) || char(13)) <> ''
                )
                ORDER BY EntryId, Ord;
                """);
            migrationBuilder.Sql("""
                INSERT INTO ProfileBullets (UserId, Text, Position, EducationId)
                SELECT UserId, TRIM(Description, ' ' || char(9) || char(10) || char(13)), 0, Id
                FROM ProfileEducations
                WHERE TRIM(COALESCE(Description, ''), ' ' || char(9) || char(10) || char(13)) <> '';
                """);
            migrationBuilder.Sql("""
                INSERT INTO ProfileBullets (UserId, Text, Position, ProjectId)
                SELECT UserId, TRIM(Description, ' ' || char(9) || char(10) || char(13)), 0, Id
                FROM ProfileProjects
                WHERE TRIM(COALESCE(Description, ''), ' ' || char(9) || char(10) || char(13)) <> '';
                """);

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ProfileProjects");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ProfileExperiences");

            migrationBuilder.DropColumn(
                name: "Highlights",
                table: "ProfileExperiences");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ProfileEducations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileBullets");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ProfileProjects",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ProfileExperiences",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Highlights",
                table: "ProfileExperiences",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ProfileEducations",
                type: "TEXT",
                nullable: true);
        }
    }
}
