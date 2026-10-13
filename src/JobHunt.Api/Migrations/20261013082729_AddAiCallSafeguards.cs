using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobHunt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCallSafeguards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiOperationId",
                table: "LlmUsages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptOrdinal",
                table: "LlmUsages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_ParseJobs_Id_UserId",
                table: "ParseJobs",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateTable(
                name: "AiOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedRequestDigest = table.Column<string>(type: "TEXT", nullable: false),
                    ProtectedWorkDigest = table.Column<string>(type: "TEXT", nullable: true),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    CredentialId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorCode = table.Column<string>(type: "TEXT", nullable: true),
                    ParseJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiOperations", x => x.Id);
                    table.UniqueConstraint("AK_AiOperations_Id_UserId", x => new { x.Id, x.UserId });
                    table.ForeignKey(
                        name: "FK_AiOperations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiOperations_ParseJobs_ParseJobId_UserId",
                        columns: x => new { x.ParseJobId, x.UserId },
                        principalTable: "ParseJobs",
                        principalColumns: new[] { "Id", "UserId" });
                });

            migrationBuilder.CreateTable(
                name: "UserLlmLimits",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    DailyRequestLimit = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLlmLimits", x => new { x.UserId, x.Provider });
                    table.CheckConstraint("CK_UserLlmLimits_Nonnegative", "\"DailyRequestLimit\" IS NULL OR \"DailyRequestLimit\" >= 0");
                    table.ForeignKey(
                        name: "FK_UserLlmLimits_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsages_AiOperationId_AttemptOrdinal",
                table: "LlmUsages",
                columns: new[] { "AiOperationId", "AttemptOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsages_AiOperationId_UserId",
                table: "LlmUsages",
                columns: new[] { "AiOperationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsages_UserId_Provider_KeySource_CreatedAt",
                table: "LlmUsages",
                columns: new[] { "UserId", "Provider", "KeySource", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_LlmUsages_AttemptIdentity",
                table: "LlmUsages",
                sql: "(\"AiOperationId\" IS NULL AND \"AttemptOrdinal\" IS NULL) OR (\"AiOperationId\" IS NOT NULL AND \"AttemptOrdinal\" IS NOT NULL AND \"AttemptOrdinal\" >= 0)");

            migrationBuilder.CreateIndex(
                name: "IX_AiOperations_ParseJobId_UserId",
                table: "AiOperations",
                columns: new[] { "ParseJobId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiOperations_UserId",
                table: "AiOperations",
                column: "UserId",
                unique: true,
                filter: "\"Status\" = 'Running'");

            migrationBuilder.CreateIndex(
                name: "IX_AiOperations_UserId_RequestId",
                table: "AiOperations",
                columns: new[] { "UserId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserLlmLimits_UserId",
                table: "UserLlmLimits",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_LlmUsages_AiOperations_AiOperationId_UserId",
                table: "LlmUsages",
                columns: new[] { "AiOperationId", "UserId" },
                principalTable: "AiOperations",
                principalColumns: new[] { "Id", "UserId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LlmUsages_AiOperations_AiOperationId_UserId",
                table: "LlmUsages");

            migrationBuilder.DropTable(
                name: "AiOperations");

            migrationBuilder.DropTable(
                name: "UserLlmLimits");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_ParseJobs_Id_UserId",
                table: "ParseJobs");

            migrationBuilder.DropIndex(
                name: "IX_LlmUsages_AiOperationId_AttemptOrdinal",
                table: "LlmUsages");

            migrationBuilder.DropIndex(
                name: "IX_LlmUsages_AiOperationId_UserId",
                table: "LlmUsages");

            migrationBuilder.DropIndex(
                name: "IX_LlmUsages_UserId_Provider_KeySource_CreatedAt",
                table: "LlmUsages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LlmUsages_AttemptIdentity",
                table: "LlmUsages");

            migrationBuilder.DropColumn(
                name: "AiOperationId",
                table: "LlmUsages");

            migrationBuilder.DropColumn(
                name: "AttemptOrdinal",
                table: "LlmUsages");
        }
    }
}
