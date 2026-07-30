using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Auth.Models.Migrations
{
    /// <inheritdoc />
    public partial class AddScholarGenerationsAndPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GenerationId",
                table: "AspNetUsers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScholarStatus",
                table: "AspNetUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ScholarGenerations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    StartsOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndsOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScholarGenerations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromotionBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Step = table.Column<int>(type: "integer", nullable: false),
                    GenerationId = table.Column<int>(type: "integer", nullable: true),
                    AffectedCount = table.Column<int>(type: "integer", nullable: false),
                    DeactivatedAlumni = table.Column<bool>(type: "boolean", nullable: false),
                    PerformedByUserId = table.Column<string>(type: "text", nullable: false),
                    PerformedByName = table.Column<string>(type: "text", nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevertedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevertedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionBatches_ScholarGenerations_GenerationId",
                        column: x => x.GenerationId,
                        principalTable: "ScholarGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PromotionBatchEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PromotionBatchId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    UserDisplayName = table.Column<string>(type: "text", nullable: false),
                    UserEmail = table.Column<string>(type: "text", nullable: true),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: false),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    PreviousTitle = table.Column<string>(type: "text", nullable: true),
                    PreviousIsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionBatchEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionBatchEntries_PromotionBatches_PromotionBatchId",
                        column: x => x.PromotionBatchId,
                        principalTable: "PromotionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_GenerationId",
                table: "AspNetUsers",
                column: "GenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_ScholarStatus_GenerationId",
                table: "AspNetUsers",
                columns: new[] { "ScholarStatus", "GenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBatchEntries_PromotionBatchId",
                table: "PromotionBatchEntries",
                column: "PromotionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBatches_GenerationId",
                table: "PromotionBatches",
                column: "GenerationId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBatches_PerformedAt",
                table: "PromotionBatches",
                column: "PerformedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScholarGenerations_Year",
                table: "ScholarGenerations",
                column: "Year",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_ScholarGenerations_GenerationId",
                table: "AspNetUsers",
                column: "GenerationId",
                principalTable: "ScholarGenerations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
            // Backfill the typed status from the historic free-text Title.
            //
            // Title has never been constrained, so its values vary ("Junior", "junior
            // scholar", "SENIOR", stray notes). Matching case-insensitively on a substring
            // maps the common cases; anything unrecognised deliberately stays Unassigned (0)
            // rather than being guessed into a real cohort, and the admin overview surfaces
            // that count so it can be corrected in bulk.
            migrationBuilder.Sql(@"
                UPDATE ""AspNetUsers"" SET ""ScholarStatus"" = 3
                    WHERE ""Title"" ILIKE '%alumn%';
                UPDATE ""AspNetUsers"" SET ""ScholarStatus"" = 2
                    WHERE ""Title"" ILIKE '%senior%' AND ""ScholarStatus"" = 0;
                UPDATE ""AspNetUsers"" SET ""ScholarStatus"" = 1
                    WHERE ""Title"" ILIKE '%junior%' AND ""ScholarStatus"" = 0;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_ScholarGenerations_GenerationId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "PromotionBatchEntries");

            migrationBuilder.DropTable(
                name: "PromotionBatches");

            migrationBuilder.DropTable(
                name: "ScholarGenerations");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_GenerationId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_ScholarStatus_GenerationId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "GenerationId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ScholarStatus",
                table: "AspNetUsers");
        }
    }
}
