using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyResearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CareersUrl",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FoundedYear",
                table: "Companies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedInUrl",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrgNumber",
                table: "Companies",
                type: "TEXT",
                maxLength: 9,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResearchedAt",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SizeBand",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebsiteUrl",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkMode",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkingLanguage",
                table: "Companies",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanySkills",
                columns: table => new
                {
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    SkillId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanySkills", x => new { x.CompanyId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_CompanySkills_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanySkills_Skills_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Skills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Companies_UserId_OrgNumber",
                table: "Companies",
                columns: new[] { "UserId", "OrgNumber" },
                unique: true,
                filter: "\"OrgNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CompanySkills_SkillId",
                table: "CompanySkills",
                column: "SkillId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanySkills");

            migrationBuilder.DropIndex(
                name: "IX_Companies_UserId_OrgNumber",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "CareersUrl",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "FoundedYear",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "LinkedInUrl",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "OrgNumber",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "ResearchedAt",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "SizeBand",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WebsiteUrl",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WorkMode",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "WorkingLanguage",
                table: "Companies");
        }
    }
}
