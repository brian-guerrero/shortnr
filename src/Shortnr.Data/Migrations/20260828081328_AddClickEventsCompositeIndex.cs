using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClickEventsCompositeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClickEvents_ShortenedUrlId",
                table: "ClickEvents");

            migrationBuilder.CreateIndex(
                name: "IX_ClickEvents_ShortenedUrlId_ClickedAtUtc",
                table: "ClickEvents",
                columns: new[] { "ShortenedUrlId", "ClickedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClickEvents_ShortenedUrlId_ClickedAtUtc",
                table: "ClickEvents");

            migrationBuilder.CreateIndex(
                name: "IX_ClickEvents_ShortenedUrlId",
                table: "ClickEvents",
                column: "ShortenedUrlId");
        }
    }
}
