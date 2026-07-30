using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shortnr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGeoAndDeviceColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Browser",
                table: "ClickEvents",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BrowserVersion",
                table: "ClickEvents",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CityName",
                table: "ClickEvents",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "ClickEvents",
                type: "TEXT",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CountryName",
                table: "ClickEvents",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceFamily",
                table: "ClickEvents",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "ClickEvents",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "ClickEvents",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OSVersion",
                table: "ClickEvents",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OperatingSystem",
                table: "ClickEvents",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "ClickEvents",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Browser",
                table: "ClickEvents");

            migrationBuilder.DropColumn(
                name: "BrowserVersion",
                table: "ClickEvents");

            migrationBuilder.DropColumn(
                name: "CityName",
                table: "ClickEvents");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "ClickEvents");

            migrationBuilder.DropColumn(
                name: "CountryName",
                table: "ClickEvents");

            migrationBuilder.DropColumn(
                name: "DeviceFamily",
                table: "ClickEvents");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "ClickEvents");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "ClickEvents");

            migrationBuilder.DropColumn(
                name: "OSVersion",
                table: "ClickEvents");

            migrationBuilder.DropColumn(
                name: "OperatingSystem",
                table: "ClickEvents");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "ClickEvents");
        }
    }
}
