using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shortnr.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddLlmUsageLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LlmUsageLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: true),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Operation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PromptTokens = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LlmUsageLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LlmUsageLogs_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageLogs_CreatedAtUtc",
                table: "LlmUsageLogs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LlmUsageLogs_OwnerUserId",
                table: "LlmUsageLogs",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LlmUsageLogs");
        }
    }
}
