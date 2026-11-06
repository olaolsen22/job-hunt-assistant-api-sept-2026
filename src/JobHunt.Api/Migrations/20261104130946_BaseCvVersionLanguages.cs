using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class BaseCvVersionLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BaseCvVersions_BaseCvId_Number",
                table: "BaseCvVersions");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "BaseCvVersions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Every existing version keeps its base CV's language before that column goes.
            migrationBuilder.Sql("""
                UPDATE BaseCvVersions
                SET Language = (SELECT b.Language FROM BaseCvs b WHERE b.Id = BaseCvVersions.BaseCvId);
                """);

            migrationBuilder.DropColumn(
                name: "Language",
                table: "BaseCvs");

            migrationBuilder.CreateIndex(
                name: "IX_BaseCvVersions_BaseCvId_Language_Number",
                table: "BaseCvVersions",
                columns: new[] { "BaseCvId", "Language", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails on the unique index once a base CV has versions in two languages; restore a backup instead.
            migrationBuilder.DropIndex(
                name: "IX_BaseCvVersions_BaseCvId_Language_Number",
                table: "BaseCvVersions");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "BaseCvs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE BaseCvs
                SET Language = (SELECT v.Language FROM BaseCvVersions v WHERE v.BaseCvId = BaseCvs.Id ORDER BY v.Id LIMIT 1);
                """);

            migrationBuilder.DropColumn(
                name: "Language",
                table: "BaseCvVersions");

            migrationBuilder.CreateIndex(
                name: "IX_BaseCvVersions_BaseCvId_Number",
                table: "BaseCvVersions",
                columns: new[] { "BaseCvId", "Number" },
                unique: true);
        }
    }
}
