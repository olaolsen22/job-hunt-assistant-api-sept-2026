using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProviderKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeySource",
                table: "LlmUsages",
                type: "TEXT",
                nullable: false,
                defaultValue: "Shared");

            migrationBuilder.AddColumn<Guid>(
                name: "UserProviderKeyId",
                table: "LlmUsages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserProviderKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProtectedKey = table.Column<string>(type: "TEXT", nullable: true),
                    Last4 = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProviderKeys", x => x.Id);
                    table.UniqueConstraint("AK_UserProviderKeys_Id_UserId", x => new { x.Id, x.UserId });
                    table.CheckConstraint("CK_UserProviderKeys_SecretLifecycle", "(\"RetiredAt\" IS NULL AND \"ProtectedKey\" IS NOT NULL) OR (\"RetiredAt\" IS NOT NULL AND \"ProtectedKey\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_UserProviderKeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsages_KeySource_Provider_Model_CreatedAt",
                table: "LlmUsages",
                columns: new[] { "KeySource", "Provider", "Model", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsages_UserProviderKeyId_Provider_Model_CreatedAt",
                table: "LlmUsages",
                columns: new[] { "UserProviderKeyId", "Provider", "Model", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsages_UserProviderKeyId_UserId",
                table: "LlmUsages",
                columns: new[] { "UserProviderKeyId", "UserId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_LlmUsages_CredentialSource",
                table: "LlmUsages",
                sql: "(\"KeySource\" = 'Shared' AND \"UserProviderKeyId\" IS NULL) OR (\"KeySource\" = 'User' AND \"UserProviderKeyId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_UserProviderKeys_UserId_Provider",
                table: "UserProviderKeys",
                columns: new[] { "UserId", "Provider" },
                unique: true,
                filter: "\"RetiredAt\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_LlmUsages_UserProviderKeys_UserProviderKeyId_UserId",
                table: "LlmUsages",
                columns: new[] { "UserProviderKeyId", "UserId" },
                principalTable: "UserProviderKeys",
                principalColumns: new[] { "Id", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LlmUsages_UserProviderKeys_UserProviderKeyId_UserId",
                table: "LlmUsages");

            migrationBuilder.DropTable(
                name: "UserProviderKeys");

            migrationBuilder.DropIndex(
                name: "IX_LlmUsages_KeySource_Provider_Model_CreatedAt",
                table: "LlmUsages");

            migrationBuilder.DropIndex(
                name: "IX_LlmUsages_UserProviderKeyId_Provider_Model_CreatedAt",
                table: "LlmUsages");

            migrationBuilder.DropIndex(
                name: "IX_LlmUsages_UserProviderKeyId_UserId",
                table: "LlmUsages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LlmUsages_CredentialSource",
                table: "LlmUsages");

            migrationBuilder.DropColumn(
                name: "KeySource",
                table: "LlmUsages");

            migrationBuilder.DropColumn(
                name: "UserProviderKeyId",
                table: "LlmUsages");
        }
    }
}
