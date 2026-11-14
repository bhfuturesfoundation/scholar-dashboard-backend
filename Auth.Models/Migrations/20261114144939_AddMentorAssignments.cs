using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddMentorAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MentorAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScholarId = table.Column<string>(type: "text", nullable: false),
                    MentorId = table.Column<string>(type: "text", nullable: false),
                    AllowJournalAccess = table.Column<bool>(type: "boolean", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MentorAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MentorAssignments_AspNetUsers_MentorId",
                        column: x => x.MentorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MentorAssignments_AspNetUsers_ScholarId",
                        column: x => x.ScholarId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MentorAssignments_MentorId",
                table: "MentorAssignments",
                column: "MentorId");

            migrationBuilder.CreateIndex(
                name: "IX_MentorAssignments_ScholarId_MentorId",
                table: "MentorAssignments",
                columns: new[] { "ScholarId", "MentorId" },
                unique: true);

            // Carry every existing pairing across, with the access the scholar had granted,
            // before the old column goes. Without this the deploy would unpair everyone.
            migrationBuilder.Sql(@"
INSERT INTO ""MentorAssignments"" (""ScholarId"", ""MentorId"", ""AllowJournalAccess"", ""AssignedAt"")
SELECT u.""Id"", u.""MentorId"", u.""AllowMentorJournalAccess"", COALESCE(u.""UpdatedAt"", u.""CreatedAt"", NOW())
FROM ""AspNetUsers"" u
JOIN ""AspNetUsers"" m ON m.""Id"" = u.""MentorId""
WHERE u.""MentorId"" IS NOT NULL AND u.""MentorId"" <> u.""Id"";");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_MentorId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_MentorId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "AllowMentorJournalAccess",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MentorId",
                table: "AspNetUsers");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowMentorJournalAccess",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MentorId",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_MentorId",
                table: "AspNetUsers",
                column: "MentorId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_MentorId",
                table: "AspNetUsers",
                column: "MentorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Back to one mentor per scholar: keep the earliest assignment.
            migrationBuilder.Sql(@"
UPDATE ""AspNetUsers"" u
SET ""MentorId"" = first.""MentorId"", ""AllowMentorJournalAccess"" = first.""AllowJournalAccess""
FROM (
    SELECT DISTINCT ON (""ScholarId"") ""ScholarId"", ""MentorId"", ""AllowJournalAccess""
    FROM ""MentorAssignments""
    ORDER BY ""ScholarId"", ""AssignedAt"", ""Id""
) first
WHERE u.""Id"" = first.""ScholarId"";");

            migrationBuilder.DropTable(
                name: "MentorAssignments");
        }
    }
}
