using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddSkillSatisfactionRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SatisfactionRating",
                table: "Skills",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SatisfactionRating",
                table: "Skills");
        }
    }
}
