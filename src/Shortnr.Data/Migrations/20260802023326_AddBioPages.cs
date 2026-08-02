using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBioPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BioPages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OwnerUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    BioText = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Theme = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BioPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BioPages_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BioPageLinks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BioPageId = table.Column<long>(type: "INTEGER", nullable: false),
                    ShortenedUrlId = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IconUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsVisible = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BioPageLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BioPageLinks_BioPages_BioPageId",
                        column: x => x.BioPageId,
                        principalTable: "BioPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BioPageLinks_ShortenedUrls_ShortenedUrlId",
                        column: x => x.ShortenedUrlId,
                        principalTable: "ShortenedUrls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BioPageLinks_BioPageId_ShortenedUrlId",
                table: "BioPageLinks",
                columns: new[] { "BioPageId", "ShortenedUrlId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BioPageLinks_ShortenedUrlId",
                table: "BioPageLinks",
                column: "ShortenedUrlId");

            migrationBuilder.CreateIndex(
                name: "IX_BioPages_OwnerUserId",
                table: "BioPages",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BioPages_Slug",
                table: "BioPages",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BioPageLinks");

            migrationBuilder.DropTable(
                name: "BioPages");
        }
    }
}
