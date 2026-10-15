using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class DropCoverLetterPlace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The letter's place field was dropped from ContentJson; the strict JSON reader rejects unknown keys.
            migrationBuilder.Sql("UPDATE CoverLetterDrafts SET ContentJson = json_remove(ContentJson, '$.place');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to restore: the place value is gone and the old code reads drafts without it.
        }
    }
}
