using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShortenedUrlMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShortenedUrlMetadatas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ShortenedUrlId = table.Column<long>(type: "INTEGER", nullable: false),
                    UtmSource = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    UtmMedium = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    UtmCampaign = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    UtmTerm = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    UtmContent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShortenedUrlMetadatas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShortenedUrlMetadatas_ShortenedUrls_ShortenedUrlId",
                        column: x => x.ShortenedUrlId,
                        principalTable: "ShortenedUrls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortenedUrlMetadatas_ShortenedUrlId",
                table: "ShortenedUrlMetadatas",
                column: "ShortenedUrlId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShortenedUrlMetadatas");
        }
    }
}
