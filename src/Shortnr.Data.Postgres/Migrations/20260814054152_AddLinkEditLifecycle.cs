using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddLinkEditLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAtUtc",
                table: "ShortenedUrls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ShortenedUrls",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "ShortenedUrls",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "ShortenedUrls",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "ShortenedUrls");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "ShortenedUrls");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "ShortenedUrls");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "ShortenedUrls");
        }
    }
}
