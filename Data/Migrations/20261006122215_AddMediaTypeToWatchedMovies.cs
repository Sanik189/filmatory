using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POP.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaTypeToWatchedMovies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WatchedMovies_UserId_MovieId",
                table: "WatchedMovies");

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "WatchedMovies",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_WatchedMovies_UserId_MovieId_Type",
                table: "WatchedMovies",
                columns: new[] { "UserId", "MovieId", "Type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WatchedMovies_UserId_MovieId_Type",
                table: "WatchedMovies");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "WatchedMovies");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedMovies_UserId_MovieId",
                table: "WatchedMovies",
                columns: new[] { "UserId", "MovieId" },
                unique: true);
        }
    }
}
