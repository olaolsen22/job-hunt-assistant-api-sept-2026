using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileEntryTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProfileEducationTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    EducationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Degree = table.Column<string>(type: "TEXT", nullable: true),
                    Field = table.Column<string>(type: "TEXT", nullable: true),
                    SourceDegree = table.Column<string>(type: "TEXT", nullable: true),
                    SourceField = table.Column<string>(type: "TEXT", nullable: true),
                    IsMachine = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileEducationTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileEducationTranslations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileEducationTranslations_ProfileEducations_EducationId",
                        column: x => x.EducationId,
                        principalTable: "ProfileEducations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileExperienceTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ExperienceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    SourceTitle = table.Column<string>(type: "TEXT", nullable: false),
                    IsMachine = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileExperienceTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileExperienceTranslations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileExperienceTranslations_ProfileExperiences_ExperienceId",
                        column: x => x.ExperienceId,
                        principalTable: "ProfileExperiences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileEducationTranslations_EducationId_Language",
                table: "ProfileEducationTranslations",
                columns: new[] { "EducationId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileEducationTranslations_UserId",
                table: "ProfileEducationTranslations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileExperienceTranslations_ExperienceId_Language",
                table: "ProfileExperienceTranslations",
                columns: new[] { "ExperienceId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileExperienceTranslations_UserId",
                table: "ProfileExperienceTranslations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileEducationTranslations");

            migrationBuilder.DropTable(
                name: "ProfileExperienceTranslations");
        }
    }
}
