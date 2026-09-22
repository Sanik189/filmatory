using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POP.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserYearRangePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreferredYearFrom",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreferredYearTo",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "YearFilterEnabled",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreferredYearFrom",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PreferredYearTo",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "YearFilterEnabled",
                table: "AspNetUsers");
        }
    }
}
