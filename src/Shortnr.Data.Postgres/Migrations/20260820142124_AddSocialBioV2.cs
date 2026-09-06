using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Shortnr.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSocialBioV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OgDescription",
                table: "ShortenedUrlMetadatas",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OgFetchedAtUtc",
                table: "ShortenedUrlMetadatas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OgImage",
                table: "ShortenedUrlMetadatas",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OgTitle",
                table: "ShortenedUrlMetadatas",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SocialAccounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    OwnerUserId = table.Column<long>(type: "bigint", nullable: true),
                    WorkspaceId = table.Column<long>(type: "bigint", nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Username = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FollowerCount = table.Column<long>(type: "bigint", nullable: true),
                    SubscriberCount = table.Column<long>(type: "bigint", nullable: true),
                    AccessTokenEncrypted = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    RefreshTokenEncrypted = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    TokenExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsLinked = table.Column<bool>(type: "boolean", nullable: false),
                    LastError = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    LastSuccessUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialAccounts_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SocialAccounts_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SocialPosts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SocialAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ExternalPostId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    MediaUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Permalink = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FetchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPosts_SocialAccounts_SocialAccountId",
                        column: x => x.SocialAccountId,
                        principalTable: "SocialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_OwnerUserId",
                table: "SocialAccounts",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_Provider_OwnerUserId",
                table: "SocialAccounts",
                columns: new[] { "Provider", "OwnerUserId" },
                unique: true,
                filter: "\"OwnerUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_Provider_WorkspaceId",
                table: "SocialAccounts",
                columns: new[] { "Provider", "WorkspaceId" },
                unique: true,
                filter: "\"OwnerUserId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_WorkspaceId",
                table: "SocialAccounts",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_SocialAccountId_ExternalPostId",
                table: "SocialPosts",
                columns: new[] { "SocialAccountId", "ExternalPostId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialPosts");

            migrationBuilder.DropTable(
                name: "SocialAccounts");

            migrationBuilder.DropColumn(
                name: "OgDescription",
                table: "ShortenedUrlMetadatas");

            migrationBuilder.DropColumn(
                name: "OgFetchedAtUtc",
                table: "ShortenedUrlMetadatas");

            migrationBuilder.DropColumn(
                name: "OgImage",
                table: "ShortenedUrlMetadatas");

            migrationBuilder.DropColumn(
                name: "OgTitle",
                table: "ShortenedUrlMetadatas");
        }
    }
}