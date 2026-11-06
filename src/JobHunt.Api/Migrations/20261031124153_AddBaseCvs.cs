using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBaseCvs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BaseCvVersionId",
                table: "Documents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BaseCvs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaseCvs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BaseCvs_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BaseCvVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    BaseCvId = table.Column<int>(type: "INTEGER", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BaseCvVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BaseCvVersions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BaseCvVersions_BaseCvs_BaseCvId",
                        column: x => x.BaseCvId,
                        principalTable: "BaseCvs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BaseCvVersionId",
                table: "Documents",
                column: "BaseCvVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_BaseCvs_UserId_NormalizedName",
                table: "BaseCvs",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaseCvVersions_BaseCvId_Number",
                table: "BaseCvVersions",
                columns: new[] { "BaseCvId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BaseCvVersions_UserId",
                table: "BaseCvVersions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_BaseCvVersions_BaseCvVersionId",
                table: "Documents",
                column: "BaseCvVersionId",
                principalTable: "BaseCvVersions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_BaseCvVersions_BaseCvVersionId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "BaseCvVersions");

            migrationBuilder.DropTable(
                name: "BaseCvs");

            migrationBuilder.DropIndex(
                name: "IX_Documents_BaseCvVersionId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "BaseCvVersionId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Origin",
                table: "Documents");
        }
    }
}
