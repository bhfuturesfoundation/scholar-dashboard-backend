using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddVerifiedGameScores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BestCombo",
                table: "GameScores",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "GameScores",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "GameScores",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionId",
                table: "GameScores",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Verified",
                table: "GameScores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_GameScores_GameId_Verified_Score",
                table: "GameScores",
                columns: new[] { "GameId", "Verified", "Score" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GameScores_GameId_Verified_Score",
                table: "GameScores");

            migrationBuilder.DropColumn(
                name: "BestCombo",
                table: "GameScores");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "GameScores");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "GameScores");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "GameScores");

            migrationBuilder.DropColumn(
                name: "Verified",
                table: "GameScores");
        }
    }
}
