using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiInsights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShortenedUrlTags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShortenedUrlId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortenedUrlTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortenedUrlTags_ShortenedUrls_ShortenedUrlId",
                        column: x => x.ShortenedUrlId,
                        principalTable: "ShortenedUrls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagSuggestions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShortenedUrlId = table.Column<long>(type: "INTEGER", nullable: false),
                    SuggestedTag = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    ClickCount = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagSuggestions_ShortenedUrls_ShortenedUrlId",
                        column: x => x.ShortenedUrlId,
                        principalTable: "ShortenedUrls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortenedUrlTags_ShortenedUrlId_Name",
                table: "ShortenedUrlTags",
                columns: new[] { "ShortenedUrlId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagSuggestions_ShortenedUrlId_Status",
                table: "TagSuggestions",
                columns: new[] { "ShortenedUrlId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TagSuggestions_ShortenedUrlId_SuggestedTag",
                table: "TagSuggestions",
                columns: new[] { "ShortenedUrlId", "SuggestedTag" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShortenedUrlTags");

            migrationBuilder.DropTable(
                name: "TagSuggestions");
        }
    }
}
