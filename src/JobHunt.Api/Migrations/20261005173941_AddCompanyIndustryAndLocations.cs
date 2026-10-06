using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyIndustryAndLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyLocations",
                columns: table => new
                {
                    CompanyId = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyLocations", x => new { x.CompanyId, x.LocationId });
                    table.ForeignKey(
                        name: "FK_CompanyLocations_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanyLocations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Industries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Industries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Industries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLocations_LocationId",
                table: "CompanyLocations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Industries_UserId_NormalizedName",
                table: "Industries",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true);

            // The old single head office becomes the company's only location. Must run before the column is dropped.
            migrationBuilder.Sql(
                "INSERT INTO CompanyLocations (CompanyId, LocationId) SELECT Id, LocationId FROM Companies WHERE LocationId IS NOT NULL");

            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Locations_LocationId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_LocationId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Companies");

            migrationBuilder.AddColumn<int>(
                name: "IndustryId",
                table: "Companies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Companies_IndustryId",
                table: "Companies",
                column: "IndustryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Industries_IndustryId",
                table: "Companies",
                column: "IndustryId",
                principalTable: "Industries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Companies_Industries_IndustryId",
                table: "Companies");

            migrationBuilder.DropIndex(
                name: "IX_Companies_IndustryId",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "IndustryId",
                table: "Companies");

            migrationBuilder.AddColumn<int>(
                name: "LocationId",
                table: "Companies",
                type: "INTEGER",
                nullable: true);

            // Best effort: one head office can't hold several locations, so keep the first.
            migrationBuilder.Sql(
                "UPDATE Companies SET LocationId = (SELECT MIN(LocationId) FROM CompanyLocations WHERE CompanyId = Companies.Id)");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_LocationId",
                table: "Companies",
                column: "LocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Companies_Locations_LocationId",
                table: "Companies",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.DropTable(
                name: "CompanyLocations");

            migrationBuilder.DropTable(
                name: "Industries");
        }
    }
}
