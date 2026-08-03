using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceIdToDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "WorkspaceId",
                table: "Domains",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Domains_WorkspaceId",
                table: "Domains",
                column: "WorkspaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Domains_Workspaces_WorkspaceId",
                table: "Domains",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Domains_Workspaces_WorkspaceId",
                table: "Domains");

            migrationBuilder.DropIndex(
                name: "IX_Domains_WorkspaceId",
                table: "Domains");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Domains");
        }
    }
}
