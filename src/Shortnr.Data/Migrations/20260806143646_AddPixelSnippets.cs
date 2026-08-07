using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPixelSnippets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PixelId",
                table: "ShortenedUrlMetadatas",
                type: "TEXT",
                maxLength: 8192,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PixelSnippetId",
                table: "ShortenedUrlMetadatas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PixelSnippets",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SnippetTemplate = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    IsCustom = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PixelSnippets", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PixelSnippets",
                columns: new[] { "Id", "IsCustom", "Name", "SnippetTemplate" },
                values: new object[,]
                {
                    { 1L, false, "Meta Pixel", "<script>\n!function(f,b,e,v,n,t,s){if(f.fbq)return;n=f.fbq=function(){n.callMethod?n.callMethod.apply(n,arguments):n.queue.push(arguments)};if(!f._fbq)f._fbq=n;n.push=n;n.loaded=!0;n.version='2.0';n.queue=[];t=b.createElement(e);t.async=!0;t.src=v;s=b.getElementsByTagName(e)[0];s.parentNode.insertBefore(t,s)}(window,document,'script','https://connect.facebook.net/en_US/fbevents.js');\nfbq('init', '{{PIXEL_ID}}');\nfbq('track', 'PageView');\n</script>" },
                    { 2L, false, "Google Ads", "<script async src=\"https://www.googletagmanager.com/gtag/js?id={{PIXEL_ID}}\"></script>\n<script>\nwindow.dataLayer = window.dataLayer || [];\nfunction gtag(){dataLayer.push(arguments);}\ngtag('js', new Date());\ngtag('config', '{{PIXEL_ID}}');\n</script>" },
                    { 3L, true, "Custom snippet", "" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortenedUrlMetadatas_PixelSnippetId",
                table: "ShortenedUrlMetadatas",
                column: "PixelSnippetId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShortenedUrlMetadatas_PixelSnippets_PixelSnippetId",
                table: "ShortenedUrlMetadatas",
                column: "PixelSnippetId",
                principalTable: "PixelSnippets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShortenedUrlMetadatas_PixelSnippets_PixelSnippetId",
                table: "ShortenedUrlMetadatas");

            migrationBuilder.DropTable(
                name: "PixelSnippets");

            migrationBuilder.DropIndex(
                name: "IX_ShortenedUrlMetadatas_PixelSnippetId",
                table: "ShortenedUrlMetadatas");

            migrationBuilder.DropColumn(
                name: "PixelId",
                table: "ShortenedUrlMetadatas");

            migrationBuilder.DropColumn(
                name: "PixelSnippetId",
                table: "ShortenedUrlMetadatas");
        }
    }
}
