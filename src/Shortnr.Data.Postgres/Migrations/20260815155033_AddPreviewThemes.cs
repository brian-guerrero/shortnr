using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Postgres.Migrations
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
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewTheme",
                table: "ShortenedUrls",
                type: "character varying(32)",
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
