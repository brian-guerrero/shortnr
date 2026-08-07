using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartLinkDeepLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AndroidDeepLink",
                table: "ShortenedUrlMetadatas",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IosDeepLink",
                table: "ShortenedUrlMetadatas",
                type: "TEXT",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AndroidDeepLink",
                table: "ShortenedUrlMetadatas");

            migrationBuilder.DropColumn(
                name: "IosDeepLink",
                table: "ShortenedUrlMetadatas");
        }
    }
}
