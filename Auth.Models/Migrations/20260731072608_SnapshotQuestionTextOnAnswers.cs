using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotQuestionTextOnAnswers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuestionTextSnapshot",
                table: "Answers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionTypeSnapshot",
                table: "Answers",
                type: "text",
                nullable: true);
            // Backfill every existing answer with the question's CURRENT wording.
            //
            // This is the best available approximation, not the truth: for answers written
            // before a question was last edited, the current wording is not what the scholar
            // actually saw. There is no record of the old text, so it cannot be recovered.
            // Stamping it now freezes the wording from this point forward, which is the whole
            // point — every edit after today stops rewriting history.
            migrationBuilder.Sql(@"
                UPDATE ""Answers"" a
                SET ""QuestionTextSnapshot"" = q.""Text"",
                    ""QuestionTypeSnapshot"" = q.""Type""
                FROM ""Questions"" q
                WHERE a.""QuestionId"" = q.""QuestionId""
                  AND a.""QuestionTextSnapshot"" IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuestionTextSnapshot",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "QuestionTypeSnapshot",
                table: "Answers");
        }
    }
}
