using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddJobRoleCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "JobRoles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JobRoleCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobRoleCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobRoleCategories_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobRoles_CategoryId",
                table: "JobRoles",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_JobRoleCategories_UserId_NormalizedName",
                table: "JobRoleCategories",
                columns: new[] { "UserId", "NormalizedName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_JobRoles_JobRoleCategories_CategoryId",
                table: "JobRoles",
                column: "CategoryId",
                principalTable: "JobRoleCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            // Fixed upgrade data: future changes to the starter vocabulary must not alter this migration.
            migrationBuilder.Sql("""
                INSERT INTO "JobRoleCategories" ("UserId", "Name", "NormalizedName")
                SELECT u."Id", c."Name", upper(c."Name")
                FROM "AspNetUsers" u
                CROSS JOIN (
                    SELECT 'IT' AS "Name" UNION ALL SELECT 'Healthcare' UNION ALL SELECT 'Education'
                    UNION ALL SELECT 'Business & Administration' UNION ALL SELECT 'Sales & Marketing'
                    UNION ALL SELECT 'Logistics' UNION ALL SELECT 'Trades & Manufacturing' UNION ALL SELECT 'Hospitality'
                ) c;
                UPDATE "JobRoles"
                SET "CategoryId" = (
                    SELECT c."Id" FROM "JobRoleCategories" c
                    WHERE c."UserId" = "JobRoles"."UserId" AND c."NormalizedName" = 'IT'
                )
                WHERE "NormalizedName" IN (
                    'FRONTEND DEVELOPER', 'BACKEND DEVELOPER', 'FULL STACK DEVELOPER', 'MOBILE DEVELOPER',
                    'DEVOPS ENGINEER', 'SITE RELIABILITY ENGINEER', 'PLATFORM ENGINEER', 'CLOUD ENGINEER',
                    'DATA ENGINEER', 'DATA SCIENTIST', 'MACHINE LEARNING ENGINEER', 'QA ENGINEER',
                    'SECURITY ENGINEER', 'EMBEDDED DEVELOPER', 'SOFTWARE ARCHITECT'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JobRoles_JobRoleCategories_CategoryId",
                table: "JobRoles");

            migrationBuilder.DropTable(
                name: "JobRoleCategories");

            migrationBuilder.DropIndex(
                name: "IX_JobRoles_CategoryId",
                table: "JobRoles");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "JobRoles");
        }
    }
}
