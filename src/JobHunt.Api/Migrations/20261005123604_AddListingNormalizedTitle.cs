using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddListingNormalizedTitle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedTitle",
                table: "Listings",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Matches Names.Normalize for ASCII and æøå (SQLite's UPPER only folds ASCII). Titles are stored trimmed.
            migrationBuilder.Sql("""
                UPDATE Listings
                SET NormalizedTitle = UPPER(REPLACE(REPLACE(REPLACE(TRIM(Title), 'æ', 'Æ'), 'ø', 'Ø'), 'å', 'Å'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NormalizedTitle",
                table: "Listings");
        }
    }
}
