using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBrandedDomains : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ShortenedUrls_ShortCode",
                table: "ShortenedUrls");

            migrationBuilder.AddColumn<long>(
                name: "DomainId",
                table: "ShortenedUrls",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Domains",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    OwnerUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    VerificationToken = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "datetime('now')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Domains", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Domains_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShortenedUrls_DomainId_ShortCode",
                table: "ShortenedUrls",
                columns: new[] { "DomainId", "ShortCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Domains_Hostname",
                table: "Domains",
                column: "Hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Domains_OwnerUserId",
                table: "Domains",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShortenedUrls_Domains_DomainId",
                table: "ShortenedUrls",
                column: "DomainId",
                principalTable: "Domains",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShortenedUrls_Domains_DomainId",
                table: "ShortenedUrls");

            migrationBuilder.DropTable(
                name: "Domains");

            migrationBuilder.DropIndex(
                name: "IX_ShortenedUrls_DomainId_ShortCode",
                table: "ShortenedUrls");

            migrationBuilder.DropColumn(
                name: "DomainId",
                table: "ShortenedUrls");

            migrationBuilder.CreateIndex(
                name: "IX_ShortenedUrls_ShortCode",
                table: "ShortenedUrls",
                column: "ShortCode",
                unique: true);
        }
    }
}
