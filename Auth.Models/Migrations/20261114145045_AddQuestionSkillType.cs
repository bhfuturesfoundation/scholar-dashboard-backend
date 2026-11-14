using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionSkillType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SkillType",
                table: "Questions",
                type: "integer",
                nullable: true);

            // Until now slot N meant "the skill question at position N", so the existing
            // questions in positions 1-4 keep exactly the meaning they had.
            migrationBuilder.Sql(@"
UPDATE ""Questions"" SET ""SkillType"" = ""Order""
WHERE ""IsSkill"" AND ""Order"" BETWEEN 1 AND 4;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SkillType",
                table: "Questions");
        }
    }
}
