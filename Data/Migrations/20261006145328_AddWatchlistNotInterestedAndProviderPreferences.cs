using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POP.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchlistNotInterestedAndProviderPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NotInterested",
                table: "WatchedMovies",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PreferredWatchProviderIds",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WatchProviderFilterEnabled",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "WatchlistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    MovieId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    PosterUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchlistItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WatchlistItems_UserId_MovieId_Type",
                table: "WatchlistItems",
                columns: new[] { "UserId", "MovieId", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WatchlistItems");

            migrationBuilder.DropColumn(
                name: "NotInterested",
                table: "WatchedMovies");

            migrationBuilder.DropColumn(
                name: "PreferredWatchProviderIds",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "WatchProviderFilterEnabled",
                table: "AspNetUsers");
        }
    }
}
