using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmModelSelection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "ParseJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserLlmSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DefaultModel = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLlmSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLlmSettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserLlmSettings_UserId",
                table: "UserLlmSettings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserLlmSettings");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "ParseJobs");
        }
    }
}
