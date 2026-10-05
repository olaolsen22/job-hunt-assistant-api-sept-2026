using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AllowReapplyToListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Listings_UserId_Url",
                table: "Listings");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_UserId_Url",
                table: "Listings",
                columns: new[] { "UserId", "Url" },
                unique: true,
                filter: "\"Status\" NOT IN ('Rejected', 'Withdrawn', 'Accepted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Listings_UserId_Url",
                table: "Listings");

            migrationBuilder.CreateIndex(
                name: "IX_Listings_UserId_Url",
                table: "Listings",
                columns: new[] { "UserId", "Url" },
                unique: true);
        }
    }
}
