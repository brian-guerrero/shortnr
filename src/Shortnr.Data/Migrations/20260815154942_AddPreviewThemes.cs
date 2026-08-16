using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviewThemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultPreviewTheme",
                table: "Workspaces",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewTheme",
                table: "ShortenedUrls",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultPreviewTheme",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "PreviewTheme",
                table: "ShortenedUrls");
        }
    }
}
