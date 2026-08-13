using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixFilteredIndexQuoting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShortenedUrls_DomainId_ShortCode",
                table: "ShortenedUrls");

            migrationBuilder.DropIndex(
                name: "IX_ShortenedUrls_ShortCode",
                table: "ShortenedUrls");

            migrationBuilder.CreateIndex(
                name: "IX_ShortenedUrls_DomainId_ShortCode",
                table: "ShortenedUrls",
                columns: new[] { "DomainId", "ShortCode" },
                unique: true,
                filter: "\"DomainId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShortenedUrls_ShortCode",
                table: "ShortenedUrls",
                column: "ShortCode",
                unique: true,
                filter: "\"DomainId\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShortenedUrls_DomainId_ShortCode",
                table: "ShortenedUrls");

            migrationBuilder.DropIndex(
                name: "IX_ShortenedUrls_ShortCode",
                table: "ShortenedUrls");

            migrationBuilder.CreateIndex(
                name: "IX_ShortenedUrls_DomainId_ShortCode",
                table: "ShortenedUrls",
                columns: new[] { "DomainId", "ShortCode" },
                unique: true,
                filter: "[DomainId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ShortenedUrls_ShortCode",
                table: "ShortenedUrls",
                column: "ShortCode",
                unique: true,
                filter: "[DomainId] IS NULL");
        }
    }
}
