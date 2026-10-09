using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentLanguage",
                table: "Profiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "English");

            migrationBuilder.CreateTable(
                name: "ProfileBulletTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    BulletId = table.Column<int>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    SourceText = table.Column<string>(type: "TEXT", nullable: false),
                    IsMachine = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileBulletTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileBulletTranslations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileBulletTranslations_ProfileBullets_BulletId",
                        column: x => x.BulletId,
                        principalTable: "ProfileBullets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProfileTranslations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Language = table.Column<string>(type: "TEXT", nullable: false),
                    Headline = table.Column<string>(type: "TEXT", nullable: true),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    SourceHeadline = table.Column<string>(type: "TEXT", nullable: true),
                    SourceSummary = table.Column<string>(type: "TEXT", nullable: true),
                    IsMachine = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProfileTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProfileTranslations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProfileTranslations_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProfileBulletTranslations_BulletId_Language",
                table: "ProfileBulletTranslations",
                columns: new[] { "BulletId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileBulletTranslations_UserId",
                table: "ProfileBulletTranslations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProfileTranslations_ProfileId_Language",
                table: "ProfileTranslations",
                columns: new[] { "ProfileId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProfileTranslations_UserId",
                table: "ProfileTranslations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProfileBulletTranslations");

            migrationBuilder.DropTable(
                name: "ProfileTranslations");

            migrationBuilder.DropColumn(
                name: "ContentLanguage",
                table: "Profiles");
        }
    }
}
